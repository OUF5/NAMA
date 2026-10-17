using System.Security.Claims;
using Api.Domain;
using Api.Features.Profile;
using Api.Infrastructure;

namespace Api.Features.FiscalYears;

// PRD 7.5 GET /api/fiscal-years. The months are the ones stored on each FY (R-CAL-09).
public static class FiscalYearsEndpoint
{
    public record FiscalYearDto(
        string Id,
        string Label,
        DateOnly StartDate,
        DateOnly EndDate,
        bool IsTransition,
        IReadOnlyList<MeEndpoint.PeriodDto> Periods)
    {
        public static FiscalYearDto From(FiscalYear fiscalYear) => new(
            fiscalYear.Id, fiscalYear.Label, fiscalYear.StartDate, fiscalYear.EndDate, fiscalYear.IsTransition,
            fiscalYear.Periods.Select(MeEndpoint.PeriodDto.From).ToList());
    }

    public static IEndpointRouteBuilder MapFiscalYears(this IEndpointRouteBuilder api)
    {
        api.MapGet("/fiscal-years", async (ClaimsPrincipal user, IUserStore store) =>
            (await store.GetFiscalYearsAsync(user.FindFirstValue("sub")!))
                .OrderBy(fiscalYear => fiscalYear.StartDate)
                .Select(FiscalYearDto.From)
                .ToList());
        return api;
    }
}
