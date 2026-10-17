using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.BankAccounts;
using Api.Features.FiscalYears;
using Api.Infrastructure;

namespace Api.Features.Entries;

// PRD 7.5 entries: add (PRD 5.4), list with filters, edit, delete and CSV export (PRD 5.5).
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

    // Everything the Entries page needs besides GET /api/categories (NFR-01, M6): the FYs for the
    // selector and the month filter, the accounts, the filtered entries and their total (TBL-04).
    public record EntriesResponse(
        IReadOnlyList<FiscalYearsEndpoint.FiscalYearDto> FiscalYears,
        string FyId,
        IReadOnlyList<BankAccountsEndpoint.BankAccountDto> BankAccounts,
        IReadOnlyList<EntryDto> Entries,
        decimal Total);

    public static IEndpointRouteBuilder MapEntries(this IEndpointRouteBuilder api)
    {
        api.MapPost("/entries", async (EntryRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var (entry, error) = await BuildAsync(uid, DocumentIds.New(), request, current: null, store);
            if (entry is null)
            {
                return error!;
            }
            await store.AddEntryAsync(uid, entry);
            return Results.Created($"/api/entries/{entry.Id}", EntryDto.From(entry));
        });

        // TBL-01, TBL-04: newest first, filtered, with the total of the filtered rows.
        api.MapGet("/entries", async ([AsParameters] EntryFilter filter, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await SelectAsync(uid, filter, store, time) is not { } selection)
            {
                return Results.NotFound();
            }
            var accounts = await store.GetBankAccountsAsync(uid);
            return Results.Ok(new EntriesResponse(
                selection.FiscalYears.Select(FiscalYearsEndpoint.FiscalYearDto.From).ToList(),
                selection.FiscalYear.Id,
                BankAccountsEndpoint.Sorted(accounts),
                selection.Entries.Select(EntryDto.From).ToList(),
                Money.FromMinor(selection.Entries.Sum(entry => entry.AmountMinor))));
        });

        // TBL-06, NFR-05: the same filters and order as the list.
        api.MapGet("/entries/export.csv", async ([AsParameters] EntryFilter filter, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await SelectAsync(uid, filter, store, time) is not { } selection)
            {
                return Results.NotFound();
            }
            var profile = (await store.GetProfileAsync(uid))!;
            var categories = (await store.GetCategoriesAsync(uid)).ToDictionary(category => category.Id, category => category.Name);
            var subCategories = (await store.GetSubCategoriesAsync(uid)).ToDictionary(sub => sub.Id, sub => sub.Name);
            var accounts = (await store.GetBankAccountsAsync(uid)).ToDictionary(account => account.Id, account => account.Name);
            var months = selection.FiscalYear.Periods.ToDictionary(period => period.Key, period => period.Label);

            var rows = selection.Entries.Select(entry => new EntriesCsv.Row(
                entry.Date,
                months.GetValueOrDefault(entry.PeriodKey, entry.PeriodKey),
                categories.GetValueOrDefault(entry.CategoryId, ""),
                subCategories.GetValueOrDefault(entry.SubCategoryId, ""),
                entry.AmountMinor,
                accounts.GetValueOrDefault(entry.BankAccountId, ""),
                entry.Note));
            return Results.File(EntriesCsv.Write(rows, profile.Currency), "text/csv; charset=utf-8", $"nama-entries-{selection.FiscalYear.Id}.csv");
        });

        // ENT-04, TBL-02: changing the date moves the entry to the right month and FY (R-CAL-09).
        // An unknown entry, or another user's, is 404.
        api.MapPut("/entries/{id}", async (string id, EntryRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await store.GetEntryAsync(uid, id) is not { } current)
            {
                return Results.NotFound();
            }
            var (entry, error) = await BuildAsync(uid, id, request, current, store);
            if (entry is null)
            {
                return error!;
            }
            return await store.UpdateEntryAsync(uid, entry) ? Results.Ok(EntryDto.From(entry)) : Results.NotFound();
        });

        // TBL-03: the confirmation is in the web app (PRD 4.6).
        api.MapDelete("/entries/{id}", async (string id, ClaimsPrincipal user, IUserStore store) =>
            await store.DeleteEntryAsync(user.FindFirstValue("sub")!, id) ? Results.NoContent() : Results.NotFound());

        return api;
    }

    // PRD 7.5: 422 when the date is outside every existing FY.
    public static IResult OutsideFiscalYears() => Results.ValidationProblem(
        new Dictionary<string, string[]> { [nameof(EntryRequest.Date)] = ["Date is outside every fiscal year."] },
        statusCode: StatusCodes.Status422UnprocessableEntity);

    // The entry a POST or PUT would store, or the error to answer with.
    private static async Task<(Entry? Entry, IResult? Error)> BuildAsync(
        string uid, string id, EntryRequest request, Entry? current, IUserStore store)
    {
        // Pickers hide archived items (PRD 4.6), so the API refuses them too. An edited entry may
        // keep the archived sub-category or account it already has (M6).
        var subCategory = (await store.GetSubCategoriesAsync(uid))
            .FirstOrDefault(sub => sub.Id == request.SubCategoryId && (!sub.Archived || sub.Id == current?.SubCategoryId));
        if (subCategory is null)
        {
            return (null, Invalid(nameof(EntryRequest.SubCategoryId), "Sub-category is unknown."));
        }
        if (!(await store.GetBankAccountsAsync(uid)).Any(account =>
                account.Id == request.BankAccountId && (!account.Archived || account.Id == current?.BankAccountId)))
        {
            return (null, Invalid(nameof(EntryRequest.BankAccountId), "Bank account is unknown."));
        }

        // R-STA-03, R-CAL-09: the date must fall inside a stored FY, which gives the month.
        if (FiscalCalendar.FindPeriod(await store.GetFiscalYearsAsync(uid), request.Date!.Value) is not { } period)
        {
            return (null, OutsideFiscalYears());
        }

        return (new Entry(
            id,
            request.Date.Value,
            period.Period.Key,
            period.FiscalYear.Id,
            subCategory.CategoryId,
            subCategory.Id,
            request.BankAccountId!,
            Money.ToMinor(request.Amount!.Value),
            (request.Note ?? "").Trim()), null);
    }

    private sealed record Selection(IReadOnlyList<FiscalYear> FiscalYears, FiscalYear FiscalYear, IReadOnlyList<Entry> Entries);

    // The chosen FY (by default the one that contains today, else the latest) and its entries that
    // match the filters. Null when the FY is unknown.
    private static async Task<Selection?> SelectAsync(string uid, EntryFilter filter, IUserStore store, TimeProvider time)
    {
        var fiscalYears = (await store.GetFiscalYearsAsync(uid)).OrderBy(fiscalYear => fiscalYear.StartDate).ToList();
        FiscalYear? chosen;
        if (string.IsNullOrEmpty(filter.FyId))
        {
            var profile = await store.GetProfileAsync(uid);
            var today = profile is null ? (DateOnly?)null : FiscalCalendar.Today(time, profile.Timezone);
            chosen = (today is { } date ? FiscalCalendar.FindPeriod(fiscalYears, date)?.FiscalYear : null) ?? fiscalYears.LastOrDefault();
        }
        else
        {
            chosen = fiscalYears.FirstOrDefault(fiscalYear => fiscalYear.Id == filter.FyId);
        }
        if (chosen is null)
        {
            return null;
        }
        var entries = (await store.GetFiscalYearEntriesAsync(uid, chosen.Id)).Where(filter.Matches).ToList();
        return new Selection(fiscalYears, chosen, entries);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

// TBL-04 filters, from the query string. Each one is optional. Ids must match exactly; q is
// searched in notes, ignoring case.
public sealed record EntryFilter(
    string? FyId,
    string? PeriodKey,
    string? CategoryId,
    string? SubCategoryId,
    string? BankAccountId,
    string? Q)
{
    public bool Matches(Entry entry) =>
        Is(PeriodKey, entry.PeriodKey)
        && Is(CategoryId, entry.CategoryId)
        && Is(SubCategoryId, entry.SubCategoryId)
        && Is(BankAccountId, entry.BankAccountId)
        && (string.IsNullOrWhiteSpace(Q) || entry.Note.Contains(Q.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool Is(string? filter, string value) => string.IsNullOrEmpty(filter) || filter == value;
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
