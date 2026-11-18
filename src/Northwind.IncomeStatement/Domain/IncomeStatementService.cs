namespace Northwind.IncomeStatement.Domain;

public sealed record StatementLine(string AccountNumber, string AccountName, Money Amount);

public sealed record StatementSection(string Title, IReadOnlyList<StatementLine> Lines, Money Subtotal);

public sealed record BankEntryMatch(BankEntry BankEntry, JournalEntry JournalEntry)
{
    /// <summary>Bank date minus journal date: negative means the bank recorded it earlier.</summary>
    public int DateOffsetDays => BankEntry.Date.DayNumber - JournalEntry.Date.DayNumber;
}

public sealed record BankReconciliationResult(
    IReadOnlyList<BankEntryMatch> Matches,
    IReadOnlyList<BankEntry> UnmatchedBankEntries,
    IReadOnlyList<JournalEntry> UnmatchedJournalEntries);

public sealed record IncomeStatementReport(
    string Company,
    string Currency,
    DateOnly Start,
    DateOnly End,
    StatementSection Revenue,
    StatementSection CostOfGoodsSold,
    Money GrossProfit,
    StatementSection OperatingExpenses,
    Money OperatingIncome,
    StatementSection OtherIncome,
    Money NetIncome,
    bool HasActivity)
{
    public Money NetRevenue => Revenue.Subtotal;
}

/// <summary>
/// Computes the income statement for an inclusive date range. The single calculation used by both the API and the UI.
/// Also reconciles bank entries against posted cash journal entries.
/// </summary>
public sealed class IncomeStatementService(Ledger ledger)
{
    public const string CashAccountNumber = "1000";

    /// <summary>A bank entry may be dated up to this many calendar days before or after its journal entry.</summary>
    public const int MatchWindowDays = 3;

    /// <summary>
    /// One-to-one matching on exact signed cash amount within <see cref="MatchWindowDays"/>.
    /// Bank entries are processed earliest first; each takes the earliest eligible journal entry.
    /// </summary>
    public BankReconciliationResult MatchBankEntryWithCashEntry(IEnumerable<BankEntry> bankEntries)
    {
        var unmatchedJournalEntries = ledger.Entries
            .Where(entry => entry.Status == EntryStatus.Posted && entry.Lines.Any(line => line.AccountNumber == CashAccountNumber))
            .Select(entry => (Entry: entry, Amount: CashAmount(entry)))
            .OrderBy(candidate => candidate.Entry.Date)
            .ToList();
        var matches = new List<BankEntryMatch>();
        var unmatchedBankEntries = new List<BankEntry>();

        foreach (var bankEntry in bankEntries.OrderBy(entry => entry.Date))
        {
            var index = unmatchedJournalEntries.FindIndex(candidate =>
                candidate.Amount == bankEntry.Amount &&
                Math.Abs(bankEntry.Date.DayNumber - candidate.Entry.Date.DayNumber) <= MatchWindowDays);

            if (index < 0)
            {
                unmatchedBankEntries.Add(bankEntry);
                continue;
            }

            matches.Add(new BankEntryMatch(bankEntry, unmatchedJournalEntries[index].Entry));
            unmatchedJournalEntries.RemoveAt(index);
        }

        return new BankReconciliationResult(
            matches,
            unmatchedBankEntries,
            unmatchedJournalEntries.Select(candidate => candidate.Entry).ToList());
    }

    /// <summary>Net cash movement of an entry: cash debits minus cash credits.</summary>
    public static Money CashAmount(JournalEntry entry) =>
        Money.Sum(entry.Lines.Where(line => line.AccountNumber == CashAccountNumber).Select(line => line.Debit - line.Credit));

    public IncomeStatementReport Calculate(DateOnly start, DateOnly end)
    {
        if (start > end)
        {
            throw new ArgumentException($"Start date {start:yyyy-MM-dd} is after end date {end:yyyy-MM-dd}.", nameof(start));
        }

        // Net debit (debits − credits) per account; each section then applies its sign rule.
        var netDebits = new Dictionary<string, Money>(StringComparer.Ordinal);
        var hasActivity = false;
        foreach (var entry in ledger.Entries.Where(e => IsIncluded(e, start, end)))
        {
            foreach (var line in entry.Lines)
            {
                if (StatementLayout.SectionFor(ledger.Accounts[line.AccountNumber].Subtype) is null)
                {
                    continue;
                }

                netDebits[line.AccountNumber] = netDebits.GetValueOrDefault(line.AccountNumber) + line.Debit - line.Credit;
                hasActivity = true;
            }
        }

        StatementSection Build(SectionDefinition definition)
        {
            var lines = ledger.Accounts.Values
                .Where(a => definition.Subtypes.Contains(a.Subtype))
                .OrderBy(a => a.Number, StringComparer.Ordinal)
                .Select(a =>
                {
                    var netDebit = netDebits.GetValueOrDefault(a.Number);
                    var amount = definition.Sign == SignRule.CreditsMinusDebits ? -netDebit : netDebit;
                    return new StatementLine(a.Number, a.Name, amount);
                })
                .ToList();
            return new StatementSection(definition.Title, lines, Money.Sum(lines.Select(l => l.Amount)));
        }

        var revenue = Build(StatementLayout.Revenue);
        var cogs = Build(StatementLayout.CostOfGoodsSold);
        var operatingExpenses = Build(StatementLayout.OperatingExpenses);
        var otherIncome = Build(StatementLayout.OtherIncome);

        var grossProfit = revenue.Subtotal - cogs.Subtotal;
        var operatingIncome = grossProfit - operatingExpenses.Subtotal;
        var netIncome = operatingIncome + otherIncome.Subtotal;

        return new IncomeStatementReport(
            ledger.Company,
            ledger.Currency,
            start,
            end,
            revenue,
            cogs,
            grossProfit,
            operatingExpenses,
            operatingIncome,
            otherIncome,
            netIncome,
            hasActivity);
    }

    /// <summary>The one rule deciding which journal entries count: posted, dated within the inclusive range.</summary>
    private static bool IsIncluded(JournalEntry entry, DateOnly start, DateOnly end) =>
        entry.Status == EntryStatus.Posted && entry.Date >= start && entry.Date <= end;
}
