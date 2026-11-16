using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Northwind.IncomeStatement.Domain;
using Northwind.IncomeStatement.Reporting;

namespace Northwind.IncomeStatement.Pages;

public class IndexModel(IncomeStatementService service) : PageModel
{
    public const string DefaultStart = "2026-01-01";
    public const string DefaultEnd = "2026-03-31";

    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? End { get; set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IncomeStatementReport? Report { get; private set; }

    public void OnGet()
    {
        // First visit (no query) shows the default period; any submitted value is validated as-is.
        if (!Request.Query.ContainsKey("start") && !Request.Query.ContainsKey("end"))
        {
            Start = DefaultStart;
            End = DefaultEnd;
        }

        var period = DateRangeParser.Parse(Start, End);
        if (!period.IsValid)
        {
            Errors = period.Errors.Values.SelectMany(v => v).ToList();
            return;
        }

        Report = service.Calculate(period.Start, period.End);
    }
}
