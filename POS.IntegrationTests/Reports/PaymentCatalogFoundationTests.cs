using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Reports;

/// <summary>
/// Locks the PR-A1 foundation seed: the system reconciles exactly the 9 payment
/// methods with their behavioral category, SAT code and flags.
/// </summary>
public class PaymentCatalogFoundationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaymentCatalogFoundationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_Reconciles_Nine_Methods_With_Category_And_Sat()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await db.PaymentMethodCatalogs.ToListAsync();
        rows.Should().HaveCount(9, "the four originals plus the five new methods");
        rows.Should().OnlyContain(r => r.IsSystem && r.IsActive);

        var byCode = rows.ToDictionary(r => r.Code);

        byCode["Cash"].Category.Should().Be(PaymentCategory.Cash);
        byCode["Cash"].SatPaymentFormCode.Should().Be("01");
        byCode["Cash"].SupportsOverpay.Should().BeTrue("only cash gives change");

        byCode["Card"].Category.Should().Be(PaymentCategory.Card);
        byCode["Card"].SatPaymentFormCode.Should().Be("04");

        byCode["Transfer"].Category.Should().Be(PaymentCategory.Digital);
        byCode["Transfer"].SatPaymentFormCode.Should().Be("03");

        byCode["Clip"].Category.Should().Be(PaymentCategory.Card);
        byCode["Clip"].ProviderKey.Should().Be("clip");

        byCode["MercadoPago"].Category.Should().Be(PaymentCategory.Digital);
        byCode["MercadoPago"].SatPaymentFormCode.Should().Be("04");

        byCode["BankTerminal"].Category.Should().Be(PaymentCategory.Card);

        byCode["StoreCredit"].Category.Should().Be(PaymentCategory.Credit);
        byCode["StoreCredit"].SatPaymentFormCode.Should().Be("05");
        byCode["StoreCredit"].RequiresCustomer.Should().BeTrue();

        byCode["LoyaltyPoints"].Category.Should().Be(PaymentCategory.Points);
        byCode["LoyaltyPoints"].SatPaymentFormCode.Should().Be("05");
        byCode["LoyaltyPoints"].RequiresCustomer.Should().BeTrue();
    }
}
