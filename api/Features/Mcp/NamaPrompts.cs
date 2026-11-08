using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Api.Features.Mcp;

// MCP-07 (owner note 34): ready-made questions Claude can start from. Each is a message to Claude that
// names the tools to call and what to answer; the numbers come from the tools, never from the prompt.
// They read nothing themselves, so they need no user id.
[McpServerPromptType]
public sealed class NamaPrompts
{
    [McpServerPrompt(Name = "monthly_review", Title = "Monthly review")]
    [Description("Review the month in progress, or a month you name, against the plan: what is left, what to watch, what last "
        + "month left to make up, and what to do about it.")]
    public static string MonthlyReview(
        [Description("A month (its key, YYYY-MM, or its name, like Nov 2026). Leave out for the month in progress.")] string? month = null) =>
        "Using NAMA, review "
        + (string.IsNullOrWhiteSpace(month) ? "the month in progress" : $"the month {month.Trim()}")
        + " against the plan. Start with get_briefing; for another month, use get_calendar to find its key and get_month for it. "
        + "Say, in the app's words: Remaining this month and its status, and how many days are left; Overspent last month and "
        + "Remaining - Overspent last month; the sub-categories near or over plan, with spent of plan; the biggest expenses; and "
        + "income against the plan. End with up to three concrete suggestions for the rest of the month, each with its amount. "
        + "Keep it short, and use the profile's currency.";

    [McpServerPrompt(Name = "spending_check", Title = "Spending check")]
    [Description("Look at one category or sub-category over the fiscal year: each month's spending against its plan, the trend, "
        + "and the biggest entries.")]
    public static string SpendingCheck(
        [Description("The category or sub-category, by name (for example Food or Groceries).")] string category) =>
        $"Using NAMA, check my spending on \"{category.Trim()}\" this fiscal year. Find it with get_categories (a category or a "
        + "sub-category; if several match, say which you chose). Then use get_overview filtered to it for each month's plan and "
        + "spent, and list_entries filtered to it (type expense) for its entries. Say: each started month's spent against its own "
        + "plan and its status, the total so far against the plan so far, the monthly average, the highest month, whether it is "
        + "going up or down, and its 3 biggest entries. End with one suggestion: keep the plan, or change it to an amount you name.";

    [McpServerPrompt(Name = "savings_outlook", Title = "Savings outlook")]
    [Description("Where the fiscal year is heading: saving so far and at its end against the plan, the projected balance, the "
        + "saving projects, and what would close any gap.")]
    public static string SavingsOutlook() =>
        "Using NAMA, tell me where my fiscal year is heading. Start with get_briefing (its outlook and projects); use get_balances "
        + "for each month's end balance and get_projects for a project's months and milestones. Say: income, spent and saving so "
        + "far against the plan; the projected saving and balance at the FY's end against Plan's saving, and the difference; "
        + "each project's state, money against its target, and what more a month it needs; and open loans (information only). "
        + "End with what would close any gap: which categories to cut and by how much a month.";
}
