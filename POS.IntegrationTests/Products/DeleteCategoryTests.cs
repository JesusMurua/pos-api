using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Products;

/// <summary>
/// Integration coverage for <c>DELETE /api/Categories/{id}</c>: 204 when the
/// category is empty, and 409 <c>category_has_products</c> when any product is
/// attached — active OR inactive. The inactive case is the regression guard:
/// the old implementation only checked active products and would cascade-delete
/// inactive ones (dragging their fiscal/order history along).
/// </summary>
public class DeleteCategoryTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DeleteCategoryTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteCategory_NoProducts_Returns204()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var categoryId = await InsertCategoryAsync(ctx.BranchId);

        var response = await client.DeleteAsync($"/api/Categories/{categoryId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Categories.AnyAsync(c => c.Id == categoryId)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteCategory_WithActiveProducts_Returns409_productCount()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var categoryId = await InsertCategoryAsync(ctx.BranchId);
        await InsertProductAsync(ctx.BranchId, categoryId, isAvailable: true);
        await InsertProductAsync(ctx.BranchId, categoryId, isAvailable: true);

        var response = await client.DeleteAsync($"/api/Categories/{categoryId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("error").GetString().Should().Be("category_has_products");
        body.RootElement.GetProperty("productCount").GetInt32().Should().Be(2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Categories.AnyAsync(c => c.Id == categoryId))
            .Should().BeTrue("the category must survive a rejected delete");
    }

    [Fact]
    public async Task DeleteCategory_WithInactiveProducts_Returns409()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        var categoryId = await InsertCategoryAsync(ctx.BranchId);
        var productId = await InsertProductAsync(ctx.BranchId, categoryId, isAvailable: false);

        var response = await client.DeleteAsync($"/api/Categories/{categoryId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("error").GetString().Should().Be("category_has_products");
        body.RootElement.GetProperty("productCount").GetInt32().Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Categories.AnyAsync(c => c.Id == categoryId))
            .Should().BeTrue("an inactive product must still block the delete");
        (await db.Products.AnyAsync(p => p.Id == productId))
            .Should().BeTrue("the inactive product must NOT be cascade-deleted");
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
            Name = $"CatDel-{suffix}",
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
            Email = $"catdel-{suffix}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CatPass123!"),
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

    private async Task<int> InsertCategoryAsync(int branchId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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

    private async Task<int> InsertProductAsync(int branchId, int categoryId, bool isAvailable)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = new Product
        {
            BranchId = branchId,
            CategoryId = categoryId,
            Name = "Attached Product",
            PriceCents = 5000,
            IsAvailable = isAvailable
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    #endregion
}
