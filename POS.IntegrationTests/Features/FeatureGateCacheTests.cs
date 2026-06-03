using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Features;

/// <summary>
/// Verifies the generation-versioned feature caching: matrix edits are not
/// observed until <see cref="IFeatureGateService.InvalidateAll"/> is called,
/// and then take effect immediately (no TTL wait). Runs in its own factory so
/// the global matrix mutation does not leak to other test classes.
/// </summary>
public class FeatureGateCacheTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AccessControl = "RealtimeAccessControl";

    private readonly CustomWebApplicationFactory _factory;

    public FeatureGateCacheTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MatrixEdit_NotVisible_UntilInvalidateAll()
    {
        // health is an appointment cluster, not fitness, so access control is OFF.
        var businessId = await SeedHealthBusinessAsync();

        (await ResolveAsync(businessId)).Should().NotContain(AccessControl,
            "health is not the gym cluster — baseline OFF (also warms the cache)");

        // Make access control applicable to the health cluster, directly in the DB.
        await AddClusterRuleAsync(ClusterCodes.Health, FeatureIds.RealtimeAccessControl);

        (await ResolveAsync(businessId)).Should().NotContain(AccessControl,
            "the global matrix cache is still warm — the edit must not be visible yet");

        InvalidateAll();

        (await ResolveAsync(businessId)).Should().Contain(AccessControl,
            "InvalidateAll bumps the generation, so the next resolve reloads the edited matrix");
    }

    #region Helpers

    private async Task<IReadOnlyList<string>> ResolveAsync(int businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IFeatureGateService>();
        return await gate.GetEnabledFeaturesAsync(businessId);
    }

    private void InvalidateAll()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IFeatureGateService>().InvalidateAll();
    }

    private async Task AddClusterRuleAsync(string clusterCode, int featureId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ClusterFeatures.Add(new ClusterFeature { ClusterCode = clusterCode, FeatureId = featureId });
        await db.SaveChangesAsync();
    }

    private async Task<int> SeedHealthBusinessAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"CacheTest-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Pro,
            CountryCode = "MX",
            DefaultTaxId = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        var catalog = new BusinessTypeCatalog
        {
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            Name = $"Health-{suffix}",
            ClusterCode = ClusterCodes.Health
        };
        db.Set<BusinessTypeCatalog>().Add(catalog);
        await db.SaveChangesAsync();

        db.Set<BusinessGiro>().Add(new BusinessGiro { BusinessId = biz.Id, BusinessTypeId = catalog.Id });
        await db.SaveChangesAsync();

        return biz.Id;
    }

    #endregion
}
