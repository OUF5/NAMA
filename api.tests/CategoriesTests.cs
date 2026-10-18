using System.Net;
using System.Text.Json.Nodes;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 7.5 categories and sub-categories: list and add (PLN-01, PLN-05, PRD 4.7), rename, archive and
// restore (STG-05). No DELETE (PRD 4.6).
public class CategoriesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private Task<HttpResponseMessage> AddCategory(string sub, JsonNode body) =>
        Send(factory, HttpMethod.Post, "/api/categories", sub, body);

    private Task<HttpResponseMessage> AddSubCategory(string sub, string? categoryId, string? name) =>
        Send(factory, HttpMethod.Post, "/api/sub-categories", sub, new JsonObject { ["categoryId"] = categoryId, ["name"] = name });

    private async Task<JsonArray> GetCategories(string sub)
    {
        var response = await Send(factory, HttpMethod.Get, "/api/categories", sub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).AsArray();
    }

    private static async Task AssertError(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.True(problem["errors"]!.AsObject().ContainsKey(field), problem.ToJsonString());
    }

    [Fact]
    public async Task Get_returns_categories_in_order_with_their_sub_categories()
    {
        var sub = await SetUpUser(factory);
        Assert.Equal(HttpStatusCode.Created, (await AddSubCategory(sub, "food", "Groceries")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddSubCategory(sub, "food", "Dining out")).StatusCode);

        var categories = await GetCategories(sub);

        Assert.Equal(["Food", "Transport"], categories.Select(c => c!["name"]!.GetValue<string>()));
        var food = categories[0]!;
        Assert.False(food["archived"]!.GetValue<bool>());
        Assert.Equal(
            [("Groceries", 0, "food", false), ("Dining out", 1, "food", false)],
            food["subCategories"]!.AsArray().Select(s => (
                s!["name"]!.GetValue<string>(), s["sortOrder"]!.GetValue<int>(),
                s["categoryId"]!.GetValue<string>(), s["archived"]!.GetValue<bool>())));
        Assert.Empty(categories[1]!["subCategories"]!.AsArray());
    }

    // PLN-05: a category added after setup is trimmed and goes last.
    [Fact]
    public async Task Add_category_trims_the_name_and_puts_it_last()
    {
        var sub = await SetUpUser(factory);

        var response = await AddCategory(sub, new JsonObject { ["name"] = "  Health " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await Json(response);
        Assert.Equal(("Health", 2), (created["name"]!.GetValue<string>(), created["sortOrder"]!.GetValue<int>()));
        Assert.Equal(32, created["id"]!.GetValue<string>().Length);
        Assert.Equal(["Food", "Transport", "Health"], (await GetCategories(sub)).Select(c => c!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_category_name_that_exists_ignoring_case_returns_400()
    {
        var sub = await SetUpUser(factory);

        await AssertError(await AddCategory(sub, new JsonObject { ["name"] = " FOOD" }), "Name");
        Assert.Equal(2, (await GetCategories(sub)).Count);
    }

    public static TheoryData<string?> InvalidNames => new() { null, "", "   ", new string('a', 41) };

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task Invalid_names_return_400(string? name)
    {
        var sub = await SetUpUser(factory);

        await AssertError(await AddCategory(sub, new JsonObject { ["name"] = name }), "Name");
        await AssertError(await AddSubCategory(sub, "food", name), "Name");
        Assert.Equal(2, (await GetCategories(sub)).Count);
        Assert.Empty((await GetCategories(sub))[0]!["subCategories"]!.AsArray());
    }

    // PRD 4.7: sub-category names are unique per category, ignoring case.
    [Fact]
    public async Task A_sub_category_name_is_unique_within_its_category_only()
    {
        var sub = await SetUpUser(factory);
        Assert.Equal(HttpStatusCode.Created, (await AddSubCategory(sub, "food", "Other")).StatusCode);

        await AssertError(await AddSubCategory(sub, "food", " other "), "Name");
        Assert.Equal(HttpStatusCode.Created, (await AddSubCategory(sub, "transport", "Other")).StatusCode);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData(null)]
    public async Task A_sub_category_needs_an_existing_category(string? categoryId)
    {
        var sub = await SetUpUser(factory);

        await AssertError(await AddSubCategory(sub, categoryId, "Groceries"), "CategoryId");
    }

    // AUTH-03: another user's category does not exist for this user.
    [Fact]
    public async Task Another_users_category_cannot_be_used()
    {
        var owner = await SetUpUser(factory);
        var created = await Json(await AddCategory(owner, new JsonObject { ["name"] = "Health" }));
        var other = await SetUpUser(factory);

        await AssertError(await AddSubCategory(other, created["id"]!.GetValue<string>(), "Doctor"), "CategoryId");
        Assert.Equal(["Food", "Transport"], (await GetCategories(other)).Select(c => c!["name"]!.GetValue<string>()));
    }

    private Task<HttpResponseMessage> Update(string sub, string url, string? name, bool? archived) =>
        Send(factory, HttpMethod.Put, url, sub, new JsonObject { ["name"] = name, ["archived"] = archived });

    // STG-05: rename, archive and restore a category. The answer has its sub-categories.
    [Fact]
    public async Task A_category_can_be_renamed_archived_and_restored()
    {
        var sub = await SetUpUser(factory);
        Assert.Equal(HttpStatusCode.Created, (await AddSubCategory(sub, "food", "Groceries")).StatusCode);

        var response = await Update(sub, "/api/categories/food", "  Groceries & food ", true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var category = await Json(response);
        Assert.Equal(("Groceries & food", true, 0, 1),
            (category["name"]!.GetValue<string>(), category["archived"]!.GetValue<bool>(), category["sortOrder"]!.GetValue<int>(),
                category["subCategories"]!.AsArray().Count));
        // The sub-category keeps its own flag (no cascade).
        Assert.False((await GetCategories(sub))[0]!["subCategories"]![0]!["archived"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "/api/categories/food", "Food", false)).StatusCode);
        Assert.False((await GetCategories(sub))[0]!["archived"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_renamed_category_must_stay_unique_ignoring_case()
    {
        var sub = await SetUpUser(factory);

        await AssertError(await Update(sub, "/api/categories/food", "TRANSPORT", false), "Name");
        Assert.Equal(HttpStatusCode.OK, (await Update(sub, "/api/categories/food", "FOOD", false)).StatusCode);
        await AssertError(await Update(sub, "/api/categories/food", "", false), "Name");
        await AssertError(await Update(sub, "/api/categories/food", "Food", null), "Archived");
        Assert.Equal(["FOOD", "Transport"], (await GetCategories(sub)).Select(c => c!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_sub_category_can_be_renamed_archived_and_restored_within_its_category()
    {
        var sub = await SetUpUser(factory);
        var groceries = (await Json(await AddSubCategory(sub, "food", "Groceries")))["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Created, (await AddSubCategory(sub, "food", "Dining out")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddSubCategory(sub, "transport", "Fuel")).StatusCode);

        await AssertError(await Update(sub, $"/api/sub-categories/{groceries}", "dining OUT", false), "Name");
        var response = await Update(sub, $"/api/sub-categories/{groceries}", " Fuel ", true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await Json(response);
        Assert.Equal(("Fuel", true, "food", 0),
            (updated["name"]!.GetValue<string>(), updated["archived"]!.GetValue<bool>(), updated["categoryId"]!.GetValue<string>(),
                updated["sortOrder"]!.GetValue<int>()));
        Assert.Equal(HttpStatusCode.OK, (await Update(sub, $"/api/sub-categories/{groceries}", "Fuel", false)).StatusCode);
        Assert.False((await GetCategories(sub))[0]!["subCategories"]![0]!["archived"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Unknown_or_other_users_items_return_404()
    {
        var owner = await SetUpUser(factory);
        var groceries = (await Json(await AddSubCategory(owner, "food", "Groceries")))["id"]!.GetValue<string>();
        var health = (await Json(await AddCategory(owner, new JsonObject { ["name"] = "Health" })))["id"]!.GetValue<string>();
        var other = await SetUpUser(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await Update(other, $"/api/categories/{health}", "Health", false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Update(other, $"/api/sub-categories/{groceries}", "Groceries", false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Update(owner, "/api/categories/missing", "X", false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Update(owner, "/api/sub-categories/missing", "X", false)).StatusCode);
    }

    // PRD 4.6: categories and sub-categories cannot be deleted.
    [Theory]
    [InlineData("/api/categories")]
    [InlineData("/api/categories/food")]
    [InlineData("/api/sub-categories")]
    [InlineData("/api/sub-categories/groceries")]
    public async Task There_is_no_delete(string url)
    {
        var sub = await SetUpUser(factory);

        var response = await Send(factory, HttpMethod.Delete, url, sub);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(2, (await GetCategories(sub)).Count);
    }

    [Theory]
    [InlineData("GET", "/api/categories")]
    [InlineData("POST", "/api/categories")]
    [InlineData("POST", "/api/sub-categories")]
    [InlineData("PUT", "/api/categories/food")]
    [InlineData("PUT", "/api/sub-categories/groceries")]
    public async Task Calls_without_a_token_return_401(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
