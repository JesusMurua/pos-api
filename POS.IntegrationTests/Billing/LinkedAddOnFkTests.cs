using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Billing;

/// <summary>
/// PR-4 closed the FK SubscriptionInvoiceItem.LinkedAddOnId → PlanAddOn (plain int? in PR-3).
/// Confirms an AddOn line item carrying a real PlanAddOn id round-trips. (InMemory does not
/// enforce FKs; the constraint itself is validated by migration + review.)
/// </summary>
public class LinkedAddOnFkTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LinkedAddOnFkTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task InvoiceItem_WithLinkedAddOnId_RoundTrips()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var addOnId = await db.PlanAddOns.Where(a => a.Code == "device_kds").Select(a => a.Id).FirstAsync();

        var invoice = new SubscriptionInvoice
        {
            SubscriptionId = 0, // standalone fixture row; FK to Subscription is not exercised here
            BusinessId = 0,
            InvoiceNumber = 99,
            Status = SubscriptionInvoiceStatus.Open,
            IssuedAtUtc = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(7),
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddMonths(1),
            SubtotalCents = 5000,
            TaxCents = 800,
            TotalCents = 5800,
            Currency = "MXN",
            Items = new List<SubscriptionInvoiceItem>
            {
                new()
                {
                    Description = "Pantalla KDS adicional",
                    Quantity = 1,
                    UnitAmountCents = 5000,
                    TotalAmountCents = 5000,
                    ItemType = SubscriptionInvoiceItemType.AddOn,
                    LinkedAddOnId = addOnId
                }
            }
        };
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var item = await db.SubscriptionInvoiceItems.AsNoTracking()
            .FirstAsync(i => i.InvoiceId == invoice.Id);
        item.LinkedAddOnId.Should().Be(addOnId);
        item.ItemType.Should().Be(SubscriptionInvoiceItemType.AddOn);
    }
}
