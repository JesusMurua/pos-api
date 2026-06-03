using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Products;

/// <summary>
/// Integration coverage for the <c>ProductResponse.hasOrders</c> flag (the FE's
/// up-front gate for the delete action) and the toggle response now carrying
/// the product's relations.
/// </summary>
public class ProductHasOrdersTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductHasOrdersTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProducts_HasOrdersFlag_TrueWhenOrderItemExists()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var productId = await InsertProductAsync(ctx.BranchId, isAvailable: true);
        await InsertOrderWithItemAsync(ctx.BranchId, productId);

        var response = await client.GetAsync("/api/Products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var product = await FindProductInListAsync(response, productId);
        product.GetProperty("hasOrders").GetBoolean()
            .Should().BeTrue("the product has an OrderItem referencing it");
    }

    [Fact]
    public async Task GetProducts_HasOrdersFlag_FalseWhenNoOrders()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var productId = await InsertProductAsync(ctx.BranchId, isAvailable: true);

        var response = await client.GetAsync("/api/Products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var product = await FindProductInListAsync(response, productId);
        product.GetProperty("hasOrders").GetBoolean()
            .Should().BeFalse("the product has never been sold");
    }

    [Fact]
    public async Task GetProduct_HasOrdersFlag_TrueOnInactiveProduct_ViaGetById()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        // Inactive product: it would NOT appear in GET /api/Products, so the
        // flag is only observable through getById, which does not filter on
        // availability.
        var productId = await InsertProductAsync(ctx.BranchId, isAvailable: false);
        await InsertOrderWithItemAsync(ctx.BranchId, productId);

        var response = await client.GetAsync($"/api/Products/{productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("hasOrders").GetBoolean()
            .Should().BeTrue("a sold product keeps the fiscal lock even after deactivation");
    }

    [Fact]
    public async Task Toggle_PreservesRelations_SizesNotEmpty()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var productId = await InsertProductWithSizeAsync(ctx.BranchId);

        var response = await client.PatchAsync($"/api/Products/{productId}/toggle", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("sizes").GetArrayLength()
            .Should().BeGreaterThan(0, "the toggle response must carry the product's sizes");
    }

    #region Helpers

    private record TenantCtx(int BusinessId, int BranchId, int OwnerUserId);

    private async Task<(HttpClient Client, TenantCtx Ctx)> CreateAuthorizedClientForFreshOwnerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"HasOrd-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Enterprise,
            CountryCode = "MX",
            // Seeded MX default tax (ApplicationDbContext.HasData) so the
            // products/tax-resolution path can load a valid tax context.
            DefaultTaxId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = biz.Id,
            Name = $"Matrix-{suffix}",
            IsMatrix = true,
            IsActive = true,
            FolioCounter = 0,
            TimeZoneId = "America/Mexico_City",
            CreatedAt = DateTime.UtcNow
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var owner = new User
        {
            BusinessId = biz.Id,
            BranchId = branch.Id,
            Name = $"Owner-{suffix}",
            Email = $"hasord-{suffix}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OrdPass123!"),
            RoleId = UserRoleIds.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = JwtTestFactory.CreateUserToken(biz.Id, branch.Id, owner.Id, "Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, new TenantCtx(biz.Id, branch.Id, owner.Id));
    }

    private async Task<int> InsertCategoryAsync(ApplicationDbContext db, int branchId)
    {
        var category = new Category
        {
            BranchId = branchId,
            Name = "Test Category",
            Icon = "pi-star",
            SortOrder = 1,
            IsActive = true
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task<int> InsertProductAsync(int branchId, bool isAvailable)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var categoryId = await InsertCategoryAsync(db, branchId);

        var product = new Product
        {
            BranchId = branchId,
            CategoryId = categoryId,
            Name = "Sellable Product",
            PriceCents = 5000,
            IsAvailable = isAvailable
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<int> InsertProductWithSizeAsync(int branchId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var categoryId = await InsertCategoryAsync(db, branchId);

        var product = new Product
        {
            BranchId = branchId,
            CategoryId = categoryId,
            Name = "Product With Size",
            PriceCents = 8000,
            IsAvailable = true,
            Sizes = new List<ProductSize>
            {
                new() { Label = "Large", ExtraPriceCents = 2000 }
            }
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task InsertOrderWithItemAsync(int branchId, int productId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            BranchId = branchId,
            OrderNumber = 1,
            TotalCents = 5000,
            CreatedAt = DateTime.UtcNow,
            Items = new List<OrderItem>
            {
                new()
                {
                    ProductId = productId,
                    ProductName = "Sellable Product",
                    Quantity = 1,
                    UnitPriceCents = 5000
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> FindProductInListAsync(HttpResponseMessage response, int productId)
    {
        var body = await ReadJsonAsync(response);
        var match = body.RootElement.EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("id").GetInt32() == productId);
        match.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            $"product {productId} must appear in the active product list");
        return match;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    #endregion
}
