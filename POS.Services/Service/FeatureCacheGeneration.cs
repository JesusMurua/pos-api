namespace POS.Services.Service;

/// <summary>
/// Process-wide monotonic counter that versions the feature-matrix caches.
/// Registered as a singleton so it survives across the scoped
/// <see cref="FeatureGateService"/> instances. Both the global-matrix cache key
/// and every per-business snapshot key embed the current value, so a single
/// <see cref="Bump"/> orphans them all at once — the backing of
/// <c>IFeatureGateService.InvalidateAll()</c> without an
/// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> prefix scan.
/// </summary>
public sealed class FeatureCacheGeneration
{
    private long _value;

    /// <summary>Current generation. Read atomically.</summary>
    public long Current => Interlocked.Read(ref _value);

    /// <summary>Advances the generation, invalidating every cached entry keyed by the previous value.</summary>
    public void Bump() => Interlocked.Increment(ref _value);
}
