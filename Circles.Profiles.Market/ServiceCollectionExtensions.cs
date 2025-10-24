using Circles.Profiles.Aggregation;
using Microsoft.Extensions.DependencyInjection;

namespace Circles.Profiles.Market;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCirclesMarket(this IServiceCollection services)
    {
        services.AddSingleton<BasicAggregator>();
        services.AddSingleton<CatalogReducer>();
        services.AddSingleton<OperatorCatalogService>();
        services.AddSingleton<IMarketPublisher, MarketPublisher>();
        return services;
    }
}
