using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Catalogs;

/// <summary>
/// Locks the seeded behavioral metadata of system payment methods produced by
/// <c>DbInitializer.UpsertPaymentMethodCatalogAsync</c> (the reconcile that runs
/// on every startup). System methods are code-owned, so this is the contract.
/// </summary>
public class PaymentMethodSeedTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaymentMethodSeedTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_Transfer_RequiresReference()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var transfer = await db.PaymentMethodCatalogs.FirstAsync(c => c.Code == "Transfer");

        // SPEI always carries a folio — the cashier must record it to reconcile later.
        transfer.RequiresReference.Should().BeTrue("SPEI/Transfer always has a folio");

        // Snapshot of the rest of the row so an accidental seed edit is caught.
        transfer.Category.Should().Be(PaymentCategory.Digital);
        transfer.SatPaymentFormCode.Should().Be("03");
        transfer.RequiresCustomer.Should().BeFalse();
        transfer.SupportsOverpay.Should().BeFalse();
        transfer.SupportsPartial.Should().BeTrue();
        transfer.IsActive.Should().BeTrue();
        transfer.IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task Seed_CashAndCardAndOther_DoNotRequireReference()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var byCode = await db.PaymentMethodCatalogs
            .Where(c => c.Code == "Cash" || c.Code == "Card" || c.Code == "Other")
            .ToDictionaryAsync(c => c.Code);

        byCode["Cash"].RequiresReference.Should().BeFalse();
        byCode["Card"].RequiresReference.Should().BeFalse("the terminal prints its own ticket");
        byCode["Other"].RequiresReference.Should().BeFalse("catch-all");
    }
}
