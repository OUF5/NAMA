using Api.Domain;

namespace Api.Tests;

// R-INC-01 to R-INC-03. Amounts are minor units (10,000 SAR = 1,000,000).
public class IncomeCalculatorTests
{
    private static IncomeItem Salary(string id, long amount, string from, string? to = null) =>
        new(id, IncomeKind.Salary, "Salary", amount, from, to);

    private static IncomeItem Extra(string id, long amount, string from, string? to = null) =>
        new(id, IncomeKind.Additional, id, amount, from, to);

    // R-INC-03: Once = one month.
    [Fact]
    public void Once_item_counts_only_in_its_month()
    {
        var bonus = Extra("bonus", 50_000, "2026-03", "2026-03");

        Assert.False(IncomeCalculator.IsActive(bonus, "2026-02"));
        Assert.True(IncomeCalculator.IsActive(bonus, "2026-03"));
        Assert.False(IncomeCalculator.IsActive(bonus, "2026-04"));
    }

    // R-INC-03: Monthly = from a month, optionally until a month. Empty end = open ended (PRD 7.4).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Open_ended_monthly_item_counts_from_its_first_month(string? to)
    {
        var rent = Extra("rent", 200_000, "2026-03", to);

        Assert.False(IncomeCalculator.IsActive(rent, "2026-02"));
        Assert.True(IncomeCalculator.IsActive(rent, "2026-03"));
        Assert.True(IncomeCalculator.IsActive(rent, "2027-05"));
    }

    [Fact]
    public void Monthly_item_with_an_end_month_stops_after_it()
    {
        var rent = Extra("rent", 200_000, "2026-03", "2026-05");

        Assert.True(IncomeCalculator.IsActive(rent, "2026-05"));
        Assert.False(IncomeCalculator.IsActive(rent, "2026-06"));
    }

    // R-INC-01
    [Fact]
    public void Total_income_is_salary_plus_additional_income()
    {
        IncomeItem[] items =
        [
            Salary("s1", 1_000_000, "2026-01"),
            Extra("rent", 200_000, "2026-01"),
            Extra("bonus", 50_000, "2026-03", "2026-03"),
        ];

        Assert.Equal(1_250_000, IncomeCalculator.TotalForPeriod(items, "2026-03"));
        Assert.Equal(1_200_000, IncomeCalculator.TotalForPeriod(items, "2026-04"));
        Assert.Equal(0, IncomeCalculator.TotalForPeriod(items, "2025-12"));
    }

    [Fact]
    public void Without_salary_the_total_is_the_additional_income()
    {
        IncomeItem[] items = [Extra("bonus", 50_000, "2026-03", "2026-03")];

        Assert.Equal(50_000, IncomeCalculator.TotalForPeriod(items, "2026-03"));
        Assert.Equal(0, IncomeCalculator.TotalForPeriod([], "2026-03"));
    }

    // R-INC-02: earlier months keep the old salary.
    [Fact]
    public void Salary_history_keeps_the_old_salary_for_earlier_months()
    {
        IncomeItem[] items =
        [
            Salary("s1", 1_000_000, "2026-01", "2026-05"),
            Salary("s2", 1_200_000, "2026-06"),
        ];

        Assert.Equal(1_000_000, IncomeCalculator.TotalForPeriod(items, "2026-05"));
        Assert.Equal(1_200_000, IncomeCalculator.TotalForPeriod(items, "2026-06"));
    }

    [Fact]
    public void Changing_the_salary_ends_the_current_salary_the_month_before()
    {
        var rent = Extra("rent", 200_000, "2026-01");

        var result = IncomeCalculator.ChangeSalary([Salary("s1", 1_000_000, "2026-01"), rent], "s2", 1_200_000, "2026-06");

        Assert.Equal(
            new[] { Salary("s1", 1_000_000, "2026-01", "2026-05"), rent, Salary("s2", 1_200_000, "2026-06") },
            result);
    }

    // Owner's rule (M2): salary items starting at or after the effective month are replaced.
    [Fact]
    public void Changing_the_salary_replaces_later_salary_items()
    {
        IncomeItem[] items =
        [
            Salary("s1", 1_000_000, "2026-01", "2026-05"),
            Salary("s2", 1_200_000, "2026-06"),
        ];

        var result = IncomeCalculator.ChangeSalary(items, "s3", 1_100_000, "2026-03");

        Assert.Equal(
            new[] { Salary("s1", 1_000_000, "2026-01", "2026-02"), Salary("s3", 1_100_000, "2026-03") },
            result);
        Assert.Equal(1_100_000, IncomeCalculator.TotalForPeriod(result, "2026-06"));
    }

    [Fact]
    public void Changing_the_salary_in_its_first_month_replaces_it()
    {
        var result = IncomeCalculator.ChangeSalary([Salary("s1", 1_000_000, "2026-01")], "s2", 900_000, "2026-01");

        Assert.Equal(new[] { Salary("s2", 900_000, "2026-01") }, result);
    }

    [Fact]
    public void Changing_the_salary_keeps_salary_items_that_ended_earlier()
    {
        IncomeItem[] items =
        [
            Salary("s1", 1_000_000, "2026-01", "2026-03"),
            Salary("s2", 1_100_000, "2026-04"),
        ];

        var result = IncomeCalculator.ChangeSalary(items, "s3", 1_200_000, "2027-01");

        Assert.Equal(
            new[]
            {
                Salary("s1", 1_000_000, "2026-01", "2026-03"),
                Salary("s2", 1_100_000, "2026-04", "2026-12"),
                Salary("s3", 1_200_000, "2027-01"),
            },
            result);
    }

    [Fact]
    public void Totals_for_every_month_of_a_fiscal_year()
    {
        var fy = FiscalCalendar.FirstFiscalYear(new DateOnly(2026, 10, 17), fiscalStartMonth: 1, payday: 27);
        IncomeItem[] items =
        [
            Salary("s1", 1_000_000, "2026-01", "2026-05"),
            Salary("s2", 1_200_000, "2026-06"),
            Extra("bonus", 50_000, "2026-03", "2026-03"),
        ];

        var totals = IncomeCalculator.TotalsForPeriods(items, fy.Periods);

        Assert.Equal(12, totals.Count);
        Assert.Equal(1_000_000, totals["2026-01"]);
        Assert.Equal(1_050_000, totals["2026-03"]);
        Assert.Equal(1_000_000, totals["2026-05"]);
        Assert.Equal(1_200_000, totals["2026-12"]);
    }
}
