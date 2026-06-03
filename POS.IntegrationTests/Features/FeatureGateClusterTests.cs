using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Features;

/// <summary>
/// Coverage for the sub-giro cluster axis added to the feature resolver and the
/// "every configured business carries a catalog sub-giro" invariant.
/// Resolves through <see cref="IFeatureGateService"/> directly (no JWT needed).
/// </summary>
public class FeatureGateClusterTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AccessControl = "RealtimeAccessControl";
    private const string Reminders = "AppointmentReminders";
    private const string Invoicing = "CfdiInvoicing";

    private readonly CustomWebApplicationFactory _factory;

    public FeatureGateClusterTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Gym_Fitness_Pro_Has_RealtimeAccessControl()
    {
        var businessId = await SeedBusinessWithClustersAsync(ClusterCodes.Fitness);
        var features = await ResolveAsync(businessId);

        features.Should().Contain(AccessControl, "fitness is the gym cluster that gates access control");
    }

    [Fact]
    public async Task Estetica_Beauty_Pro_No_AccessControl_But_Has_Reminders()
    {
        var businessId = await SeedBusinessWithClustersAsync(ClusterCodes.Beauty);
        var features = await ResolveAsync(businessId);

        features.Should().NotContain(AccessControl, "beauty is not a gym cluster");
        features.Should().Contain(Reminders, "beauty is an appointment-based cluster");
    }

    [Fact]
    public async Task MultiCluster_Fitness_And_Beauty_Has_AccessControl()
    {
        var businessId = await SeedBusinessWithClustersAsync(ClusterCodes.Fitness, ClusterCodes.Beauty);
        var features = await ResolveAsync(businessId);

        features.Should().Contain(AccessControl, "any matching cluster grants the feature (OR semantics)");
    }

    [Fact]
    public async Task Services_NoSubGiro_FailsClosed_On_ClusterGatedFeature()
    {
        var businessId = await SeedBusinessWithClustersAsync(/* none */);
        var features = await ResolveAsync(businessId);

        features.Should().NotContain(AccessControl,
            "a business with no clusters must not leak a cluster-gated feature (fail-closed)");
    }

    [Fact]
    public async Task FeatureWithoutClusterRule_Unaffected_By_Cluster()
    {
        var businessId = await SeedBusinessWithClustersAsync(ClusterCodes.Beauty);
        var features = await ResolveAsync(businessId);

        features.Should().Contain(Invoicing,
            "features without cluster rules keep pure Plan × Macro resolution");
    }

    [Fact]
    public async Task UpdateGiro_WithoutCatalogSubGiro_Throws()
    {
        var businessId = await SeedBusinessWithClustersAsync(ClusterCodes.Beauty);

        using var scope = _factory.Services.CreateScope();
        var business = scope.ServiceProvider.GetRequiredService<IBusinessService>();

        var act = async () => await business.UpdateGiroAsync(
            businessId, MacroCategoryIds.Services, Array.Empty<int>(), "solo texto libre");

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task CompleteOnboardingGuard_WithoutSubGiro_Throws()
    {
        var businessId = await SeedBusinessWithClustersAsync(/* none */);

        using var scope = _factory.Services.CreateScope();
        var business = scope.ServiceProvider.GetRequiredService<IBusinessService>();

        var act = async () => await business.EnsureCanCompleteOnboardingAsync(businessId);

        await act.Should().ThrowAsync<ValidationException>();
    }

    #region Helpers

    private async Task<IReadOnlyList<string>> ResolveAsync(int businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IFeatureGateService>();
        return await gate.GetEnabledFeaturesAsync(businessId);
    }

    /// <summary>
    /// Seeds a Pro / Services business and attaches one catalog sub-giro per
    /// supplied cluster code (each via a fresh BusinessTypeCatalog row).
    /// </summary>
    private async Task<int> SeedBusinessWithClustersAsync(params string[] clusterCodes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"FeatTest-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Pro,
            CountryCode = "MX",
            DefaultTaxId = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        for (var i = 0; i < clusterCodes.Length; i++)
        {
            var catalog = new BusinessTypeCatalog
            {
                PrimaryMacroCategoryId = MacroCategoryIds.Services,
                Name = $"SubGiro-{clusterCodes[i]}-{suffix}-{i}",
                ClusterCode = clusterCodes[i]
            };
            db.Set<BusinessTypeCatalog>().Add(catalog);
            await db.SaveChangesAsync();

            db.Set<BusinessGiro>().Add(new BusinessGiro
            {
                BusinessId = biz.Id,
                BusinessTypeId = catalog.Id
            });
        }

        await db.SaveChangesAsync();
        return biz.Id;
    }

    #endregion
}
