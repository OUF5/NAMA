using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Domain;
using Api.Features.FiscalYears;
using Api.Features.Transfers;
using Api.Infrastructure;

namespace Api.Features.Banks;

// What the balances of one user are made of, read once (R-BAL-03). Banks, the top bar summary and the
// Add page's transfer preview use it.
public sealed record BalanceData(
    UserProfile Profile,
    DateOnly Today,
    IReadOnlyList<FiscalYear> FiscalYears,
    IReadOnlyList<BankAccount> Accounts,
    IReadOnlyList<IncomeSplit> Splits,
    IReadOnlyList<Transfer> Transfers,
    IReadOnlyDictionary<string, long> Income,
    IReadOnlyList<Entry> Entries,
    IReadOnlyDictionary<string, AccountBalance> Balances)
{
    // The month the split and the shares are shown for: today's, else the latest stored month.
    public string? ShownPeriodKey =>
        FiscalCalendar.FindPeriod(FiscalYears, Today)?.Period.Key ?? FiscalCalendar.Latest(FiscalYears)?.Periods[^1].Key;

    public IncomeSplit? ShownSplit => ShownPeriodKey is { } key ? BalanceCalculator.SplitFor(Splits, key) : null;

    // The split that applies from now on: the one with the latest month (archiving needs it, R-BAL-02).
    public IncomeSplit? LatestSplit => Splits.MaxBy(split => split.FromPeriodKey, StringComparer.Ordinal);

    // The total covers the active accounts that have a balance; archived ones are listed on their own.
    public long? Total
    {
        get
        {
            var active = Accounts.Where(account => !account.Archived && Balances.ContainsKey(account.Id)).ToList();
            return active.Count == 0 ? null : active.Sum(account => Balances[account.Id].Balance);
        }
    }

    // Every active account has an opening balance and income has a split, so the total is complete.
    public bool Complete => Splits.Count > 0 && Accounts.Where(account => !account.Archived).All(account => account.Opening is not null);
}

// PRD 5.11 Banks (BNK-01 to BNK-05): every account with its balance and what makes it up, the income
// split (R-BAL-02) and the transfers (R-TRF-01), in one call (NFR-01). Bank account changes answer
// with the same body, because a change moves the balances.
public static class BanksEndpoint
{
    public record BalanceDto(decimal Opening, decimal Income, decimal Spent, decimal TransfersIn, decimal TransfersOut, decimal Balance)
    {
        public static BalanceDto From(AccountBalance balance) => new(
            Money.FromMinor(balance.Opening), Money.FromMinor(balance.Income), Money.FromMinor(balance.Spent),
            Money.FromMinor(balance.TransfersIn), Money.FromMinor(balance.TransfersOut), Money.FromMinor(balance.Balance));
    }

    // OpeningBalance and OpeningDate are null until set (R-BAL-01); Balance is null then too. Share is
    // the account's percent of income in the shown month and MonthIncome what that gives (both null
    // without a split).
    public record AccountDto(
        string Id, string Name, bool Archived, decimal? OpeningBalance, DateOnly? OpeningDate, decimal? Share, decimal? MonthIncome,
        BalanceDto? Balance);

    // Percent per account id (80.5 is 80.5%).
    public record SplitDto(string FromPeriodKey, IReadOnlyDictionary<string, decimal> Shares)
    {
        public static SplitDto From(IncomeSplit split) => new(
            split.FromPeriodKey, split.Shares.ToDictionary(share => share.Key, share => Money.FromBasisPoints(share.Value)));
    }

    // Accounts are sorted by name, archived ones included. Total is null when no active account has a
    // balance. Split is the one of the shown month (today's, else the latest stored month); the
    // history is newest first. Income (month key → total income) gives the split's amounts.
    public record BanksResponse(
        DateOnly Today,
        string? CurrentPeriodKey,
        IReadOnlyList<AccountDto> Accounts,
        decimal? Total,
        bool Complete,
        SplitDto? Split,
        IReadOnlyList<SplitDto> SplitHistory,
        IReadOnlyList<FiscalYearsEndpoint.FiscalYearDto> FiscalYears,
        IReadOnlyDictionary<string, decimal> Income,
        IReadOnlyList<TransfersEndpoint.TransferDto> Transfers);

