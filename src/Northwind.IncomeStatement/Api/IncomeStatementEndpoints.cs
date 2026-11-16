using Northwind.IncomeStatement.Domain;
using Northwind.IncomeStatement.Reporting;

namespace Northwind.IncomeStatement.Api;

public static class IncomeStatementEndpoints
{
    public static IEndpointRouteBuilder MapIncomeStatementApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/income-statement", (string? start, string? end, IncomeStatementService service) =>
        {
            var period = DateRangeParser.Parse(start, end);
            if (!period.IsValid)
            {
                return Results.ValidationProblem(
                    period.Errors,
                    detail: string.Join(" ", period.Errors.Values.SelectMany(v => v)),
                    title: "Invalid reporting period");
            }

            return Results.Ok(IncomeStatementResponse.From(service.Calculate(period.Start, period.End)));
        });

        return app;
    }
}

/// <summary>
/// JSON contract for <c>GET /income-statement</c>. Money values serialize as exact 2-decimal strings.
/// Kept separate from the domain records on purpose: it nests the period, names line fields
/// <c>account</c>/<c>name</c> and places <c>netRevenue</c> after the revenue section, so the wire
/// format stays stable when domain types change.
/// </summary>
public sealed record IncomeStatementResponse(
    string Company,
    string Currency,
    PeriodDto Period,
    SectionDto Revenue,
    Money NetRevenue,
    SectionDto CostOfGoodsSold,
    Money GrossProfit,
    SectionDto OperatingExpenses,
    Money OperatingIncome,
    SectionDto OtherIncome,
    Money NetIncome,
    bool HasActivity)
{
    public static IncomeStatementResponse From(IncomeStatementReport r) => new(
        r.Company,
        r.Currency,
        new PeriodDto(r.Start, r.End),
        SectionDto.From(r.Revenue),
        r.NetRevenue,
        SectionDto.From(r.CostOfGoodsSold),
        r.GrossProfit,
        SectionDto.From(r.OperatingExpenses),
        r.OperatingIncome,
        SectionDto.From(r.OtherIncome),
        r.NetIncome,
        r.HasActivity);
}

public sealed record PeriodDto(DateOnly Start, DateOnly End);

public sealed record SectionDto(string Title, IReadOnlyList<LineDto> Lines, Money Subtotal)
{
    public static SectionDto From(StatementSection s) =>
        new(s.Title, [.. s.Lines.Select(l => new LineDto(l.AccountNumber, l.AccountName, l.Amount))], s.Subtotal);
}

public sealed record LineDto(string Account, string Name, Money Amount);
