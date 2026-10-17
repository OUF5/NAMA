using System.Net;
using System.Text.Json.Nodes;
using static Api.Tests.ApiCalls;

namespace Api.Tests;

// PRD 7.5 categories and sub-categories: list and add (PLN-01, PLN-05, PRD 4.7). No DELETE (PRD 4.6).
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

    // PRD 4.6: categories and sub-categories cannot be deleted.
    [Theory]
    [InlineData("/api/categories")]
    [InlineData("/api/categories/food")]
    [InlineData("/api/sub-categories")]
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
    public async Task Calls_without_a_token_return_401(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
