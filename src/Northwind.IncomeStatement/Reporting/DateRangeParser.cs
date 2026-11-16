using System.Globalization;

namespace Northwind.IncomeStatement.Reporting;

/// <summary>
/// Validates a user-supplied reporting period. Shared by the API and the UI so both apply identical rules.
/// </summary>
public static class DateRangeParser
{
    public const string Format = "yyyy-MM-dd";

    public sealed record Result(DateOnly Start, DateOnly End, IReadOnlyDictionary<string, string[]> Errors)
    {
        public bool IsValid => Errors.Count == 0;
    }

    public static Result Parse(string? start, string? end)
    {
        var errors = new Dictionary<string, string[]>();
        var startDate = ParseDate("start", start, errors);
        var endDate = ParseDate("end", end, errors);

        if (startDate is { } s && endDate is { } e && s > e)
        {
            errors["start"] = [$"Start date {s.ToString(Format, CultureInfo.InvariantCulture)} must be on or before end date {e.ToString(Format, CultureInfo.InvariantCulture)}."];
        }

        return new Result(startDate ?? default, endDate ?? default, errors);
    }

    private static DateOnly? ParseDate(string name, string? text, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(text))
        {
            errors[name] = [$"The '{name}' date is required (format YYYY-MM-DD)."];
            return null;
        }

        if (!DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors[name] = [$"The '{name}' date '{text}' is not a real calendar date in the format YYYY-MM-DD."];
            return null;
        }

        return date;
    }
}
