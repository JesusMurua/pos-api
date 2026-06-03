using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Products;

/// <summary>
/// Integration coverage for <c>DELETE /api/Products/{id}</c>: the happy-path
/// 204 (with cascade removal of sizes/extras), the 409 guard when the product
/// has order history, and the 404 for an unknown id. Each test seeds a fresh
/// tenant so state never leaks across cases.
/// </summary>
public class DeleteProductTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DeleteProductTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Test 1 — No orders → 204 and the row is gone

    [Fact]
    public async Task DeleteProduct_NoOrders_Returns204()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var productId = await InsertProductAsync(ctx.BranchId);

        var response = await client.DeleteAsync($"/api/Products/{productId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Products.AnyAsync(p => p.Id == productId))
            .Should().BeFalse("the product must be hard-deleted from the database");
    }

    #endregion

    #region Test 2 — Product with order history → 409 product_has_orders

    [Fact]
    public async Task DeleteProduct_WithOrders_Returns409()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var productId = await InsertProductAsync(ctx.BranchId);
        await InsertOrderWithItemAsync(ctx.BranchId, productId);

        var response = await client.DeleteAsync($"/api/Products/{productId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("error").GetString()
            .Should().Be("product_has_orders");
        body.RootElement.GetProperty("orderCount").GetInt32()
            .Should().Be(1, "exactly one OrderItem references the product");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Products.AnyAsync(p => p.Id == productId))
            .Should().BeTrue("a product with sales history must survive the rejected delete");
    }

    #endregion

    #region Test 3 — Unknown id → 404

    [Fact]
    public async Task DeleteProduct_NotFound_Returns404()
    {
        var (client, _) = await CreateAuthorizedClientForFreshOwnerAsync();

        var response = await client.DeleteAsync("/api/Products/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Test 4 — Cascade removes sizes and extras

    [Fact]
    public async Task DeleteProduct_RemovesSizesAndExtras()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var (productId, sizeId, groupId, extraId) =
            await InsertProductWithSizesAndExtrasAsync(ctx.BranchId);

        var response = await client.DeleteAsync($"/api/Products/{productId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Products.AnyAsync(p => p.Id == productId)).Should().BeFalse();
        (await db.Set<ProductSize>().AnyAsync(s => s.Id == sizeId))
            .Should().BeFalse("ProductSize cascades on product delete");
        (await db.Set<ProductModifierGroup>().AnyAsync(g => g.Id == groupId))
            .Should().BeFalse("ProductModifierGroup cascades on product delete");
        (await db.Set<ProductExtra>().AnyAsync(e => e.Id == extraId))
            .Should().BeFalse("ProductExtra cascades through its modifier group");
    }

    #endregion

    #region Helpers

    private record TenantCtx(int BusinessId, int BranchId, int OwnerUserId);

    private async Task<(HttpClient Client, TenantCtx Ctx)> CreateAuthorizedClientForFreshOwnerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"DelTest-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Enterprise,
            CountryCode = "MX",
            DefaultTaxId = 0,
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
            Email = $"del-{suffix}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("DelPass123!"),
            RoleId = UserRoleIds.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = JwtTestFactory.CreateUserToken(
            businessId: biz.Id,
            branchId: branch.Id,
            userId: owner.Id,
            role: "Owner");
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

    private async Task<int> InsertProductAsync(int branchId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var categoryId = await InsertCategoryAsync(db, branchId);

        var product = new Product
        {
            BranchId = branchId,
            CategoryId = categoryId,
            Name = "Deletable Product",
            PriceCents = 5000,
            IsAvailable = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<(int ProductId, int SizeId, int GroupId, int ExtraId)>
        InsertProductWithSizesAndExtrasAsync(int branchId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var categoryId = await InsertCategoryAsync(db, branchId);

        var extra = new ProductExtra { Label = "Extra Cheese", PriceCents = 1500, SortOrder = 1 };
        var group = new ProductModifierGroup
        {
            Name = "Toppings",
            SortOrder = 1,
            MinSelectable = 0,
            MaxSelectable = 3,
            Extras = new List<ProductExtra> { extra }
        };
        var product = new Product
        {
            BranchId = branchId,
            CategoryId = categoryId,
            Name = "Product With Children",
            PriceCents = 8000,
            IsAvailable = true,
            Sizes = new List<ProductSize>
            {
                new() { Label = "Large", ExtraPriceCents = 2000 }
            },
            ModifierGroups = new List<ProductModifierGroup> { group }
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (product.Id, product.Sizes!.First().Id, group.Id, extra.Id);
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
                    ProductName = "Deletable Product",
                    Quantity = 1,
                    UnitPriceCents = 5000
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    #endregion
}
