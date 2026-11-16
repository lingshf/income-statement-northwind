using System.Globalization;
using System.Text.Json;

namespace Northwind.IncomeStatement.Domain;

public sealed class LedgerValidationException(IReadOnlyList<string> errors)
    : Exception("Ledger is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => " - " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Reads <c>ledger.json</c> and validates it completely before anything is reported.
/// Any problem fails fast with a <see cref="LedgerValidationException"/> listing every error found.
/// </summary>
public static class LedgerLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly Dictionary<string, AccountType> AccountTypes = new(StringComparer.Ordinal)
    {
        ["asset"] = AccountType.Asset,
        ["liability"] = AccountType.Liability,
        ["equity"] = AccountType.Equity,
        ["revenue"] = AccountType.Revenue,
        ["expense"] = AccountType.Expense,
    };

    private static readonly Dictionary<string, AccountSubtype> AccountSubtypes = new(StringComparer.Ordinal)
    {
        ["operating_revenue"] = AccountSubtype.OperatingRevenue,
        ["contra_revenue"] = AccountSubtype.ContraRevenue,
        ["cogs"] = AccountSubtype.Cogs,
        ["operating_expense"] = AccountSubtype.OperatingExpense,
        ["other_income"] = AccountSubtype.OtherIncome,
        ["balance_sheet"] = AccountSubtype.BalanceSheet,
    };

    private static readonly Dictionary<string, EntryStatus> Statuses = new(StringComparer.Ordinal)
    {
        ["posted"] = EntryStatus.Posted,
        ["draft"] = EntryStatus.Draft,
        ["void"] = EntryStatus.Void,
    };

    public static Ledger LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new LedgerValidationException([$"Ledger file not found: {path}"]);
        }

        return Parse(File.ReadAllText(path));
    }

    public static Ledger Parse(string json)
    {
        RawLedger? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawLedger>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new LedgerValidationException([$"Ledger is not valid JSON: {ex.Message}"]);
        }

        if (raw is null)
        {
            throw new LedgerValidationException(["Ledger is empty."]);
        }

        var errors = new List<string>();
        var accounts = ValidateAccounts(raw.Accounts, errors);
        var entries = ValidateEntries(raw.JournalEntries, accounts, errors);

        if (string.IsNullOrWhiteSpace(raw.Company))
        {
            errors.Add("Ledger 'company' is missing.");
        }

        if (string.IsNullOrWhiteSpace(raw.Currency))
        {
            errors.Add("Ledger 'currency' is missing.");
        }

        if (errors.Count > 0)
        {
            throw new LedgerValidationException(errors);
        }

        return new Ledger(raw.Company!, raw.Currency!, accounts.Values, entries);
    }

    private static Dictionary<string, Account> ValidateAccounts(List<RawAccount?>? rawAccounts, List<string> errors)
    {
        var accounts = new Dictionary<string, Account>(StringComparer.Ordinal);
        if (rawAccounts is null || rawAccounts.Count == 0)
        {
            errors.Add("Ledger has no 'accounts'.");
            return accounts;
        }

        int? numberLength = null;
        for (var i = 0; i < rawAccounts.Count; i++)
        {
            var raw = rawAccounts[i];
            var where = $"Account #{i + 1}";
            if (raw is null)
            {
                errors.Add($"{where} is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw.Number) || raw.Number.Trim() != raw.Number)
            {
                errors.Add($"{where} has a missing or blank-padded 'number' ('{raw.Number}').");
                continue;
            }

            where = $"Account {raw.Number}";
            numberLength ??= raw.Number.Length;
            if (raw.Number.Length != numberLength)
            {
                errors.Add($"{where} has length {raw.Number.Length}; all account numbers must have length {numberLength}.");
            }

            if (string.IsNullOrWhiteSpace(raw.Name))
            {
                errors.Add($"{where} has no 'name'.");
            }

            var typeOk = TryMap(AccountTypes, raw.Type, out var type);
            if (!typeOk)
            {
                errors.Add($"{where} has unknown type '{raw.Type}'.");
            }

            var subtypeOk = TryMap(AccountSubtypes, raw.Subtype, out var subtype);
            if (!subtypeOk)
            {
                errors.Add($"{where} has unknown subtype '{raw.Subtype}'.");
            }

            if (typeOk && subtypeOk && !IsConsistent(type, subtype))
            {
                errors.Add($"{where} has subtype '{raw.Subtype}', which is not valid for type '{raw.Type}'.");
            }

            if (raw.IsActive is null)
            {
                errors.Add($"{where} has no 'is_active'.");
            }

            if (!accounts.TryAdd(raw.Number, new Account(raw.Number, raw.Name ?? "", type, subtype, raw.IsActive ?? false)))
            {
                errors.Add($"{where} is defined more than once.");
            }
        }

        return accounts;
    }

    private static List<JournalEntry> ValidateEntries(
        List<RawEntry?>? rawEntries,
        Dictionary<string, Account> accounts,
        List<string> errors)
    {
        var entries = new List<JournalEntry>();
        if (rawEntries is null)
        {
            errors.Add("Ledger has no 'journal_entries'.");
            return entries;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rawEntries.Count; i++)
        {
            var raw = rawEntries[i];
            if (raw is null)
            {
                errors.Add($"Journal entry #{i + 1} is null.");
                continue;
            }

            var where = string.IsNullOrWhiteSpace(raw.Id) ? $"Journal entry #{i + 1}" : $"Journal entry {raw.Id}";
            if (string.IsNullOrWhiteSpace(raw.Id))
            {
                errors.Add($"{where} has no 'id'.");
            }
            else if (!ids.Add(raw.Id))
            {
                errors.Add($"{where} is defined more than once.");
            }

            var dateOk = DateOnly.TryParseExact(raw.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
            if (!dateOk)
            {
                errors.Add($"{where} has invalid date '{raw.Date}'; expected a real calendar date as YYYY-MM-DD.");
            }

            if (!TryMap(Statuses, raw.Status, out var status))
            {
                errors.Add($"{where} has unknown status '{raw.Status}'.");
            }

            var lines = ValidateLines(raw.Lines, where, accounts, errors, out var linesOk);
            if (linesOk)
            {
                var debits = Money.Sum(lines.Select(l => l.Debit));
                var credits = Money.Sum(lines.Select(l => l.Credit));
                if (debits != credits)
                {
                    errors.Add($"{where} is unbalanced: debits {debits} != credits {credits}.");
                }
            }

            entries.Add(new JournalEntry(raw.Id ?? "", date, status, raw.Memo ?? "", lines));
        }

        return entries;
    }

    private static List<JournalLine> ValidateLines(
        List<RawLine?>? rawLines,
        string entry,
        Dictionary<string, Account> accounts,
        List<string> errors,
        out bool allLinesValid)
    {
        var lines = new List<JournalLine>();
        allLinesValid = true;
        if (rawLines is null || rawLines.Count < 2)
        {
            errors.Add($"{entry} must have at least 2 lines.");
            allLinesValid = false;
            return lines;
        }

        for (var i = 0; i < rawLines.Count; i++)
        {
            var raw = rawLines[i];
            var where = $"{entry} line {i + 1}";
            if (raw is null)
            {
                errors.Add($"{where} is null.");
                allLinesValid = false;
                continue;
            }

            if (raw.Account is null || !accounts.ContainsKey(raw.Account))
            {
                errors.Add($"{where} references unknown account '{raw.Account}'.");
                allLinesValid = false;
            }

            var debitOk = TryParseAmount(raw.Debit, $"{where} debit", errors, out var debit);
            var creditOk = TryParseAmount(raw.Credit, $"{where} credit", errors, out var credit);
            if (!debitOk || !creditOk)
            {
                allLinesValid = false;
                continue;
            }

            if (debit.IsZero == credit.IsZero)
            {
                errors.Add($"{where} must have exactly one of debit/credit non-zero (debit {debit}, credit {credit}).");
                allLinesValid = false;
            }

            lines.Add(new JournalLine(raw.Account ?? "", debit, credit));
        }

        return lines;
    }

    private static bool TryParseAmount(string? text, string where, List<string> errors, out Money amount)
    {
        if (!Money.TryParse(text, out amount))
        {
            errors.Add($"{where} '{text}' is not a valid amount with exactly 2 decimals.");
            return false;
        }

        if (amount.IsNegative)
        {
            errors.Add($"{where} '{text}' must not be negative.");
            return false;
        }

        return true;
    }

    private static bool TryMap<T>(Dictionary<string, T> map, string? key, out T value) where T : struct
    {
        value = default;
        return key is not null && map.TryGetValue(key, out value);
    }

    private static bool IsConsistent(AccountType type, AccountSubtype subtype) => type switch
    {
        AccountType.Revenue => subtype is AccountSubtype.OperatingRevenue or AccountSubtype.ContraRevenue or AccountSubtype.OtherIncome,
        AccountType.Expense => subtype is AccountSubtype.Cogs or AccountSubtype.OperatingExpense,
        _ => subtype is AccountSubtype.BalanceSheet,
    };

    private sealed record RawLedger(string? Company, string? Currency, List<RawAccount?>? Accounts, List<RawEntry?>? JournalEntries);

    private sealed record RawAccount(string? Number, string? Name, string? Type, string? Subtype, bool? IsActive);

    private sealed record RawEntry(string? Id, string? Date, string? Status, string? Memo, List<RawLine?>? Lines);

    private sealed record RawLine(string? Account, string? Debit, string? Credit);
}
