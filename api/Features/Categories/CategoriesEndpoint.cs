using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Features.Categories;

// PRD 7.5 categories and sub-categories: list and add (PLN-01, PLN-05). Rename and archive come
// with Settings (M8). There is never a DELETE (PRD 4.6).
public static class CategoriesEndpoint
{
    public record SubCategoryDto(string Id, string CategoryId, string Name, int SortOrder, bool Archived);

    public record CategoryDto(string Id, string Name, int SortOrder, bool Archived, IReadOnlyList<SubCategoryDto> SubCategories);

    public static IEndpointRouteBuilder MapCategories(this IEndpointRouteBuilder api)
    {
        // Archived ones are included and flagged.
        api.MapGet("/categories", async (ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var subCategories = await store.GetSubCategoriesAsync(uid);
            return (await store.GetCategoriesAsync(uid))
                .OrderBy(category => category.SortOrder)
                .Select(category => ToDto(category, subCategories))
                .ToList();
        });

        // PRD 4.7: unique ignoring case per user.
        api.MapPost("/categories", async (CategoryRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var categories = await store.GetCategoriesAsync(uid);
            if (categories.Any(category => Names.Lower(category.Name) == Names.Lower(request.Name)))
            {
                return Invalid(nameof(CategoryRequest.Name), "A category with this name already exists.");
            }
            var category = new Category(DocumentIds.New(), Names.Normalize(request.Name), NextSortOrder(categories.Select(c => c.SortOrder)));
            await store.AddCategoryAsync(uid, category);
            return Results.Created($"/api/categories/{category.Id}", ToDto(category, []));
        });

        // PRD 4.7: unique ignoring case per category.
        api.MapPost("/sub-categories", async (SubCategoryRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (!(await store.GetCategoriesAsync(uid)).Any(category => category.Id == request.CategoryId))
            {
                return Invalid(nameof(SubCategoryRequest.CategoryId), "Category is unknown.");
            }
            var siblings = (await store.GetSubCategoriesAsync(uid)).Where(sub => sub.CategoryId == request.CategoryId).ToList();
            if (siblings.Any(sub => Names.Lower(sub.Name) == Names.Lower(request.Name)))
            {
                return Invalid(nameof(SubCategoryRequest.Name), "This category already has a sub-category with this name.");
            }
            var subCategory = new SubCategory(DocumentIds.New(), request.CategoryId!, Names.Normalize(request.Name),
                NextSortOrder(siblings.Select(sub => sub.SortOrder)));
            await store.AddSubCategoryAsync(uid, subCategory);
            return Results.Created($"/api/sub-categories/{subCategory.Id}", ToSubDto(subCategory));
        });

        return api;
    }

    public static CategoryDto ToDto(Category category, IEnumerable<SubCategory> subCategories) => new(
        category.Id, category.Name, category.SortOrder, category.Archived,
        subCategories.Where(sub => sub.CategoryId == category.Id).OrderBy(sub => sub.SortOrder).Select(ToSubDto).ToList());

    private static SubCategoryDto ToSubDto(SubCategory sub) => new(sub.Id, sub.CategoryId, sub.Name, sub.SortOrder, sub.Archived);

    private static int NextSortOrder(IEnumerable<int> sortOrders) => sortOrders.DefaultIfEmpty(-1).Max() + 1;

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class CategoryRequest : IValidatableObject
{
    public string? Name { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Names.IsValid(Name))
        {
            yield return new ValidationResult("Name is 1 to 40 characters.", [nameof(Name)]);
        }
    }
}

public sealed class SubCategoryRequest : IValidatableObject
{
    [Required]
    public string? CategoryId { get; init; }

    public string? Name { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Names.IsValid(Name))
        {
            yield return new ValidationResult("Name is 1 to 40 characters.", [nameof(Name)]);
        }
    }
}
