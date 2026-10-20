using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.Entries;
using Api.Infrastructure;

namespace Api.Features.Transfers;

// R-TRF-01: money moved between two of the user's own accounts (TRF-01 on Add, BNK-04 on Banks). It
// only moves balances: it is never spending, never income, and never part of a plan. Transfers are
// listed by GET /api/banks.
public static class TransfersEndpoint
{
    public record TransferDto(
        string Id,
        DateOnly Date,
        string PeriodKey,
        string FyId,
        string FromBankAccountId,
        string ToBankAccountId,
        decimal Amount,
        string Note)
    {
        public static TransferDto From(Transfer transfer) => new(
            transfer.Id, transfer.Date, transfer.PeriodKey, transfer.FyId, transfer.FromBankAccountId,
            transfer.ToBankAccountId, Money.FromMinor(transfer.AmountMinor), transfer.Note);
    }

    public static IEndpointRouteBuilder MapTransfers(this IEndpointRouteBuilder api)
    {
        api.MapPost("/transfers", async (TransferRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var (transfer, error) = await BuildAsync(uid, DocumentIds.New(), request, current: null, store);
            if (transfer is null)
            {
                return error!;
            }
            await store.AddTransferAsync(uid, transfer);
            return Results.Created($"/api/transfers/{transfer.Id}", TransferDto.From(transfer));
        });

        // An unknown transfer, or another user's, is 404. The date moves it to the right month.
        api.MapPut("/transfers/{id}", async (string id, TransferRequest request, ClaimsPrincipal user, IUserStore store) =>
        {
            var uid = user.FindFirstValue("sub")!;
            if (await store.GetTransferAsync(uid, id) is not { } current)
            {
                return Results.NotFound();
            }
            var (transfer, error) = await BuildAsync(uid, id, request, current, store);
            if (transfer is null)
            {
                return error!;
            }
            return await store.UpdateTransferAsync(uid, transfer) ? Results.Ok(TransferDto.From(transfer)) : Results.NotFound();
        });

        // The confirmation is in the web app (as entries, PRD 4.6).
        api.MapDelete("/transfers/{id}", async (string id, ClaimsPrincipal user, IUserStore store) =>
            await store.DeleteTransferAsync(user.FindFirstValue("sub")!, id) ? Results.NoContent() : Results.NotFound());

        return api;
    }

    // As entries (M6): pickers hide archived accounts, so the API refuses them too, except the ones an
    // edited transfer already has. The date must fall inside a stored FY (R-STA-03).
    private static async Task<(Transfer? Transfer, IResult? Error)> BuildAsync(
        string uid, string id, TransferRequest request, Transfer? current, IUserStore store)
    {
        var accounts = await store.GetBankAccountsAsync(uid);
        bool Usable(string? accountId) => accounts.Any(account => account.Id == accountId
            && (!account.Archived || accountId == current?.FromBankAccountId || accountId == current?.ToBankAccountId));
        if (!Usable(request.FromBankAccountId))
        {
            return (null, Invalid(nameof(TransferRequest.FromBankAccountId), "Bank account is unknown."));
        }
        if (!Usable(request.ToBankAccountId))
        {
            return (null, Invalid(nameof(TransferRequest.ToBankAccountId), "Bank account is unknown."));
        }
        if (FiscalCalendar.FindPeriod(await store.GetFiscalYearsAsync(uid), request.Date!.Value) is not { } period)
        {
            return (null, EntriesEndpoint.OutsideFiscalYears());
        }
        return (new Transfer(
            id,
            request.Date.Value,
            period.Period.Key,
            period.FiscalYear.Id,
            request.FromBankAccountId!,
            request.ToBankAccountId!,
            Money.ToMinor(request.Amount!.Value),
            (request.Note ?? "").Trim()), null);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class TransferRequest : IValidatableObject
{
    [Required]
    public string? FromBankAccountId { get; init; }

    [Required]
    public string? ToBankAccountId { get; init; }

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
        if (FromBankAccountId is not null && FromBankAccountId == ToBankAccountId)
        {
            yield return new ValidationResult("Pick two different accounts.", [nameof(ToBankAccountId)]);
        }
        // PRD 4.7: notes are optional, at most 200 characters.
        if ((Note ?? "").Trim().Length > EntryRequest.NoteMaxLength)
        {
            yield return new ValidationResult("Note is at most 200 characters.", [nameof(Note)]);
        }
    }
}
