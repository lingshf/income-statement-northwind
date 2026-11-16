using System.Globalization;
using Northwind.IncomeStatement.Domain;

namespace Northwind.IncomeStatement.Tests.Domain;

/// <summary>
/// Expected values are computed by hand from ledger.csv and the reconciliation table in spec.md,
/// never from production code.
/// </summary>
public class IncomeStatementServiceTests
{
    private static readonly Ledger SuppliedLedger =
        LedgerLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "ledger.json"));

    private static IncomeStatementReport Calculate(string start, string end) =>
        new IncomeStatementService(SuppliedLedger).Calculate(Date(start), Date(end));

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Money M(string amount) => Money.Parse(amount);

    private static Money Line(StatementSection section, string account) =>
        section.Lines.Single(l => l.AccountNumber == account).Amount;

    private static Money Line(IncomeStatementReport report, string account) =>
        new[] { report.Revenue, report.CostOfGoodsSold, report.OperatingExpenses, report.OtherIncome }
            .SelectMany(s => s.Lines)
            .Single(l => l.AccountNumber == account)
            .Amount;

    [Fact]
    public void Q1_2026_matches_the_reconciliation_table_to_the_cent()
    {
        var r = Calculate("2026-01-01", "2026-03-31");

        Assert.Equal("Northwind Coffee Roasters", r.Company);
        Assert.Equal("USD", r.Currency);
        Assert.Equal(new DateOnly(2026, 1, 1), r.Start);
        Assert.Equal(new DateOnly(2026, 3, 31), r.End);

        Assert.Equal(["4000", "4100", "4900"], r.Revenue.Lines.Select(l => l.AccountNumber));
        Assert.Equal(M("35650.75"), Line(r.Revenue, "4000"));
        Assert.Equal(M("3000.00"), Line(r.Revenue, "4100"));
        Assert.Equal(M("-800.25"), Line(r.Revenue, "4900"));
        Assert.Equal(M("37850.50"), r.Revenue.Subtotal);
        Assert.Equal(M("37850.50"), r.NetRevenue);

        Assert.Equal(["5000"], r.CostOfGoodsSold.Lines.Select(l => l.AccountNumber));
        Assert.Equal(M("14272.75"), r.CostOfGoodsSold.Subtotal);
        Assert.Equal(M("23577.75"), r.GrossProfit);

        Assert.Equal(["6000", "6100", "6200", "6300"], r.OperatingExpenses.Lines.Select(l => l.AccountNumber));
        Assert.Equal(M("55500.00"), Line(r.OperatingExpenses, "6000"));
        Assert.Equal(M("9000.00"), Line(r.OperatingExpenses, "6100"));
        Assert.Equal(M("1099.97"), Line(r.OperatingExpenses, "6200"));
        Assert.Equal(M("2500.10"), Line(r.OperatingExpenses, "6300"));
        Assert.Equal(M("68100.07"), r.OperatingExpenses.Subtotal);
        Assert.Equal(M("-44522.32"), r.OperatingIncome);

        Assert.Equal(["7000"], r.OtherIncome.Lines.Select(l => l.AccountNumber));
        Assert.Equal(M("42.18"), r.OtherIncome.Subtotal);

        Assert.Equal(M("-44480.14"), r.NetIncome);
        Assert.True(r.HasActivity);
    }

    [Fact]
    public void Inactive_account_still_reports_its_history()
    {
        Assert.False(SuppliedLedger.Accounts["6300"].IsActive);
        Assert.Equal(M("2500.10"), Line(Calculate("2026-01-20", "2026-01-20"), "6300"));
    }

    [Fact]
    public void Balance_sheet_accounts_are_never_on_the_statement()
    {
        var r = Calculate("2025-01-01", "2026-12-31");
        var accounts = new[] { r.Revenue, r.CostOfGoodsSold, r.OperatingExpenses, r.OtherIncome }
            .SelectMany(s => s.Lines)
            .Select(l => l.AccountNumber);

        Assert.Equal(["4000", "4100", "4900", "5000", "6000", "6100", "6200", "6300", "7000"], accounts);
    }

    [Fact]
    public void Contra_revenue_debits_are_negative_revenue()
    {
        // JE-012: return of 650.25 debited to 4900.
        var r = Calculate("2026-02-14", "2026-02-14");

        Assert.Equal(M("-650.25"), Line(r.Revenue, "4900"));
        Assert.Equal(M("-650.25"), r.NetRevenue);
        Assert.Equal(M("-650.25"), r.NetIncome);
    }

    [Fact]
    public void Multi_line_entry_splits_gross_sale_and_discount()
    {
        // JE-016: 15,000.00 gross sale, 150.00 discount; JE-017: 6,012.45 COGS, same day.
        var r = Calculate("2026-03-02", "2026-03-02");

        Assert.Equal(M("15000.00"), Line(r.Revenue, "4000"));
        Assert.Equal(M("-150.00"), Line(r.Revenue, "4900"));
        Assert.Equal(M("14850.00"), r.NetRevenue);
        Assert.Equal(M("6012.45"), r.CostOfGoodsSold.Subtotal);
        Assert.Equal(M("8837.55"), r.GrossProfit);
    }

    [Fact]
    public void Vendor_credit_reduces_expense()
    {
        // JE-020: 100.00 credit to 6200 on its own day shows as negative expense.
        var day = Calculate("2026-03-20", "2026-03-20");
        Assert.Equal(M("-100.00"), Line(day, "6200"));
        Assert.Equal(M("100.00"), day.NetIncome);

        // JE-018 1,199.97 less JE-020 100.00.
        Assert.Equal(M("1099.97"), Line(Calculate("2026-03-01", "2026-03-31"), "6200"));
    }

    [Fact]
    public void Draft_entries_are_excluded()
    {
        // JE-019 is a 5,000.00 draft bonus accrual and the only entry on 2026-03-15.
        var r = Calculate("2026-03-15", "2026-03-15");

        Assert.Equal(Money.Zero, Line(r, "6000"));
        Assert.False(r.HasActivity);
    }

    [Fact]
    public void Void_entries_are_excluded()
    {
        // JE-009 (void) duplicates JE-010 (posted, 8,200.00) on 2026-02-03; count it once.
        var r = Calculate("2026-02-03", "2026-02-03");
        Assert.Equal(M("8200.00"), Line(r, "4000"));
        Assert.Equal(M("3280.00"), r.CostOfGoodsSold.Subtotal);
        Assert.Equal(M("4920.00"), r.NetIncome);

        // JE-025 (void) is the only entry on 2026-03-18.
        Assert.False(Calculate("2026-03-18", "2026-03-18").HasActivity);
    }

    [Fact]
    public void Start_date_is_inclusive_and_prepaid_rent_is_not_spread()
    {
        // JE-007 expenses all three months of rent on 2026-01-01, the first day of the range.
        Assert.Equal(M("9000.00"), Line(Calculate("2026-01-01", "2026-01-01"), "6100"));
        Assert.Equal(Money.Zero, Line(Calculate("2026-01-02", "2026-03-31"), "6100"));
    }

    [Fact]
    public void End_date_is_inclusive()
    {
        // 2026-03-31: JE-021 subscription 1,000.00, JE-022 payroll 18,500.00, JE-023 interest 42.18.
        var r = Calculate("2026-03-31", "2026-03-31");
        Assert.Equal(M("1000.00"), Line(r, "4100"));
        Assert.Equal(M("18500.00"), Line(r, "6000"));
        Assert.Equal(M("42.18"), Line(r, "7000"));
        Assert.Equal(M("-17457.82"), r.NetIncome);

        var dayBefore = Calculate("2026-01-01", "2026-03-30");
        Assert.Equal(M("2000.00"), Line(dayBefore, "4100"));
        Assert.Equal(Money.Zero, dayBefore.OtherIncome.Subtotal);
    }

    [Fact]
    public void Entries_just_outside_the_range_are_excluded()
    {
        // JE-001 (2025-12-15, 5,000.00) and JE-024 (2026-04-01, 9,100.00) sit either side of Q1.
        Assert.Equal(M("5000.00"), Calculate("2025-12-01", "2025-12-31").NetIncome);
        Assert.Equal(M("9100.00"), Calculate("2026-04-01", "2026-04-01").NetIncome);
    }

    [Fact]
    public void Annual_subscription_is_revenue_only_when_recognized()
    {
        // JE-004 bills 12,000.00 to deferred revenue on 2026-01-10; only JE-005/014/021 recognize 1,000.00 each.
        Assert.Equal(Money.Zero, Line(Calculate("2026-01-10", "2026-01-10"), "4100"));
        Assert.Equal(M("1000.00"), Line(Calculate("2026-01-01", "2026-01-31"), "4100"));
        Assert.Equal(M("3000.00"), Line(Calculate("2026-01-01", "2026-12-31"), "4100"));
    }

    [Fact]
    public void Range_covering_all_data_includes_every_posted_entry()
    {
        // Q1 net income -44,480.14 plus JE-001 5,000.00 and JE-024 9,100.00.
        var r = Calculate("2000-01-01", "2099-12-31");

        Assert.Equal(M("49750.75"), Line(r, "4000"));
        Assert.Equal(M("-30380.14"), r.NetIncome);
    }

    [Fact]
    public void Empty_period_is_a_zero_statement_listing_every_account()
    {
        var r = Calculate("2030-01-01", "2030-12-31");

        Assert.False(r.HasActivity);
        Assert.Equal(3, r.Revenue.Lines.Count);
        Assert.Equal(4, r.OperatingExpenses.Lines.Count);
        Assert.All(
            new[] { r.Revenue, r.CostOfGoodsSold, r.OperatingExpenses, r.OtherIncome }.SelectMany(s => s.Lines),
            l => Assert.Equal(Money.Zero, l.Amount));
        Assert.Equal(Money.Zero, r.GrossProfit);
        Assert.Equal(Money.Zero, r.OperatingIncome);
        Assert.Equal(Money.Zero, r.NetIncome);
    }

    [Fact]
    public void Start_after_end_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Calculate("2026-03-31", "2026-01-01"));
    }

    [Fact]
    public void Lines_are_ordered_by_account_number_not_file_order()
    {
        var ledger = LedgerLoader.Parse("""
            {
              "company": "Test Co", "currency": "USD",
              "accounts": [
                {"number": "6200", "name": "B", "type": "expense", "subtype": "operating_expense", "is_active": true},
                {"number": "0100", "name": "Cash", "type": "asset", "subtype": "balance_sheet", "is_active": true},
                {"number": "6010", "name": "A", "type": "expense", "subtype": "operating_expense", "is_active": true}
              ],
              "journal_entries": []
            }
            """);

        var r = new IncomeStatementService(ledger).Calculate(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));

        Assert.Equal(["6010", "6200"], r.OperatingExpenses.Lines.Select(l => l.AccountNumber));
    }
}
