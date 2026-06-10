using Microsoft.Extensions.DependencyInjection;
using POS.IntegrationTests.Infrastructure;
using POS.Services.Notifications;

namespace POS.IntegrationTests.Notifications;

/// <summary>
/// Locks the 15 code-owned es-MX templates: each renders a non-empty subject + html + text from
/// a complete payload, and the registry exposes them all.
/// </summary>
public class NotificationRenderTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public NotificationRenderTests(CustomWebApplicationFactory factory) => _factory = factory;

    public static IEnumerable<object[]> Templates() => new[]
    {
        new object[] { "Welcome", new Dictionary<string, string> { ["name"] = "Ana", ["businessName"] = "Taquería" } },
        new object[] { "InvoiceCreated", new Dictionary<string, string> { ["invoiceNumber"] = "5", ["totalPesos"] = "$149.00", ["dueDate"] = "2026-07-01" } },
        new object[] { "PaymentReceived", new Dictionary<string, string> { ["amountPesos"] = "$149.00", ["invoiceNumber"] = "5" } },
        new object[] { "PaymentOverdue", new Dictionary<string, string> { ["invoiceNumber"] = "5", ["totalPesos"] = "$149.00", ["daysLate"] = "3" } },
        new object[] { "PaymentFailed", new Dictionary<string, string>() },
        new object[] { "SubscriptionPriceChanged", new Dictionary<string, string> { ["beforePesos"] = "$149.00", ["afterPesos"] = "$199.00", ["effectiveDate"] = "2026-07-01" } },
        new object[] { "PlanChanged", new Dictionary<string, string> { ["oldPlan"] = "Basic", ["newPlan"] = "Pro" } },
        new object[] { "AddOnActivated", new Dictionary<string, string> { ["addOnName"] = "Pantalla KDS", ["quantity"] = "2" } },
        new object[] { "AddOnDeactivated", new Dictionary<string, string> { ["addOnName"] = "Pantalla KDS" } },
        new object[] { "TrialExpiring3d", new Dictionary<string, string> { ["plan"] = "Pro" } },
        new object[] { "TrialExpiring1d", new Dictionary<string, string> { ["plan"] = "Pro" } },
        new object[] { "TrialExpired", new Dictionary<string, string> { ["plan"] = "Pro" } },
        new object[] { "TrialConverted", new Dictionary<string, string> { ["plan"] = "Pro" } },
        new object[] { "Suspended", new Dictionary<string, string> { ["reason"] = "Falta de pago" } },
        new object[] { "Reactivated", new Dictionary<string, string>() },
    };

    [Theory]
    [MemberData(nameof(Templates))]
    public void Template_RendersNonEmpty(string code, Dictionary<string, string> payload)
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<INotificationTemplateRegistry>();

        var rendered = registry.Get(code).Render(payload);

        rendered.Subject.Should().NotBeNullOrWhiteSpace();
        rendered.BodyHtml.Should().Contain("<html");
        rendered.BodyText.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Registry_ExposesAllFifteen()
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<INotificationTemplateRegistry>();
        registry.All().Should().HaveCount(15);
    }

    [Fact]
    public void Welcome_IncludesTheName()
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<INotificationTemplateRegistry>();
        var rendered = registry.Get("Welcome").Render(new Dictionary<string, string> { ["name"] = "Ana", ["businessName"] = "Taquería" });
        rendered.BodyHtml.Should().Contain("Ana");
    }
}
