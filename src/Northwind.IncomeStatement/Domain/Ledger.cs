namespace Northwind.IncomeStatement.Domain;

public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense,
}

public enum AccountSubtype
{
    OperatingRevenue,
    ContraRevenue,
    Cogs,
    OperatingExpense,
    OtherIncome,
    BalanceSheet,
}

public enum EntryStatus
{
    Posted,
    Draft,
    Void,
}

/// <param name="Number">Fixed-length account identifier. Always a string; never parsed as a number.</param>
/// <param name="IsActive">Whether new postings are allowed today. Not a reporting filter.</param>
public sealed record Account(string Number, string Name, AccountType Type, AccountSubtype Subtype, bool IsActive);

public sealed record JournalLine(string AccountNumber, Money Debit, Money Credit);

public sealed record JournalEntry(string Id, DateOnly Date, EntryStatus Status, string Memo, IReadOnlyList<JournalLine> Lines);

public sealed class Ledger
{
    public Ledger(string company, string currency, IEnumerable<Account> accounts, IEnumerable<JournalEntry> entries)
    {
        Company = company;
        Currency = currency;
        Accounts = accounts.ToDictionary(a => a.Number, StringComparer.Ordinal);
        Entries = entries.ToList();
    }

    public string Company { get; }

    public string Currency { get; }

    public IReadOnlyDictionary<string, Account> Accounts { get; }

    public IReadOnlyList<JournalEntry> Entries { get; }
}
