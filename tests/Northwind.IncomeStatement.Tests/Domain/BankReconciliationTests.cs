using Northwind.IncomeStatement.Domain;

namespace Northwind.IncomeStatement.Tests.Domain;

public class BankReconciliationTests
{
    [Theory]
    [InlineData(-4, false)]
    [InlineData(-3, true)]
    [InlineData(-2, true)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Bank_date_must_be_within_three_calendar_days_in_either_direction(int daysLater, bool matches)
    {
        var journal = Journal("JE-1", new DateOnly(2026, 1, 31), "-100.10");
        var bank = Bank("BT-1", journal.Date.AddDays(daysLater), "-100.10");

        var result = Reconcile([journal], [bank]);

        if (matches)
        {
            var match = Assert.Single(result.Matches);
            Assert.Same(journal, match.JournalEntry);
            Assert.Same(bank, match.BankEntry);
            Assert.Empty(result.UnmatchedBankEntries);
            Assert.Empty(result.UnmatchedJournalEntries);
        }
        else
        {
            Assert.Empty(result.Matches);
            Assert.Same(bank, Assert.Single(result.UnmatchedBankEntries));
            Assert.Same(journal, Assert.Single(result.UnmatchedJournalEntries));
        }
    }

    [Theory]
    [InlineData("100.10", "100.10", true)]
    [InlineData("-100.10", "-100.10", true)]
    [InlineData("100.10", "100.11", false)]
    [InlineData("-100.10", "-100.11", false)]
    [InlineData("100.10", "-100.10", false)]
    public void Amounts_must_match_exactly_including_sign_and_cents(string journalAmount, string bankAmount, bool matches)
    {
        var date = new DateOnly(2026, 3, 1);
        var result = Reconcile([Journal("JE-1", date, journalAmount)], [Bank("BT-1", date, bankAmount)]);

        Assert.Equal(matches ? 1 : 0, result.Matches.Count);
        Assert.Equal(matches ? 0 : 1, result.UnmatchedBankEntries.Count);
        Assert.Equal(matches ? 0 : 1, result.UnmatchedJournalEntries.Count);
    }

    [Fact]
    public void Only_posted_cash_entries_are_eligible()
    {
        var date = new DateOnly(2026, 3, 1);
        var entries = new[]
        {
            Journal("draft", date, "100.10") with { Status = EntryStatus.Draft },
            Journal("void", date, "100.10") with { Status = EntryStatus.Void },
            Journal("receivable", date, "100.10") with
            {
                Lines = [new JournalLine("1100", Money.Parse("100.10"), Money.Zero)],
            },
            Journal("posted", date, "100.10"),
        };

        var result = Reconcile(entries, [Bank("BT-1", date, "100.10")]);

        Assert.Equal("posted", Assert.Single(result.Matches).JournalEntry.Id);
        Assert.Empty(result.UnmatchedJournalEntries);
    }

    [Fact]
    public void Multiple_cash_lines_are_netted_without_including_other_accounts()
    {
        var date = new DateOnly(2026, 3, 1);
        var journal = Journal("JE-1", date, "0.00") with
        {
            Lines =
            [
                new JournalLine("1000", Money.Parse("100.10"), Money.Zero),
                new JournalLine("1000", Money.Zero, Money.Parse("20.05")),
                new JournalLine("4000", Money.Zero, Money.Parse("80.05")),
            ],
        };

        var result = Reconcile([journal], [Bank("BT-1", date, "80.05")]);

        Assert.Single(result.Matches);
        Assert.Empty(result.UnmatchedJournalEntries);
    }

    [Fact]
    public void Matching_is_one_to_one_in_date_then_input_order_without_mutating_the_ledger()
    {
        var date = new DateOnly(2026, 3, 1);
        var entries = new[]
        {
            Journal("later", date.AddDays(1), "100.10"),
            Journal("first-tie", date, "100.10"),
            Journal("second-tie", date, "100.10"),
        };
        var banks = new[]
        {
            Bank("later-bank", date.AddDays(2), "100.10"),
            Bank("first-bank", date.AddDays(1), "100.10"),
            Bank("second-bank", date.AddDays(1), "100.10"),
            Bank("duplicate-bank", date.AddDays(2), "100.10"),
        };
        var ledger = CreateLedger(entries);
        var service = new IncomeStatementService(ledger);
        var originalBanks = banks.ToArray();

        var result = service.MatchBankEntryWithCashEntry(banks);
        var repeated = service.MatchBankEntryWithCashEntry(banks);

        Assert.Equal(new[] { "first-bank", "second-bank", "later-bank" }, result.Matches.Select(m => m.BankEntry.Id));
        Assert.Equal(new[] { "first-tie", "second-tie", "later" }, result.Matches.Select(m => m.JournalEntry.Id));
        Assert.Equal("duplicate-bank", Assert.Single(result.UnmatchedBankEntries).Id);
        Assert.Empty(result.UnmatchedJournalEntries);
        Assert.Equal(result.Matches.ToArray(), repeated.Matches.ToArray());
        Assert.Equal(entries, ledger.Entries);
        Assert.Equal(originalBanks, banks);
    }

    [Fact]
    public void Earliest_journal_is_consumed_first_to_preserve_later_matching_opportunities()
    {
        var date = new DateOnly(2026, 3, 1);
        var result = Reconcile(
            [Journal("newer", date.AddDays(1), "100.10"), Journal("older", date, "100.10")],
            [Bank("later", date.AddDays(4), "100.10"), Bank("earlier", date.AddDays(3), "100.10")]);

        Assert.Equal(new[] { "older", "newer" }, result.Matches.Select(m => m.JournalEntry.Id));
        Assert.Empty(result.UnmatchedBankEntries);
        Assert.Empty(result.UnmatchedJournalEntries);
    }

    [Fact]
    public void Empty_inputs_and_unmatched_records_are_returned_correctly()
    {
        var date = new DateOnly(2026, 3, 1);
        var empty = Reconcile([], []);
        Assert.Empty(empty.Matches);
        Assert.Empty(empty.UnmatchedBankEntries);
        Assert.Empty(empty.UnmatchedJournalEntries);

        var noBank = Reconcile([Journal("later", date.AddDays(1), "1.00"), Journal("earlier", date, "2.00")], []);
        Assert.Empty(noBank.Matches);
        Assert.Equal(new[] { "earlier", "later" }, noBank.UnmatchedJournalEntries.Select(e => e.Id));

        var noJournal = Reconcile([], [Bank("later", date.AddDays(1), "1.00"), Bank("earlier", date, "2.00")]);
        Assert.Empty(noJournal.Matches);
        Assert.Equal(new[] { "earlier", "later" }, noJournal.UnmatchedBankEntries.Select(e => e.Id));
    }

    [Fact]
    public void Maximum_supported_date_does_not_overflow_the_matching_window()
    {
        var result = Reconcile(
            [Journal("JE-1", DateOnly.MaxValue, "1.00")],
            [Bank("BT-1", DateOnly.MaxValue, "1.00")]);

        Assert.Single(result.Matches);
    }

    [Fact]
    public void Supplied_files_reconcile_using_the_confirmed_date_rule()
    {
        var ledger = LedgerLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "ledger.json"));
        var bank = BankTransactionLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "bank_transactions.json"));
        var result = new IncomeStatementService(ledger).MatchBankEntryWithCashEntry(bank.Entries);

        Assert.Equal(7, bank.Entries.Count);
        Assert.Equal(new[] { "BT-01", "BT-02", "BT-03", "BT-04", "BT-06" }, result.Matches.Select(m => m.BankEntry.Id));
        Assert.Equal(new[] { "JE-007", "JE-006", "JE-013", "JE-015", "JE-022" }, result.Matches.Select(m => m.JournalEntry.Id));
        Assert.Equal(new[] { "BT-05", "BT-07" }, result.UnmatchedBankEntries.Select(e => e.Id));
        Assert.Equal(new[] { "JE-023" }, result.UnmatchedJournalEntries.Select(e => e.Id));
    }

    private static BankReconciliationResult Reconcile(IEnumerable<JournalEntry> journals, IEnumerable<BankEntry> banks) =>
        new IncomeStatementService(CreateLedger(journals)).MatchBankEntryWithCashEntry(banks);

    private static Ledger CreateLedger(IEnumerable<JournalEntry> journals) =>
        new("Test Co", "USD",
            [new Account("1000", "Cash", AccountType.Asset, AccountSubtype.BalanceSheet, true)],
            journals);

    private static BankEntry Bank(string id, DateOnly date, string amount) =>
        new(id, date, Money.Parse(amount), "Bank transaction");

    private static JournalEntry Journal(string id, DateOnly date, string amount)
    {
        var money = Money.Parse(amount);
        return new JournalEntry(id, date, EntryStatus.Posted, "Journal transaction",
        [
            new JournalLine("1000", money.IsNegative ? Money.Zero : money, money.IsNegative ? -money : Money.Zero),
            new JournalLine("4000", money.IsNegative ? -money : Money.Zero, money.IsNegative ? Money.Zero : money),
        ]);
    }
}
