namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Thread-unsafe counter used by <c>CatalogApiTests</c> to assert that
/// warm-cache requests do not call into the catalog repository.
/// <para>
/// Originally designed as an EF Core <c>IMaterializationInterceptor</c>
/// (see BDD-021 D1). Runtime verification showed that the
/// <c>IMaterializationInterceptor.InitializingInstance</c> hook does NOT
/// fire on the EF Core InMemory provider used by the test host, so the
/// interceptor approach yielded a permanent zero count. The pre-approved
/// D1 fallback applies: counter is incremented by
/// <see cref="CountingCatalogRepository"/> — a decorator over
/// <see cref="POS.Repository.IRepository.ICatalogRepository"/> wrapped
/// into <see cref="CountingUnitOfWork"/> at DI registration time.
/// </para>
/// <para>
/// Filename preserved to keep BDD-021 references stable; type retains the
/// same public surface (<see cref="Count"/> + <see cref="Reset"/>) so
/// <see cref="CustomWebApplicationFactory.QueryCounter"/> consumers do
/// not change.
/// </para>
/// </summary>
public class EFQueryCounterInterceptor
{
    private int _count;

    /// <summary>
    /// Number of catalog repository invocations since the last <see cref="Reset"/>.
    /// xUnit serializes facts within one test class, so a plain int is safe;
    /// no <c>Interlocked</c> needed.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Resets the counter to zero. Tests asserting on cache miss / hit
    /// semantics call this in their Arrange step to isolate themselves
    /// from prior tests in the same class.
    /// </summary>
    public void Reset() => _count = 0;

    /// <summary>
    /// Increments the counter. Called by <see cref="CountingCatalogRepository"/>
    /// on every method invocation it forwards to the inner repository.
    /// </summary>
    internal void Increment() => _count++;
}
