using System.Text.Json;

namespace Circles.Market.Api.Cart;

public interface ICartValidator
{
    ValidationResult Validate(Basket basket, CancellationToken ct = default);
}

public class CartValidator : ICartValidator
{
    public ValidationResult Validate(Basket basket, CancellationToken ct = default)
    {
        var result = new ValidationResult { BasketId = basket.BasketId };
        var reqs = new List<ValidationRequirement>();
        var trace = new List<RuleTrace>();

        bool anyPhysical = basket.Items.Any(); // heuristic: any item implies physical by default
        if (anyPhysical)
        {
            var r = new ValidationRequirement
            {
                Id = "req:shipping-address",
                RuleId = "rule:shipping-address",
                Reason = "Physical item present",
                Slot = "shippingAddress",
                Path = "/shippingAddress",
                ExpectedTypes = new[] { "https://schema.org/PostalAddress" },
                Cardinality = new Cardinality { Min = 1, Max = 1 }
            };
            EvaluateSlot(r, basket.ShippingAddress);
            reqs.Add(r);
            trace.Add(new RuleTrace { Id = r.RuleId, Evaluated = true, Result = r.Status });
        }

        bool invoiceLikely = basket.BillingAddress is not null || basket.ContactPoint is not null;
        if (invoiceLikely)
        {
            var r = new ValidationRequirement
            {
                Id = "req:billing-address",
                RuleId = "rule:invoice",
                Reason = "Invoice requires billing address",
                Slot = "billingAddress",
                Path = "/billingAddress",
                ExpectedTypes = new[] { "https://schema.org/PostalAddress" },
                Cardinality = new Cardinality { Min = 1, Max = 1 }
            };
            EvaluateSlot(r, basket.BillingAddress);
            reqs.Add(r);
            trace.Add(new RuleTrace { Id = r.RuleId, Evaluated = true, Result = r.Status });
        }

        bool ageItems = basket.Items.Any(i => (i.OrderedItem.Sku ?? string.Empty).Contains("alcohol", StringComparison.OrdinalIgnoreCase)
                                              || (i.OrderedItem.Sku ?? string.Empty).Contains("tobacco", StringComparison.OrdinalIgnoreCase));
        if (ageItems)
        {
            var r = new ValidationRequirement
            {
                Id = "req:age-proof",
                RuleId = "rule:age-proof",
                Reason = "Restricted item present",
                Slot = "ageProof",
                Path = "/ageProof",
                ExpectedTypes = new[] { "https://schema.org/Person" },
                Cardinality = new Cardinality { Min = 1, Max = 1 }
            };
            EvaluatePerson(r, basket.AgeProof);
            reqs.Add(r);
            trace.Add(new RuleTrace { Id = r.RuleId, Evaluated = true, Result = r.Status });
        }

        // Basic structural checks -> 422 on malformed basket
        foreach (var (line, idx) in basket.Items.Select((x, i) => (x, i)))
        {
            if (line.OrderQuantity < 0)
            {
                throw new ArgumentException($"Negative orderQuantity at /items/{idx}/orderQuantity");
            }
            if (line.OrderQuantity == 0)
            {
                // mark as invalid shape rather than throwing
                var r = new ValidationRequirement
                {
                    Id = $"req:qty-{idx}",
                    RuleId = "rule:quantity-nonzero",
                    Reason = "Quantity must be >= 1",
                    Slot = "items[].orderQuantity",
                    Path = $"/items/{idx}/orderQuantity",
                    ExpectedTypes = new[] { "https://schema.org/Number" },
                    Cardinality = new Cardinality { Min = 1, Max = 1 },
                    Status = "invalidShape"
                };
                reqs.Add(r);
                trace.Add(new RuleTrace { Id = r.RuleId, Evaluated = true, Result = r.Status });
            }
        }

        result.Requirements = reqs;
        result.RuleTrace = trace;
        result.Missing = reqs.Where(r => r.Status == "missing").Select(r => new MissingSlot
        {
            Slot = r.Slot,
            Path = r.Path,
            ExpectedTypes = r.ExpectedTypes
        }).ToList();
        result.Valid = reqs.All(r => r.Status == "ok");
        return result;
    }

    private static void EvaluateSlot(ValidationRequirement r, PostalAddress? addr)
    {
        if (addr is null)
        {
            r.Status = "missing";
            return;
        }
        bool typeOk = addr.Type == "PostalAddress";
        if (!typeOk)
        {
            r.Status = "typeMismatch";
            r.FoundAt = r.Path;
            r.FoundType = addr.Type;
            return;
        }
        // Minimal shape requirement: postalCode present
        if (string.IsNullOrWhiteSpace(addr.PostalCode))
        {
            r.Status = "invalidShape";
            r.FoundAt = r.Path;
            r.FoundType = "https://schema.org/PostalAddress";
            return;
        }
        r.Status = "ok";
        r.FoundAt = r.Path;
        r.FoundType = "https://schema.org/PostalAddress";
    }

    private static void EvaluatePerson(ValidationRequirement r, PersonMinimal? p)
    {
        if (p is null)
        {
            r.Status = "missing";
            return;
        }
        if (p.Type != "Person")
        {
            r.Status = "typeMismatch";
            r.FoundAt = r.Path;
            r.FoundType = p.Type;
            return;
        }
        if (string.IsNullOrWhiteSpace(p.BirthDate))
        {
            r.Status = "invalidShape";
            r.FoundAt = r.Path;
            r.FoundType = "https://schema.org/Person";
            return;
        }
        r.Status = "ok";
        r.FoundAt = r.Path;
        r.FoundType = "https://schema.org/Person";
    }
}
