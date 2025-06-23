// Circles.Profiles.Sdk.Tests/TestUtils.cs
using System.Reflection;
using Circles.Profiles.Models;

namespace Circles.Profiles.Sdk.Tests;

/// <summary>Light-weight helpers that keep the tests readable.</summary>
internal static class TestUtils
{
    /// Asserts the value is not <see langword="null"/> and returns it
    internal static T NonNull<T>(this T? value)
    {
        Assert.That(value, Is.Not.Null);
        return value!;
    }

    /// Reflection shim so tests can inspect the writer’s owning profile without
    /// making the production code public.
    internal static Profile OwnerProfile(this NamespaceWriter writer)
    {
        var f = typeof(NamespaceWriter)
            .GetField("_ownerProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        return (Profile)(f?.GetValue(writer)
                         ?? throw new InvalidOperationException("field _ownerProfile not found"));
    }
}