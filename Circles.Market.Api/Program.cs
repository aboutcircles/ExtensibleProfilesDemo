using System.Threading.RateLimiting;
using Circles.Market.Api;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Sdk;
using Nethereum.Web3;
using Circles.Profiles.Market;

var builder = WebApplication.CreateBuilder(args);

// Observability & config
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Memory cache with a global size cap (200 MiB)
builder.Services.AddMemoryCache(o => o.SizeLimit = 200 * 1024 * 1024);

// Rate limiting: simple fixed-window per-IP
builder.Services.AddRateLimiter(o =>
{
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        string key = ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            AutoReplenishment = true,
            QueueLimit = 0
        });
    });
});

var chainRpcUrl = Environment.GetEnvironmentVariable("RPC")
                  ?? throw new Exception("The RPC env variable is not set.");
var ipfsRpcUrl = Environment.GetEnvironmentVariable("IPFS_RPC_URL") ??
                 throw new Exception("The IPFS_RPC_URL env variable is not set.");
var ipfsRpcBearer = Environment.GetEnvironmentVariable("IPFS_RPC_BEARER") ??
                    throw new Exception("The IPFS_RPC_BEARER env variable is not set.");

// IPFS store: inner RPC client + CID-keyed caching proxy
builder.Services.AddSingleton<IIpfsStore>(_ => new IpfsRpcApiStore(ipfsRpcUrl, ipfsRpcBearer));
builder.Services.Decorate<IIpfsStore, CachingIpfsStore>();

// Chain + registry
builder.Services.AddSingleton<INameRegistry>(_ => new NameRegistry(chainRpcUrl));
builder.Services.AddSingleton<IChainApi>(_ => new EthereumChainApi(new Web3(chainRpcUrl), Helpers.DefaultChainId));

// Signature verification:
// Register one DefaultSignatureVerifier instance and expose it as both interfaces.
builder.Services.AddSingleton<DefaultSignatureVerifier>(sp =>
    new DefaultSignatureVerifier(sp.GetRequiredService<IChainApi>()));
builder.Services.AddSingleton<ISignatureVerifier>(sp =>
    sp.GetRequiredService<DefaultSignatureVerifier>());
builder.Services.AddSingleton<ISafeBytesVerifier>(sp =>
    sp.GetRequiredService<DefaultSignatureVerifier>());

// Aggregator service
builder.Services.AddSingleton<Circles.Profiles.Aggregation.BasicAggregator>();
builder.Services.AddSingleton<CatalogReducer>();
builder.Services.AddSingleton<OperatorCatalogService>();

// Optional writes support
builder.Services.AddSingleton<IMarketPublisher, MarketPublisher>();

// OpenAPI only in dev
builder.Services.AddOpenApi();

// CORS: allow all origins/headers/methods (for demo/tooling usage) with exposed pagination headers
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("X-Next-Cursor", "Link")
    );
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAll");
app.UseRateLimiter();

app.MapGet("/api/operator/{op}/catalog",
        (string op,
                long? chainId,
                long? start,
                long? end,
                int? pageSize,
                string? cursor,
                int? offset,
                HttpContext ctx,
                OperatorCatalogService opCatalog,
                CancellationToken ct)
            => OperatorCatalogEndpoint.Handle(op, chainId, start, end, pageSize, cursor, offset, ctx, opCatalog, ct))
    .WithName("OperatorAggregatedCatalog")
    .WithSummary(
        "Aggregates verified product/* links across many avatars under the operator namespace and returns a paged AggregatedCatalog.")
    .WithDescription(
        "Inputs: operator address path param; repeated ?avatars=...; optional chainId/start/end; cursor/offset pagination. Implements CPA rules (verification, chain domain, nonce replay, time window) and reduces to newest-first product catalog with tombstone support.")
    .WithOpenApi();

app.Run();