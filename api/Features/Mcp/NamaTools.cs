using System.ComponentModel;
using System.Security.Claims;
using Api.Features.BankAccounts;
using Api.Features.Banks;
using Api.Features.Categories;
using Api.Features.Entries;
using Api.Features.FiscalYears;
using Api.Features.Income;
using Api.Features.Loans;
using Api.Features.Overview;
using Api.Features.Plans;
using Api.Features.Profile;
using Api.Features.Summary;
using Api.Infrastructure;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Api.Features.Mcp;

// MCP-04: the Claude connector's tools, all read-only. Each one answers with what its page's endpoint
// answers (the same LoadAsync), so Claude and the app always agree. The user id is the connection's
// "sub" (McpTokenHandler), never an argument. People and invite links are never shown.
[McpServerToolType]
public sealed class NamaTools(IUserStore store, TimeProvider time, EmailAllowlist admins)
{
    // MCP-04: list_entries sends at most this many entries, the newest; Count and Total cover all.
    public const int MaxEntries = 300;

    public const string Instructions =
        "NAMA is a personal budget tracked by fiscal year (FY). A month runs from payday to the day before the next payday, "
        + "and its key (periodKey, YYYY-MM) is the month it starts in; its label is its name on screen. "
        + "Amounts are decimals in the profile's currency. Spending is planned per sub-category per month; entries are expenses. "
        + "Ids (categoryId, subCategoryId, bankAccountId) are named by get_categories and get_banks. "
        + "Start with get_calendar for today, the current FY and month. Every tool only reads: nothing can be changed here.";

    public record CalendarResult(MeEndpoint.MeResponse Me, IReadOnlyList<FiscalYearsEndpoint.FiscalYearDto> FiscalYears);

    // Truncated is true when there are more than MaxEntries matching entries.
    public record EntriesResult(
        string FyId,
        int Count,
        decimal Total,
        bool Truncated,
        IReadOnlyList<EntriesEndpoint.EntryDto> Entries,
        IReadOnlyList<BankAccountsEndpoint.BankAccountDto> BankAccounts);

    [McpServerTool(Name = "get_calendar", Title = "Calendar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("The profile (name, currency, time zone, payday), today, the current FY and month, and every FY with its months (key, label, start and end date).")]
    public async Task<CalendarResult> GetCalendar(ClaimsPrincipal user)
    {
        var uid = Uid(user);
        var fiscalYears = (await store.GetFiscalYearsAsync(uid)).OrderBy(fiscalYear => fiscalYear.StartDate).Select(FiscalYearsEndpoint.FiscalYearDto.From);
        return new CalendarResult(await MeEndpoint.LoadAsync(uid, admins.IsAdmin(user), store, time), fiscalYears.ToList());
    }

    [McpServerTool(Name = "get_summary", Title = "Summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("The top bar and Dashboard: the month in progress (income, carried in, planned, spent, remaining, unassigned, days left), the total bank balance and each account's, and the projection to the FY's end.")]
    public async Task<SummaryEndpoint.SummaryResponse> GetSummary(ClaimsPrincipal user) =>
        await SummaryEndpoint.LoadAsync(Uid(user), store, time) ?? throw NotSetUp();

    [McpServerTool(Name = "get_overview", Title = "Tracker overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Tracker for one FY: per month its status, income, planned, spent, remaining, unspent and saved; per sub-category per month planned, spent and remaining; the FY's totals and plan vs actual. Optionally narrowed to a category or sub-category.")]
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
    [Description("One month in detail: spending by day and by bank account, each category against the month before, the 5 newest and 5 biggest entries, the count and total, and the month's additional income.")]
    public async Task<MonthEndpoint.MonthResponse> GetMonth(
        ClaimsPrincipal user,
        [Description("The FY's id. Leave out for the current FY.")] string? fyId = null,
        [Description("The month's key (YYYY-MM, the month it starts in). Leave out for the current month.")] string? periodKey = null,
        [Description("Only this category.")] string? categoryId = null,
        [Description("Only this sub-category.")] string? subCategoryId = null)
    {
        var uid = Uid(user);
        var me = await MeEndpoint.LoadAsync(uid, isAdmin: false, store, time);
        var fy = fyId ?? me.CurrentFyId ?? await CurrentFyIdAsync(uid);
        var month = periodKey ?? (fy == me.CurrentFyId ? me.CurrentPeriodKey : null)
            ?? throw new McpException("No month is in progress in that FY: name the month (periodKey). get_calendar lists them.");
        return await MonthEndpoint.LoadAsync(uid, fy, month, categoryId, subCategoryId, store)
            ?? throw new McpException($"There is no month {month} in FY {fy}. get_calendar lists them.");
    }

    [McpServerTool(Name = "list_entries", Title = "Entries", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Expenses of one FY, newest first, with filters, and the count and total of every match. At most 300 entries are listed (Truncated says when there are more); narrow the filters to see the rest.")]
    public async Task<EntriesResult> ListEntries(
        ClaimsPrincipal user,
        [Description("The FY's id. Leave out for the current FY.")] string? fyId = null,
        [Description("Only this month (YYYY-MM).")] string? periodKey = null,
        [Description("Only this category.")] string? categoryId = null,
        [Description("Only this sub-category.")] string? subCategoryId = null,
        [Description("Only this bank account.")] string? bankAccountId = null,
        [Description("Words to find in the notes, ignoring case.")] string? q = null)
    {
        var filter = new EntryFilter(fyId, periodKey, categoryId, subCategoryId, bankAccountId, q);
        var response = await EntriesEndpoint.LoadAsync(Uid(user), filter, store, time)
            ?? throw new McpException($"There is no FY {fyId}. get_calendar lists them.");
        return new EntriesResult(
            response.FyId,
            response.Entries.Count,
            response.Total,
            response.Entries.Count > MaxEntries,
            response.Entries.Take(MaxEntries).ToList(),
            response.BankAccounts);
    }

    [McpServerTool(Name = "get_categories", Title = "Categories", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Every category with its sub-categories (ids and names), archived ones flagged.")]
    public Task<List<CategoriesEndpoint.CategoryDto>> GetCategories(ClaimsPrincipal user) => CategoriesEndpoint.LoadAsync(Uid(user), store);

    [McpServerTool(Name = "get_plans", Title = "Plan", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Plan for one FY: the amount planned per sub-category per month, and per month the carried in, planned income, planned and unassigned.")]
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
