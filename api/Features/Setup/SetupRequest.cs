using System.ComponentModel.DataAnnotations;
using Api.Domain;
using Api.Features.Income;

namespace Api.Features.Setup;

// PRD 5.2 wizard, sent once on Finish. Amounts are decimal (R-MON-01).
public sealed class SetupRequest : IValidatableObject
{
    public string? ProjectName { get; init; }
    public string? DisplayName { get; init; }
    public string? Currency { get; init; }

    // SET-04: detected in the browser. An IANA id, for example "Asia/Riyadh".
    public string? Timezone { get; init; }

    [Range(1, 12)]
    public int FiscalStartMonth { get; init; }

    [Range(1, 31)]
    public int PaydayDay { get; init; }

    [Required]
    public decimal? Salary { get; init; }

    public List<AdditionalIncomeRequest>? AdditionalIncome { get; init; }
    public List<BankAccountSetupRequest>? BankAccounts { get; init; }
    public List<string?>? Categories { get; init; }

    // SET-01, PRD 4.7, R-MON-02. Everything that the attributes above cannot express.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Names.IsValid(ProjectName))
        {
            yield return Error(nameof(ProjectName), "Project name is 1 to 40 characters.");
        }
        if (!Names.IsValid(DisplayName))
        {
            yield return Error(nameof(DisplayName), "Your name is 1 to 40 characters.");
        }
        if (Currency is null || !Money.Currencies.Contains(Currency))
        {
            yield return Error(nameof(Currency), $"Currency is one of {string.Join(", ", Money.Currencies)}.");
        }
        if (string.IsNullOrEmpty(Timezone) || !TimeZoneInfo.TryFindSystemTimeZoneById(Timezone, out _))
        {
            yield return Error(nameof(Timezone), "Time zone is unknown.");
        }
        // SET-01: a salary of 0 is allowed.
        if (Salary is { } salary && !Money.IsValidAmount(salary, allowZero: true))
        {
            yield return Error(nameof(Salary), "Salary is 0 to 10,000,000 with at most 2 decimals.");
        }

        var items = AdditionalIncome ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // A label is not a name in 4.7, so two items may share one (owner's choice, M3).
            if (!Names.IsValid(item.Label))
            {
                yield return Error($"{nameof(AdditionalIncome)}[{i}].{nameof(item.Label)}", "Label is 1 to 40 characters.");
            }
            if (item.Amount is not { } amount || !Money.IsValidAmount(amount))
            {
                yield return Error($"{nameof(AdditionalIncome)}[{i}].{nameof(item.Amount)}", "Amount is more than 0 and at most 10,000,000, with at most 2 decimals.");
            }
            if (item.Type is not (AdditionalIncomeRequest.Once or AdditionalIncomeRequest.Monthly))
            {
                yield return Error($"{nameof(AdditionalIncome)}[{i}].{nameof(item.Type)}", "Type is once or monthly.");
            }
            // R-INC-04 (owner note 17): as in Settings; missing is both.
            if (!AdditionalIncomeItemRequest.IsCounts(item.Counts))
            {
                yield return Error($"{nameof(AdditionalIncome)}[{i}].{nameof(item.Counts)}", "Counts in is planned, actual or both.");
            }
            // The months must also be months of the first FY; the endpoint checks that, because it
            // needs today. Month keys are YYYY-MM, so ordinal order is calendar order.
            if (string.IsNullOrEmpty(item.FromPeriodKey))
            {
                yield return Error($"{nameof(AdditionalIncome)}[{i}].{nameof(item.FromPeriodKey)}", "Pick a month.");
            }
            else if (item.Type == AdditionalIncomeRequest.Monthly && !string.IsNullOrEmpty(item.ToPeriodKey)
                && string.CompareOrdinal(item.ToPeriodKey, item.FromPeriodKey) < 0)
            {
                yield return Error($"{nameof(AdditionalIncome)}[{i}].{nameof(item.ToPeriodKey)}", "Until is the same month as From or later.");
            }
        }

        var accounts = BankAccounts ?? [];
        foreach (var error in ValidateNames(nameof(BankAccounts), "bank account", accounts.Select(account => account.Name).ToList()))
        {
            yield return error;
        }
        // R-BAL-01, R-BAL-02: each account's balance today and its share of income, together 100%.
        for (var i = 0; i < accounts.Count; i++)
        {
            if (accounts[i].OpeningBalance is not { } balance || !Money.IsValidBalance(balance))
            {
                yield return Error($"{nameof(BankAccounts)}[{i}].{nameof(BankAccountSetupRequest.OpeningBalance)}",
                    "Balance is -10,000,000 to 10,000,000 with at most 2 decimals.");
            }
            if (accounts[i].Share is not { } share || !Money.IsValidPercent(share))
            {
                yield return Error($"{nameof(BankAccounts)}[{i}].{nameof(BankAccountSetupRequest.Share)}", "A share is 0 to 100 with at most 2 decimals.");
            }
        }
        if (accounts.Count > 0 && accounts.All(account => account.Share is { } share && Money.IsValidPercent(share))
            && accounts.Sum(account => account.Share!.Value) != 100)
        {
            yield return Error(nameof(BankAccounts), "The shares of income add up to exactly 100%.");
        }
        foreach (var error in ValidateNames(nameof(Categories), "category", Categories))
        {
            yield return error;
        }
    }

    private static IEnumerable<ValidationResult> ValidateNames(string member, string what, List<string?>? names)
    {
        names ??= [];
        if (names.Count == 0)
        {
            yield return Error(member, $"Add at least one {what}.");
        }
        for (var i = 0; i < names.Count; i++)
        {
            if (!Names.IsValid(names[i]))
            {
                yield return Error($"{member}[{i}]", "Name is 1 to 40 characters.");
            }
        }
        if (!Names.AreUnique(names))
        {
            yield return Error(member, $"Each {what} name is used once (ignoring case).");
        }
    }

    private static ValidationResult Error(string member, string message) => new(message, [member]);
}

// R-BAL-01: OpeningBalance is the balance today (the setup day). R-BAL-02: Share is the percent of
// income the account gets.
public sealed class BankAccountSetupRequest
{
    public string? Name { get; init; }
    public decimal? OpeningBalance { get; init; }
    public decimal? Share { get; init; }
}

// R-INC-03: Once is one month (ToPeriodKey is ignored); Monthly runs from a month, optionally
// until a month. The months are months of the first FY.
public sealed class AdditionalIncomeRequest
{
    public const string Once = "once";
    public const string Monthly = "monthly";

    public string? Label { get; init; }
    public decimal? Amount { get; init; }
    public string? Type { get; init; }
    public string? FromPeriodKey { get; init; }
    public string? ToPeriodKey { get; init; }
    public string? Counts { get; init; }
}
