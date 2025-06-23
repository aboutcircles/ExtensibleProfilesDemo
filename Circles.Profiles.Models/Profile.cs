namespace Circles.Profiles.Models;

public sealed record Profile
{
    public string SchemaVersion { get; init; } = "1.1";
    public string? PreviewImageUrl { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>namespace → *head-of-index* CID</summary>
    public Dictionary<string, string> Namespaces { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SigningKey> SigningKeys { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}