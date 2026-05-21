using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using POS.Domain.Models;

namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Substitutes for the default <see cref="IModelCustomizer"/> when the test
/// host uses the EF Core InMemory provider. Pre-registers a string-based
/// value converter for every <see cref="JsonDocument"/> scalar property
/// BEFORE the user's <c>OnModelCreating</c> runs, so the conventions
/// classify them as scalars (not navigations) and the InMemory provider
/// can persist them as plain text. The production Npgsql provider is not
/// affected — this customizer is only registered in
/// <see cref="CustomWebApplicationFactory"/>.
/// </summary>
internal sealed class InMemoryModelCustomizer : ModelCustomizer
{
    public InMemoryModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        var jsonConverter = new ValueConverter<JsonDocument?, string?>(
            v => v == null ? null : v.RootElement.GetRawText(),
            v => v == null ? null : JsonDocument.Parse(v, default));

        // Pre-register the conversion so EF Core's RelationshipDiscoveryConvention
        // does not try to treat these properties as navigations to JsonDocument
        // (which Npgsql resolves via its native jsonb mapping, but InMemory
        // cannot since it lacks a registered CLR-to-store type mapping for
        // JsonDocument).
        modelBuilder.Entity<Customer>().Property(c => c.ExtensionData).HasConversion(jsonConverter);
        modelBuilder.Entity<Order>().Property(o => o.ExtensionData).HasConversion(jsonConverter);
        modelBuilder.Entity<OrderItem>().Property(o => o.ExtensionData).HasConversion(jsonConverter);
        modelBuilder.Entity<OrderPayment>().Property(o => o.ExtensionData).HasConversion(jsonConverter);
        modelBuilder.Entity<Product>().Property(p => p.ExtensionData).HasConversion(jsonConverter);

        // Now let the production OnModelCreating run; its HasColumnType("jsonb")
        // calls are no-ops on the InMemory provider, but the conversion above
        // keeps the property as a string-mapped scalar.
        base.Customize(modelBuilder, context);
    }
}
