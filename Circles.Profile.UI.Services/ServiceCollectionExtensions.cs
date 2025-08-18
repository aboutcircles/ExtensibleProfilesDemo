using Circles.Profiles.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Circles.Profile.UI.Services;

/// <summary>
/// Extension methods for setting up profile services in an <see cref="IServiceCollection" />.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the profile services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection with profile services added.</returns>
    public static IServiceCollection AddProfileServices(this IServiceCollection services)
    {
        // Register the IpfsStore as a singleton
        services.AddSingleton<IIpfsStore, IpfsStore>();
        
        // Register the SignatureService as a scoped service
        services.AddScoped<SignatureService>();
        
        // Register the ProfileUpdateService as a scoped service
        services.AddScoped<ProfileUpdateService>();
        
        return services;
    }
}
