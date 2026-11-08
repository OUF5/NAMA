using System.ComponentModel;
using System.Security.Claims;
using Api.Features.Banks;
using Api.Features.Categories;
using Api.Features.Entries;
using Api.Features.FiscalYears;
using Api.Features.Income;
using Api.Features.Loans;
using Api.Features.Overview;
using Api.Features.Plans;
using Api.Features.Profile;
using Api.Features.Projects;
using Api.Features.Summary;
using Api.Infrastructure;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Api.Features.Mcp;

// MCP-04: the Claude connector's tools, all read-only. Each one answers with what its page's endpoint
// answers (the same LoadAsync), so Claude and the app always agree; get_briefing and list_entries add
// names to the ids (owner note 34). The user id is the connection's "sub" (McpTokenHandler), never an
// argument. People and invite links are never shown.
[McpServerToolType]
public sealed class NamaTools(IUserStore store, TimeProvider time, EmailAllowlist admins)
{
    // MCP-04: list_entries sends at most this many entries, the newest; Count and Total cover all.
    public const int MaxEntries = 300;

    // Owner note 34: what Claude reads when it connects. The words are the app's, so Claude says what
    // the owner sees.
    public const string Instructions =
        "NAMA is a personal budget tracked by fiscal year (FY). A month runs from payday to the day before the next payday; "
        + "its key (periodKey, YYYY-MM) is the month it starts in and its label is its name on screen. "
        + "Amounts are decimals in the profile's currency. Every tool only reads: nothing can be changed here.\n"
        + "Start with get_briefing: today, the month in progress, what to watch, the money, the outlook, income to come, "
        + "loans and projects in one call, with names. Then go deeper with get_month (a month in detail), list_entries "
        + "(every expense, transfer and additional income, filtered), get_overview (plan vs spent per sub-category per month), "
        + "get_balances (each month's end balance), get_projects, get_plans, get_income, get_banks and get_loans.\n"
        + "How the app counts, in its words: Remaining this month = the month's planned - spent (the big number on Tracker "
        + "and Dashboard, and the top bar's \"Left in\"). Overspent last month = what went over plan last month and is not "
        + "made up yet, per sub-category; Remaining - Overspent last month is what is left once it is covered. Available "
        + "(Tracker's Year view) = a month's plan + what was left or overspent in the FY's earlier months (carriedIn + planned); "
        + "a field named remaining in get_summary and get_overview is that carried amount minus spent, not Remaining this month. "
        + "A line is within plan below 80% of the month's plan, near limit from 80% to 100%, and over plan above; spending "
        + "with no plan is over plan. Income is the salary and additional items: Planned items count only in the plan, Actual "
        + "only in what came in, Both in both. Accumulated savings = income - spent, added up from the FY's first month; the "
        + "projection spends what is left of this month's plan and each month to come its plan. Entries have a type: expense, "
        + "transfer (between the owner's own accounts, never spending or income) or income (additional items, one row per month). "
        + "Loans are information only: they change no balance.\n"
        + "Ids (categoryId, subCategoryId, bankAccountId) come with names in get_briefing and list_entries; get_categories and "
        + "get_banks name them all.";

    public record CalendarResult(MeEndpoint.MeResponse Me, IReadOnlyList<FiscalYearsEndpoint.FiscalYearDto> FiscalYears);

    // Owner note 34 (TBL-07): one row of any type, with names. An expense has its category, sub-category
    // and account; a transfer its from and to accounts; income its label, what it counts in and its
    // account (none for a Planned item). Id is the entry's or the transfer's, IncomeId the item's.
    public record EntryRow(
        string Type,
        DateOnly Date,
        string PeriodKey,
        string Month,
        decimal Amount,
        string? Category = null,
        string? SubCategory = null,
        string? CategoryId = null,
        string? SubCategoryId = null,
        string? Account = null,
        string? BankAccountId = null,
        string? FromAccount = null,
        string? ToAccount = null,
        string? Label = null,
        string? Counts = null,
        string? Note = null,
        string? Id = null,
        string? IncomeId = null);

    // Count is every matching row, Truncated true when more than MaxEntries match; Spent, Moved and
    // Income are each type's total of every match.
    public record EntriesResult(
        string FyId,
        int Count,
        bool Truncated,
        decimal Spent,
        decimal Moved,
        decimal Income,
        IReadOnlyList<EntryRow> Entries);

