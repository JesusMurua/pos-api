using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using POS.Repository;

namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the API in-process with an EF Core InMemory provider, deterministic
/// JWT secrets, and the production hosted workers disabled.
/// Each factory instance owns a unique InMemory database, so xUnit
/// <c>IClassFixture</c> grants per-test-class data isolation without
/// cross-fixture pollution.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"PosTests_{Guid.NewGuid():N}";

    /// <summary>
    /// Per-factory EF Core materialization counter wired into the test
    /// DbContext options. Tests assert on cache miss / hit semantics via
    /// <c>_factory.QueryCounter.Reset()</c> and <c>_factory.QueryCounter.Count</c>.
    /// Per-factory (not static) so xUnit parallel class execution does not
    /// produce cross-class counter interference.
    /// </summary>
    public EFQueryCounterInterceptor QueryCounter { get; } = new();

    static CustomWebApplicationFactory()
    {
        // Program.cs reads these env vars INLINE during top-level execution
        // (before any ConfigureAppConfiguration / ConfigureWebHost callbacks
        // get a chance to run), maps them into IConfiguration, and uses the
        // resulting values to construct singletons such as the StripeClient
        // and the JWT bearer signing key. Setting them here — at static
        // initialization, before the first factory instance is built —
        // guarantees they are present in the process environment by the
        // time Program.Main runs.
        Environment.SetEnvironmentVariable("JWT_SECRET", TestConstants.JwtSecret);
        Environment.SetEnvironmentVariable(
            "ACCESS_CONTROL_QR_TOKEN_HMAC_SECRET", TestConstants.AccessControlHmacSecret);
        Environment.SetEnvironmentVariable(
            "STRIPE_SECRET_KEY", "sk_test_dummy_integration_secret");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Anything except "Production" prevents the DATA_PROTECTION_KEYS_PATH
        // fail-fast in Program.cs and silences IsDevelopment-only test seeders.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Belt-and-suspenders on top of the env vars above: anything that
            // is read AFTER ConfigureAppConfiguration runs also sees the
            // override. The InMemory connection string is intentionally blank
            // because the InMemory provider replaces the DbContextOptions in
            // ConfigureTestServices below.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestConstants.JwtSecret,
                ["Jwt:Issuer"] = TestConstants.JwtIssuer,
                ["Jwt:Audience"] = TestConstants.JwtAudience,
                ["AccessControl:QrTokenHmacSecret"] = TestConstants.AccessControlHmacSecret,
                ["Stripe:SecretKey"] = "sk_test_dummy_integration_secret",
                ["ConnectionStrings:DefaultConnection"] = string.Empty
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceDbContextWithInMemory(services);
            RemoveProductionHostedServices(services);
        });
    }

    /// <summary>
    /// Removes the production DbContext registration (Npgsql provider +
    /// interceptors) and substitutes an InMemory provider scoped to this
    /// factory instance.
    /// AddDbContext registers three relevant descriptors:
    ///   - <c>IDbContextOptionsConfiguration&lt;ApplicationDbContext&gt;</c> (the
    ///     <c>UseNpgsql(...)</c> lambda — additive, so it must be cleared to
    ///     stop EF Core seeing two providers configured on the same options),
    ///   - <c>DbContextOptions&lt;ApplicationDbContext&gt;</c> (the resolved options),
    ///   - <c>ApplicationDbContext</c> itself (scoped instance).
    /// All three are removed before the InMemory replacement is added.
    /// </summary>
    private void ReplaceDbContextWithInMemory(IServiceCollection services)
    {
        services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
        services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<ApplicationDbContext>();

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseInMemoryDatabase(_databaseName);
            // Strip JsonDocument properties from the test model so the
            // InMemory provider can validate it — see InMemoryModelCustomizer.
            options.ReplaceService<IModelCustomizer, InMemoryModelCustomizer>();
        });

        // D1 fallback (BDD-021 §9 / Appendix C): the EF Core
        // IMaterializationInterceptor does NOT fire on the InMemory provider,
        // so cache miss / hit measurement is moved to the repository boundary
        // by decorating IUnitOfWork.Catalog with CountingCatalogRepository
        // inside CountingUnitOfWork.
        var unitOfWorkDescriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IUnitOfWork));
        if (unitOfWorkDescriptor is not null)
        {
            services.Remove(unitOfWorkDescriptor);
        }
        services.AddScoped<IUnitOfWork>(sp =>
        {
            var inner = ActivatorUtilities.CreateInstance<UnitOfWork>(sp);
            return new CountingUnitOfWork(inner, QueryCounter);
        });
    }

    /// <summary>
    /// Strips out the long-running background workers
    /// (<c>StripeEventProcessorWorker</c>, <c>PaymentWebhookProcessorWorker</c>,
    /// <c>KdsEventDispatcherWorker</c>) so tests boot cleanly without those
    /// loops competing for the shared InMemory database. Framework-provided
    /// <see cref="IHostedService"/> entries (Kestrel, request lifecycle) are
    /// preserved by matching on the POS.API.Workers namespace only.
    /// </summary>
    private static void RemoveProductionHostedServices(IServiceCollection services)
    {
        var posWorkers = services
            .Where(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType?.Namespace == "POS.API.Workers")
            .ToList();

        foreach (var descriptor in posWorkers)
        {
            services.Remove(descriptor);
        }
    }
}
