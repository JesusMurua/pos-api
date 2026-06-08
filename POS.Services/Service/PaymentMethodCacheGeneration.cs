namespace POS.Services.Service;

/// <summary>
/// Process-wide monotonic counter versioning the per-tenant cache of the public
/// <c>GET /api/payment-methods/available</c> endpoint. Registered as a singleton;
/// each cache key embeds the current value, so a single <see cref="Bump"/> orphans
/// every tenant's entry at once. Dedicated to payment methods (not reusing the
/// feature-cache generation) so the two systems invalidate independently.
/// </summary>
public sealed class PaymentMethodCacheGeneration
{
    private long _value;

    /// <summary>Current generation. Read atomically.</summary>
    public long Current => Interlocked.Read(ref _value);

    /// <summary>Advances the generation, invalidating every cached entry keyed by the previous value.</summary>
    public void Bump() => Interlocked.Increment(ref _value);
}