    [McpServerTool(Name = "get_briefing", Title = "Briefing", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Start here. One compact answer to \"how am I doing?\", with names: today; the month in progress (day of days, "
        + "planned, spent, Remaining this month and its status, Overspent last month, Remaining - Overspent last month, income); "
        + "up to 5 sub-categories near or over this month's plan; the top 5 categories this month against their plan and last "
        + "month; what last month left to make up by category; the 5 biggest and 5 newest expenses this month; the total bank "
        + "balance and each account's; the FY so far and at its end (income, spent and saving against the plan, the projected "
        + "balance); additional income this month and next; open loans; and each saving project's state.")]
    public async Task<Briefing.BriefingResult> GetBriefing(ClaimsPrincipal user) =>
        await Briefing.LoadAsync(Uid(user), store, time) ?? throw NotSetUp();

    [McpServerTool(Name = "get_calendar", Title = "Calendar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("The profile (name, currency, time zone, payday), today, the current FY and month, and every FY with its months (key, label, start and end date).")]
    public async Task<CalendarResult> GetCalendar(ClaimsPrincipal user)
    {
        var uid = Uid(user);
        var fiscalYears = (await store.GetFiscalYearsAsync(uid)).OrderBy(fiscalYear => fiscalYear.StartDate).Select(FiscalYearsEndpoint.FiscalYearDto.From);
        return new CalendarResult(await MeEndpoint.LoadAsync(uid, admins.IsAdmin(user), store, time), fiscalYears.ToList());
    }

    [McpServerTool(Name = "get_summary", Title = "Summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("The top bar and Dashboard's projection: the month in progress (day, days, days left, income, carriedIn, planned, "
        + "spent, remaining (carried: carriedIn + planned - spent), unassigned, and overspentLastMonth; Remaining this month is "
        + "planned - spent), the total bank balance and each account's, and the projection to the FY's end (balance today, still "
        + "to spend this month, the months to come, the projected balance, saved to date, planned saved and projected saving), "
        + "and lastAdded: the last expense added per sub-category and per account, the last transfer per account, the last additional income. "
        + "get_briefing says the same with names.")]
    public async Task<SummaryEndpoint.SummaryResponse> GetSummary(ClaimsPrincipal user) =>
        await SummaryEndpoint.LoadAsync(Uid(user), store, time) ?? throw NotSetUp();

    [McpServerTool(Name = "get_overview", Title = "Tracker overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Tracker for one FY: per month its status, income (and plannedIncome, additionalIncome), carriedIn, planned, spent, "
        + "remaining (carried), unspent (income - spent) and saved (accumulated savings), with projectedUnspent and projectedSaved "
        + "to the FY's end; per category and sub-category per month carriedIn, planned, spent and remaining (Available = carriedIn "
        + "+ planned, carried from the FY's first month); the FY's totals; and planVsActual (Dashboard: income, spent and saving "
        + "against the plan so far and at the FY's end). Ids are named by the categories it lists. Optionally narrowed to a "
        + "category or sub-category. Large for a whole FY: prefer get_briefing or a filter.")]
    public async Task<OverviewEndpoint.OverviewResponse> GetOverview(
        ClaimsPrincipal user,
        [Description("The FY's id (the key of its first month, YYYY-MM). Leave out for the current FY.")] string? fyId = null,
        [Description("Only this category.")] string? categoryId = null,
        [Description("Only this sub-category.")] string? subCategoryId = null)
    {
        var uid = Uid(user);
        var fy = fyId ?? await CurrentFyIdAsync(uid);
        return await OverviewEndpoint.LoadAsync(uid, fy, categoryId, subCategoryId, store, time)
            ?? throw new McpException($"There is no FY {fy}, or no such category or sub-category. get_calendar lists the FYs.");
    }

    [McpServerTool(Name = "get_month", Title = "Tracker month", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("One month in detail (Tracker's month): spending by day and by bank account, each category against the month "
        + "before, the 5 newest and 5 biggest expenses, the count and total, the month's additional income, and overspent: what "
        + "went over plan in the months back and is not made up yet, by category, with what is left of this month's plan and what "
        + "is left after covering it (below 0 when this month cannot).")]
    public async Task<MonthEndpoint.MonthResponse> GetMonth(
        ClaimsPrincipal user,
        [Description("The FY's id. Leave out for the current FY.")] string? fyId = null,
        [Description("The month's key (YYYY-MM, the month it starts in). Leave out for the current month.")] string? periodKey = null,
        [Description("Only this category.")] string? categoryId = null,
        [Description("Only this sub-category.")] string? subCategoryId = null,
        [Description("How many months back the overspend to make up is counted over: 1 (last month, the default), 2, 3, 6, "
            + "or 12 for since the FY's first month; never before the FY's first month.")] int? back = null)
    {
        var uid = Uid(user);
        var me = await MeEndpoint.LoadAsync(uid, isAdmin: false, store, time);
        var fy = fyId ?? me.CurrentFyId ?? await CurrentFyIdAsync(uid);
        var month = periodKey ?? (fy == me.CurrentFyId ? me.CurrentPeriodKey : null)
            ?? throw new McpException("No month is in progress in that FY: name the month (periodKey). get_calendar lists them.");
        return await MonthEndpoint.LoadAsync(uid, fy, month, categoryId, subCategoryId, store, back)
            ?? throw new McpException($"There is no month {month} in FY {fy}. get_calendar lists them.");
    }

    [McpServerTool(Name = "list_entries", Title = "Entries", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Entries of one FY, newest first, as the Entries page lists them: expenses, transfers between the owner's "
        + "accounts and additional income (one row per month of each item), each with its type and names, filtered; with the "
        + "count of every match and each type's total (spent, moved, income). A category or sub-category keeps expenses only; a "
        + "bank account keeps what was paid from it, moved into or out of it, and received into it. At most 300 rows are listed "
        + "(truncated says when there are more); narrow the filters to see the rest.")]
    public async Task<EntriesResult> ListEntries(
        ClaimsPrincipal user,
        [Description("The FY's id. Leave out for the current FY.")] string? fyId = null,
        [Description("Only this month (YYYY-MM).")] string? periodKey = null,
        [Description("Only this category.")] string? categoryId = null,
        [Description("Only this sub-category.")] string? subCategoryId = null,
        [Description("Only this bank account.")] string? bankAccountId = null,
        [Description("Words to find in the notes (and income labels), ignoring case.")] string? q = null,
        [Description("Only this type: expense, transfer or income. Leave out for every type.")] string? type = null)
    {
        var uid = Uid(user);
        var filter = new EntryFilter(fyId, periodKey, categoryId, subCategoryId, bankAccountId, q, type);
        if (!filter.HasKnownType)
        {
            throw new McpException("type is expense, transfer or income (or left out for every type).");
        }
        var response = await EntriesEndpoint.LoadAsync(uid, filter, store, time)
            ?? throw new McpException($"There is no FY {fyId}. get_calendar lists them.");
        var categories = await CategoriesEndpoint.LoadAsync(uid, store);
        var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
        var subNames = categories.SelectMany(category => category.SubCategories).ToDictionary(sub => sub.Id, sub => sub.Name);
        var accounts = response.BankAccounts.ToDictionary(account => account.Id, account => account.Name);
        var months = response.FiscalYears.Single(fiscalYear => fiscalYear.Id == response.FyId).Periods.ToDictionary(period => period.Key, period => period.Label);
        string Month(string key) => months.GetValueOrDefault(key, key);
        string? Account(string? id) => id is null ? null : accounts.GetValueOrDefault(id, "");

        // Newest first; on one day, expenses, then transfers, then income (as the page and the CSV).
        var rows = response.Entries
            .Select(entry => new EntryRow(
                EntryFilter.Expense, entry.Date, entry.PeriodKey, Month(entry.PeriodKey), entry.Amount,
                Category: categoryNames.GetValueOrDefault(entry.CategoryId, ""), SubCategory: subNames.GetValueOrDefault(entry.SubCategoryId, ""),
                CategoryId: entry.CategoryId, SubCategoryId: entry.SubCategoryId,
                Account: Account(entry.BankAccountId), BankAccountId: entry.BankAccountId, Note: entry.Note, Id: entry.Id))
            .Concat(response.Transfers.Select(transfer => new EntryRow(
                EntryFilter.Transfer, transfer.Date, transfer.PeriodKey, Month(transfer.PeriodKey), transfer.Amount,
                FromAccount: Account(transfer.FromBankAccountId), ToAccount: Account(transfer.ToBankAccountId), Note: transfer.Note, Id: transfer.Id)))
            .Concat(response.Income.Select(income => new EntryRow(
                EntryFilter.Income, income.Date, income.PeriodKey, Month(income.PeriodKey), income.Amount,
                Account: Account(income.BankAccountId), BankAccountId: income.BankAccountId,
                Label: income.Label, Counts: income.Counts, IncomeId: income.IncomeId)))
            .OrderByDescending(row => row.Date)
            .ToList();
        return new EntriesResult(
            response.FyId, rows.Count, rows.Count > MaxEntries, response.Total, response.TransferTotal, response.IncomeTotal,
            rows.Take(MaxEntries).ToList());
    }

    [McpServerTool(Name = "get_balances", Title = "Balances by month", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Each month's end balance of one FY, the total of the active accounts (Tracker's Balance row): actual for a closed "
        + "month, projected for the month in progress and the months to come (each adds its income minus its plan); the FY's last "
        + "month is the projected balance at its end. Balances are null until every active account has an opening balance and "
        + "income has a split (complete), and before an account's opening date.")]
    public async Task<BalancesEndpoint.BalancesResponse> GetBalances(
        ClaimsPrincipal user,
        [Description("The FY's id. Leave out for the current FY.")] string? fyId = null)
    {
        var uid = Uid(user);
        var fy = fyId ?? await CurrentFyIdAsync(uid);
        return await BalancesEndpoint.LoadAsync(uid, fy, store, time) ?? throw new McpException($"There is no FY {fy}. get_calendar lists them.");
    }

    [McpServerTool(Name = "get_projects", Title = "Projects", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Saving projects (the Projects page): each with its targets (best, likely, worst; the active one is target), "
        + "deadline, status (active, paused, done) and state (onTrack, offTrack, reached, paused, done), its one bank account and "
        + "money in it now, the monthly amount and the plan by month, each month's planned against what went in (behind below 0 "
        + "is ahead), still to put in this month, still to save, the projected money at the deadline, how much is short and what "
        + "more a month closes it, and milestones. Accounts lists the accounts by id with their balances.")]
    public async Task<ProjectsEndpoint.ProjectsResponse> GetProjects(ClaimsPrincipal user) =>
        await ProjectsEndpoint.LoadAsync(Uid(user), store, time) ?? throw NotSetUp();

    [McpServerTool(Name = "get_categories", Title = "Categories", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Every category with its sub-categories (ids and names), archived ones flagged.")]
    public Task<List<CategoriesEndpoint.CategoryDto>> GetCategories(ClaimsPrincipal user) => CategoriesEndpoint.LoadAsync(Uid(user), store);

    [McpServerTool(Name = "get_plans", Title = "Plan", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Plan for one FY: the amount planned per sub-category per month, and per month the planned income (and its part from additional items), planned and unassigned.")]
    public async Task<PlansEndpoint.PlansResponse> GetPlans(
        ClaimsPrincipal user,
        [Description("The FY's id. Leave out for the current FY.")] string? fyId = null)
    {
        var uid = Uid(user);
        var fy = fyId ?? await CurrentFyIdAsync(uid);
        return await PlansEndpoint.LoadAsync(uid, fy, store) ?? throw new McpException($"There is no FY {fy}. get_calendar lists them.");
    }

    [McpServerTool(Name = "get_income", Title = "Income", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Income for one FY: the salary history, the additional income items (what they count in and the account they go into), and the planned and actual income per month.")]
    public async Task<IncomeEndpoint.IncomeResponse> GetIncome(
        ClaimsPrincipal user,
        [Description("The FY's id. Leave out for the current FY.")] string? fyId = null) =>
        await IncomeEndpoint.LoadAsync(Uid(user), fyId, store, time) ?? throw new McpException($"There is no FY {fyId}. get_calendar lists them.");

    [McpServerTool(Name = "get_banks", Title = "Banks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bank accounts (ids and names) with their opening balance and how today's balance adds up, the total, the income split and its history, and the transfers between accounts.")]
    public async Task<BanksEndpoint.BanksResponse> GetBanks(ClaimsPrincipal user) =>
        await BanksEndpoint.LoadAsync(Uid(user), store, time) is { } data ? BanksEndpoint.ToResponse(data) : throw NotSetUp();

    [McpServerTool(Name = "get_loans", Title = "Loans", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Loans (information only, they change no balance): each loan with its name, type (lent or borrowed), amount, date, account and status, the totals, and the open loans by person.")]
    public async Task<LoansEndpoint.LoansResponse> GetLoans(ClaimsPrincipal user) =>
        await LoansEndpoint.LoadAsync(Uid(user), store, time) ?? throw NotSetUp();

    private static string Uid(ClaimsPrincipal user) => user.FindFirstValue("sub")!;

    // The FY that contains today, else the latest (as GET /api/entries and GET /api/income).
    private async Task<string> CurrentFyIdAsync(string uid)
    {
        var me = await MeEndpoint.LoadAsync(uid, isAdmin: false, store, time);
        return me.CurrentFyId
            ?? (await store.GetFiscalYearsAsync(uid)).OrderBy(fiscalYear => fiscalYear.StartDate).LastOrDefault()?.Id
            ?? throw NotSetUp();
    }

    private static McpException NotSetUp() => new("NAMA is not set up yet for this account: finish Setup in the app first.");
}
