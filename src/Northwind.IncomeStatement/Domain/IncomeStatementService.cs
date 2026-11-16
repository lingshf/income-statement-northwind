namespace Northwind.IncomeStatement.Domain;

public sealed record StatementLine(string AccountNumber, string AccountName, Money Amount);

public sealed record StatementSection(string Title, IReadOnlyList<StatementLine> Lines, Money Subtotal);

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
/// </summary>
public sealed class IncomeStatementService(Ledger ledger)
{
    public IncomeStatementReport Calculate(DateOnly start, DateOnly end)
    {
        if (start > end)
        {
            throw new ArgumentException($"Start date {start:yyyy-MM-dd} is after end date {end:yyyy-MM-dd}.", nameof(start));
        }

        var activity = new Dictionary<string, (Money Debits, Money Credits)>(StringComparer.Ordinal);
        var hasActivity = false;
        foreach (var entry in ledger.Entries.Where(e => IsIncluded(e, start, end)))
        {
            foreach (var line in entry.Lines)
            {
                if (StatementLayout.SectionFor(ledger.Accounts[line.AccountNumber].Subtype) is null)
                {
                    continue;
                }

                var (debits, credits) = activity.GetValueOrDefault(line.AccountNumber);
                activity[line.AccountNumber] = (debits + line.Debit, credits + line.Credit);
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
                    var (debits, credits) = activity.GetValueOrDefault(a.Number);
                    var amount = definition.Sign == SignRule.CreditsMinusDebits ? credits - debits : debits - credits;
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
