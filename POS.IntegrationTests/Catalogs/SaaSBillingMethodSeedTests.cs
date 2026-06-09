using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Catalogs;

/// <summary>
/// Locks the 7 system SaaS billing rails seeded by
/// <c>DbInitializer.UpsertSaaSBillingMethodsAsync</c>. These are code-owned
/// (IsSystem=true), reconciled on every startup. See
/// docs/saas-billing-architecture.md §4.1.
/// </summary>
public class SaaSBillingMethodSeedTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SaaSBillingMethodSeedTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_CreatesSevenSystemRails()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rails = await db.SaaSBillingMethods.OrderBy(r => r.SortOrder).ToListAsync();

        rails.Should().HaveCount(7);
        rails.Should().OnlyContain(r => r.IsSystem && r.IsActive);
        rails.Select(r => r.Code).Should().ContainInOrder(
            "Stripe", "BankTransfer", "OxxoPay", "Cash", "BankDeposit", "Check", "Other");
    }

    [Fact]
    public async Task Seed_RailBehaviorFlagsAreCorrect()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var byCode = await db.SaaSBillingMethods.ToDictionaryAsync(r => r.Code);

        // Automatic rails confirm via webhook; Stripe/OxxoPay carry the provider key.
        byCode["Stripe"].IsAutomatic.Should().BeTrue();
        byCode["Stripe"].ProviderKey.Should().Be("stripe");
        byCode["OxxoPay"].IsAutomatic.Should().BeTrue();
        byCode["OxxoPay"].ProviderKey.Should().Be("stripe");

        // Manual rails are operator-registered; reference-bearing ones require a folio.
        byCode["BankTransfer"].IsAutomatic.Should().BeFalse();
        byCode["BankTransfer"].RequiresReference.Should().BeTrue();
        byCode["BankDeposit"].RequiresReference.Should().BeTrue();
        byCode["Check"].RequiresReference.Should().BeTrue();
        byCode["Cash"].IsAutomatic.Should().BeFalse();
        byCode["Cash"].RequiresReference.Should().BeFalse();
        byCode["Other"].RequiresReference.Should().BeFalse();
        byCode["Cash"].ProviderKey.Should().BeNull();
    }
}