    public static IEndpointRouteBuilder MapBanks(this IEndpointRouteBuilder api)
    {
        api.MapGet("/banks", async (ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
            await ResultAsync(user.FindFirstValue("sub")!, store, time));

        // R-BAL-02: the shares apply from a stored month on, and replace every split from that month on.
        api.MapPut("/income-split", async (IncomeSplitRequest request, ClaimsPrincipal user, IUserStore store, TimeProvider time) =>
        {
            var uid = user.FindFirstValue("sub")!;
            var fiscalYears = await store.GetFiscalYearsAsync(uid);
            if (!fiscalYears.SelectMany(fiscalYear => fiscalYear.Periods).Any(period => period.Key == request.EffectiveFromPeriodKey))
            {
                return Invalid(nameof(IncomeSplitRequest.EffectiveFromPeriodKey), "Pick a month of a fiscal year.");
            }
            var active = (await store.GetBankAccountsAsync(uid)).Where(account => !account.Archived).Select(account => account.Id).ToHashSet();
            var shares = request.Shares!
                .Where(share => share.Value is > 0)
                .ToDictionary(share => share.Key, share => Money.ToBasisPoints(share.Value!.Value));
            if (shares.Keys.Any(id => !active.Contains(id)))
            {
                return Invalid(nameof(IncomeSplitRequest.Shares), "Only active bank accounts can get a share of income.");
            }
            var split = new IncomeSplit(request.EffectiveFromPeriodKey!, shares);
            await store.SetIncomeSplitAsync(uid, split, BalanceCalculator.Replaced(await store.GetIncomeSplitsAsync(uid), split.FromPeriodKey));
            return await ResultAsync(uid, store, time);
        });

        return api;
    }

    // R-BAL-03. alsoFrom: the top bar also needs the current FY's entries, so one query serves both.
    public static async Task<BalanceData?> LoadAsync(string uid, IUserStore store, TimeProvider time, DateOnly? alsoFrom = null)
    {
        var profile = await store.GetProfileAsync(uid);
        if (profile is null)
        {
            return null;
        }
        var today = FiscalCalendar.Today(time, profile.Timezone);
        var fiscalYears = (await store.GetFiscalYearsAsync(uid)).OrderBy(fiscalYear => fiscalYear.StartDate).ToList();
        var accounts = await store.GetBankAccountsAsync(uid);
        var splits = await store.GetIncomeSplitsAsync(uid);
        var transfers = await store.GetTransfersAsync(uid);
        var periods = fiscalYears.SelectMany(fiscalYear => fiscalYear.Periods).ToList();
        var income = IncomeCalculator.TotalsForPeriods(await store.GetIncomesAsync(uid), periods);

        // Only what a balance can count: entries from the earliest opening date on.
        var dates = accounts.Where(account => account.Opening is not null).Select(account => account.Opening!.Date).ToList();
        if (alsoFrom is { } extra)
        {
            dates.Add(extra);
        }
        var entries = dates.Count == 0 ? [] : await store.GetEntriesFromAsync(uid, dates.Min());

        var balances = accounts
            .Where(account => account.Opening is not null)
            .ToDictionary(account => account.Id, account => BalanceCalculator.Balance(
                account.Id,
                account.Opening!,
                periods,
                income,
                splits,
                entries.Where(entry => entry.BankAccountId == account.Id).Select(entry => new DatedAmount(entry.Date, entry.AmountMinor)),
                transfers.Where(transfer => transfer.ToBankAccountId == account.Id).Select(transfer => new DatedAmount(transfer.Date, transfer.AmountMinor)),
                transfers.Where(transfer => transfer.FromBankAccountId == account.Id).Select(transfer => new DatedAmount(transfer.Date, transfer.AmountMinor)),
                today));

        return new BalanceData(profile, today, fiscalYears, accounts, splits, transfers, income, entries, balances);
    }

    public static async Task<IResult> ResultAsync(string uid, IUserStore store, TimeProvider time, bool created = false)
    {
        if (await LoadAsync(uid, store, time) is not { } data)
        {
            return Results.NotFound();
        }
        var response = ToResponse(data);
        return created ? Results.Created("/api/banks", response) : Results.Ok(response);
    }

    public static BanksResponse ToResponse(BalanceData data)
    {
        var split = data.ShownSplit;
        var monthIncome = split is not null && data.ShownPeriodKey is { } key
            ? BalanceCalculator.Distribute(data.Income.GetValueOrDefault(key), split.Shares)
            : null;
        return new BanksResponse(
            data.Today,
            FiscalCalendar.FindPeriod(data.FiscalYears, data.Today)?.Period.Key,
            data.Accounts
                .OrderBy(account => Names.Lower(account.Name), StringComparer.Ordinal)
                .Select(account => new AccountDto(
                    account.Id,
                    account.Name,
                    account.Archived,
                    account.Opening is { } opening ? Money.FromMinor(opening.AmountMinor) : null,
                    account.Opening?.Date,
                    split is null ? null : Money.FromBasisPoints(split.Shares.GetValueOrDefault(account.Id)),
                    monthIncome is null ? null : Money.FromMinor(monthIncome.GetValueOrDefault(account.Id)),
                    data.Balances.TryGetValue(account.Id, out var balance) ? BalanceDto.From(balance) : null))
                .ToList(),
            data.Total is { } total ? Money.FromMinor(total) : null,
            data.Complete,
            split is null ? null : SplitDto.From(split),
            data.Splits.OrderByDescending(item => item.FromPeriodKey, StringComparer.Ordinal).Select(SplitDto.From).ToList(),
            data.FiscalYears.Select(FiscalYearsEndpoint.FiscalYearDto.From).ToList(),
            data.Income.ToDictionary(pair => pair.Key, pair => Money.FromMinor(pair.Value)),
            data.Transfers.Select(TransfersEndpoint.TransferDto.From).ToList());
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

// R-BAL-02: account id → percent (0 to 100, at most 2 decimals); together exactly 100.
public sealed class IncomeSplitRequest : IValidatableObject
{
    [Required]
    public string? EffectiveFromPeriodKey { get; init; }

    [Required]
    public Dictionary<string, decimal?>? Shares { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var shares = Shares ?? [];
        foreach (var (id, percent) in shares)
        {
            if (percent is not { } value || !Money.IsValidPercent(value))
            {
                yield return new ValidationResult("A share is 0 to 100 with at most 2 decimals.", [$"{nameof(Shares)}[{id}]"]);
            }
        }
        if (shares.Values.All(percent => percent is { } value && Money.IsValidPercent(value))
            && shares.Values.Sum(percent => percent!.Value) != 100)
        {
            yield return new ValidationResult("The shares add up to exactly 100%.", [nameof(Shares)]);
        }
    }
}
