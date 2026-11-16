using Microsoft.AspNetCore.Mvc.Testing;
using Northwind.IncomeStatement.Domain;

namespace Northwind.IncomeStatement.Tests.Domain;

public class LedgerLoaderTests
{
    private const string ValidLedger = """
        {
          "company": "Test Co",
          "currency": "USD",
          "accounts": [
            {"number": "1000", "name": "Cash", "type": "asset", "subtype": "balance_sheet", "is_active": true},
            {"number": "4000", "name": "Sales", "type": "revenue", "subtype": "operating_revenue", "is_active": true},
            {"number": "6000", "name": "Rent", "type": "expense", "subtype": "operating_expense", "is_active": false}
          ],
          "journal_entries": [
            {"id": "JE-1", "date": "2026-01-31", "status": "posted", "memo": "sale", "lines": [
              {"account": "1000", "debit": "100.10", "credit": "0.00"},
              {"account": "4000", "debit": "0.00", "credit": "100.10"}
            ]},
            {"id": "JE-2", "date": "2026-02-01", "status": "draft", "memo": "rent", "lines": [
              {"account": "6000", "debit": "40.00", "credit": "0.00"},
              {"account": "6000", "debit": "10.05", "credit": "0.00"},
              {"account": "1000", "debit": "0.00", "credit": "50.05"}
            ]}
          ]
        }
        """;

    [Fact]
    public void Supplied_ledger_loads_with_expected_counts()
    {
        var ledger = LedgerLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "ledger.json"));

        Assert.Equal("Northwind Coffee Roasters", ledger.Company);
        Assert.Equal("USD", ledger.Currency);
        Assert.Equal(15, ledger.Accounts.Count);
        Assert.Equal(25, ledger.Entries.Count);
        Assert.Equal(51, ledger.Entries.Sum(e => e.Lines.Count));
        Assert.Equal(22, ledger.Entries.Count(e => e.Status == EntryStatus.Posted));
        Assert.Equal(1, ledger.Entries.Count(e => e.Status == EntryStatus.Draft));
        Assert.Equal(2, ledger.Entries.Count(e => e.Status == EntryStatus.Void));
    }

    [Fact]
    public void Valid_ledger_maps_all_fields_exactly()
    {
        var ledger = LedgerLoader.Parse(ValidLedger);

        var rent = ledger.Accounts["6000"];
        Assert.Equal(new Account("6000", "Rent", AccountType.Expense, AccountSubtype.OperatingExpense, false), rent);

        var draft = ledger.Entries[1];
        Assert.Equal("JE-2", draft.Id);
        Assert.Equal(new DateOnly(2026, 2, 1), draft.Date);
        Assert.Equal(EntryStatus.Draft, draft.Status);
        Assert.Equal(3, draft.Lines.Count);
        Assert.Equal(new JournalLine("6000", Money.Parse("10.05"), Money.Zero), draft.Lines[1]);
    }

    [Fact]
    public void App_refuses_to_start_with_an_invalid_ledger()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, ValidLedger.Replace("\"credit\": \"100.10\"", "\"credit\": \"100.11\""));
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b => b.UseSetting("Ledger:Path", path));

            var ex = Assert.ThrowsAny<Exception>(() => factory.Services);

            Assert.IsType<LedgerValidationException>(ex.GetBaseException());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("\"debit\": \"0.00\", \"credit\": \"100.10\"", "\"debit\": \"0.00\", \"credit\": \"100.11\"", "JE-1 is unbalanced: debits 100.10 != credits 100.11")]
    [InlineData("{\"account\": \"4000\", \"debit\": \"0.00\"", "{\"account\": \"4999\", \"debit\": \"0.00\"", "JE-1 line 2 references unknown account '4999'")]
    [InlineData("\"debit\": \"100.10\"", "\"debit\": \"100.1\"", "JE-1 line 1 debit '100.1' is not a valid amount")]
    [InlineData("\"debit\": \"100.10\", \"credit\": \"0.00\"", "\"debit\": \"-100.10\", \"credit\": \"0.00\"", "JE-1 line 1 debit '-100.10' must not be negative")]
    [InlineData("\"debit\": \"100.10\", \"credit\": \"0.00\"", "\"debit\": \"100.10\", \"credit\": \"100.10\"", "JE-1 line 1 must have exactly one of debit/credit non-zero")]
    [InlineData("\"debit\": \"100.10\", \"credit\": \"0.00\"", "\"debit\": \"0.00\", \"credit\": \"0.00\"", "JE-1 line 1 must have exactly one of debit/credit non-zero")]
    [InlineData("\"date\": \"2026-01-31\"", "\"date\": \"2026-02-30\"", "JE-1 has invalid date '2026-02-30'")]
    [InlineData("\"date\": \"2026-01-31\"", "\"date\": \"2026-1-31\"", "JE-1 has invalid date '2026-1-31'")]
    [InlineData("\"date\": \"2026-01-31\"", "\"date\": \"2026-01-31T00:00:00\"", "JE-1 has invalid date")]
    [InlineData("\"status\": \"posted\"", "\"status\": \"Posted\"", "JE-1 has unknown status 'Posted'")]
    [InlineData("\"type\": \"revenue\"", "\"type\": \"income\"", "Account 4000 has unknown type 'income'")]
    [InlineData("\"subtype\": \"operating_revenue\"", "\"subtype\": \"sales\"", "Account 4000 has unknown subtype 'sales'")]
    [InlineData("\"subtype\": \"operating_revenue\"", "\"subtype\": \"cogs\"", "Account 4000 has subtype 'cogs', which is not valid for type 'revenue'")]
    [InlineData("{\"number\": \"6000\"", "{\"number\": \"600\"", "Account 600 has length 3; all account numbers must have length 4")]
    [InlineData("{\"number\": \"6000\"", "{\"number\": \"4000\"", "Account 4000 is defined more than once")]
    [InlineData("\"id\": \"JE-2\"", "\"id\": \"JE-1\"", "JE-1 is defined more than once")]
    [InlineData("{\"account\": \"1000\", \"debit\": \"100.10\", \"credit\": \"0.00\"},", "", "JE-1 must have at least 2 lines")]
    public void Invalid_ledger_fails_fast_with_a_clear_error(string find, string replace, string expectedError)
    {
        Assert.Contains(find, ValidLedger);
        var json = ValidLedger.Replace(find, replace);

        var ex = Assert.Throws<LedgerValidationException>(() => LedgerLoader.Parse(json));

        Assert.Contains(ex.Errors, e => e.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_json_is_rejected()
    {
        var ex = Assert.Throws<LedgerValidationException>(() => LedgerLoader.Parse("{ not json"));

        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void Numeric_amounts_are_rejected_because_amounts_must_be_strings()
    {
        var json = ValidLedger.Replace("\"debit\": \"100.10\"", "\"debit\": 100.10");

        Assert.Throws<LedgerValidationException>(() => LedgerLoader.Parse(json));
    }

    [Fact]
    public void Missing_file_is_reported()
    {
        var ex = Assert.Throws<LedgerValidationException>(() => LedgerLoader.LoadFile("does-not-exist.json"));

        Assert.Contains("not found", ex.Message);
    }
}
