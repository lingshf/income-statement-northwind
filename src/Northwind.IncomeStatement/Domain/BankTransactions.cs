using System.Globalization;
using System.Text.Json;

namespace Northwind.IncomeStatement.Domain;

/// <param name="Amount">Signed: positive is money into the bank account, negative is money out.</param>
public sealed record BankEntry(string Id, DateOnly Date, Money Amount, string Description);

/// <summary>Bank statement lines, loaded separately from the ledger.</summary>
public sealed class BankTransactions(IEnumerable<BankEntry> entries)
{
    public IReadOnlyList<BankEntry> Entries { get; } = entries.ToList();
}

public sealed class BankTransactionsValidationException(IReadOnlyList<string> errors)
    : Exception("Bank transactions are invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => " - " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Reads <c>bank_transactions.json</c> and validates it completely; any problem fails fast with a
/// <see cref="BankTransactionsValidationException"/> listing every error found.
/// </summary>
public static class BankTransactionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static BankTransactions LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new BankTransactionsValidationException([$"Bank transactions file not found: {path}"]);
        }

        return Parse(File.ReadAllText(path));
    }

    public static BankTransactions Parse(string json)
    {
        RawBankFile? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawBankFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BankTransactionsValidationException([$"Bank transactions file is not valid JSON: {ex.Message}"]);
        }

        if (raw?.BankEntries is null)
        {
            throw new BankTransactionsValidationException(["Bank transactions file must contain a 'bank_entries' array."]);
        }

        var errors = new List<string>();
        var entries = new List<BankEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < raw.BankEntries.Count; i++)
        {
            var entry = raw.BankEntries[i];
            var where = $"Bank entry #{i + 1}";
            if (entry is null)
            {
                errors.Add($"{where} is null.");
                continue;
            }

            var hasId = !string.IsNullOrWhiteSpace(entry.Id);
            if (!hasId)
            {
                errors.Add($"{where} is missing 'id'.");
            }
            else
            {
                where = $"Bank entry {entry.Id}";
                if (!ids.Add(entry.Id!))
                {
                    errors.Add($"{where} is defined more than once.");
                }
            }

            var dateOk = DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
            if (!dateOk)
            {
                errors.Add($"{where} has invalid date '{entry.Date}'; expected a real calendar date as YYYY-MM-DD.");
            }

            var amountOk = Money.TryParse(entry.Amount, out var amount);
            if (!amountOk)
            {
                errors.Add($"{where} amount '{entry.Amount}' is not a valid amount with exactly 2 decimals.");
            }

            if (hasId && dateOk && amountOk)
            {
                entries.Add(new BankEntry(entry.Id!, date, amount, entry.Description ?? ""));
            }
        }

        if (errors.Count > 0)
        {
            throw new BankTransactionsValidationException(errors);
        }

        return new BankTransactions(entries);
    }

    private sealed record RawBankFile(List<RawBankEntry?>? BankEntries);

    private sealed record RawBankEntry(string? Id, string? Date, string? Amount, string? Description);
}
