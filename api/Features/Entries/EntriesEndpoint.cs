using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Infrastructure;

namespace Api.Features.Entries;

// PRD 7.5 POST /api/entries (PRD 5.4 Add entry). Edit and delete come with Entries (M6).
public static class EntriesEndpoint
{
    // DateOnly is sent as YYYY-MM-DD; the amount is a decimal (R-MON-01).
    public record EntryDto(
        string Id,
        DateOnly Date,
        string PeriodKey,
        string FyId,
        string CategoryId,
        string SubCategoryId,
        string BankAccountId,
        decimal Amount,
        string Note)
    {
        public static EntryDto From(Entry entry) => new(
            entry.Id, entry.Date, entry.PeriodKey, entry.FyId, entry.CategoryId, entry.SubCategoryId,
            entry.BankAccountId, Money.FromMinor(entry.AmountMinor), entry.Note);
    }

    public static IEndpointRouteBuilder MapEntries(this IEndpointRouteBuilder api)
    {
        api.MapPost("/entries", async (EntryRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;

            // Pickers hide archived items (PRD 4.6), so the API refuses them too.
            var subCategory = (await store.GetSubCategoriesAsync(uid))
                .FirstOrDefault(sub => sub.Id == request.SubCategoryId && !sub.Archived);
            if (subCategory is null)
            {
                return Invalid(nameof(EntryRequest.SubCategoryId), "Sub-category is unknown.");
            }
            if (!(await store.GetBankAccountsAsync(uid)).Any(account => account.Id == request.BankAccountId && !account.Archived))
            {
                return Invalid(nameof(EntryRequest.BankAccountId), "Bank account is unknown.");
            }

            // R-STA-03, R-CAL-09: the date must fall inside a stored FY, which gives the month.
            var found = FiscalCalendar.FindPeriod(await store.GetFiscalYearsAsync(uid), request.Date!.Value);
            if (found is not { } period)
            {
                return OutsideFiscalYears();
            }

            var entry = new Entry(
                DocumentIds.New(),
                request.Date.Value,
                period.Period.Key,
                period.FiscalYear.Id,
                subCategory.CategoryId,
                subCategory.Id,
                request.BankAccountId!,
                Money.ToMinor(request.Amount!.Value),
                (request.Note ?? "").Trim());
            await store.AddEntryAsync(uid, entry);
            return Results.Created($"/api/entries/{entry.Id}", EntryDto.From(entry));
        });

        return api;
    }

    // PRD 7.5: 422 when the date is outside every existing FY.
    public static IResult OutsideFiscalYears() => Results.ValidationProblem(
        new Dictionary<string, string[]> { [nameof(EntryRequest.Date)] = ["Date is outside every fiscal year."] },
        statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class EntryRequest : IValidatableObject
{
    public const int NoteMaxLength = 200;

    [Required]
    public string? SubCategoryId { get; init; }

    [Required]
    public string? BankAccountId { get; init; }

    [Required]
    public DateOnly? Date { get; init; }

    [Required]
    public decimal? Amount { get; init; }

    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // R-MON-02
        if (Amount is { } amount && !Money.IsValidAmount(amount))
        {
            yield return new ValidationResult("Amount is more than 0 and at most 10,000,000, with at most 2 decimals.", [nameof(Amount)]);
        }
        // PRD 4.7: notes are optional, at most 200 characters.
        if ((Note ?? "").Trim().Length > NoteMaxLength)
        {
            yield return new ValidationResult("Note is at most 200 characters.", [nameof(Note)]);
        }
    }
}
