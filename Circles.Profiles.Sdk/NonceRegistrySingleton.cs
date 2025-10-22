using Circles.Profiles.Interfaces;

namespace Circles.Profiles.Sdk;

public static class NonceRegistrySingleton
{
    public static INonceRegistry Instance => _instance.Value;

    private static readonly Lazy<INonceRegistry> _instance =
        new(() => new InMemoryNonceRegistry(), LazyThreadSafetyMode.ExecutionAndPublication);
}