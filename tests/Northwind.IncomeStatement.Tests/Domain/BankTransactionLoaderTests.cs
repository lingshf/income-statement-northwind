using Microsoft.AspNetCore.Mvc.Testing;
using Northwind.IncomeStatement.Domain;

namespace Northwind.IncomeStatement.Tests.Domain;

public class BankTransactionLoaderTests
{
    private const string Valid = """
        {
          "bank_entries": [
            {"id": "BT-1", "date": "2026-02-02", "amount": "-100.10", "description": "Payment"},
            {"id": "BT-2", "date": "2026-02-03", "amount": "25.00", "description": "Deposit"}
          ]
        }
        """;

    [Fact]
    public void Supplied_file_loads_all_entries()
    {
        var bank = BankTransactionLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "bank_transactions.json"));

        Assert.Equal(7, bank.Entries.Count);
        Assert.Equal(new BankEntry("BT-01", new DateOnly(2026, 1, 2), Money.FromCents(-900000), "ACH DEBIT LANDMARK PROPERTIES"), bank.Entries[0]);
    }

    [Fact]
    public void Valid_file_maps_exact_signed_amounts_in_file_order()
    {
        var bank = BankTransactionLoader.Parse(Valid);

        Assert.Equal(
            new[]
            {
                new BankEntry("BT-1", new DateOnly(2026, 2, 2), Money.FromCents(-10010), "Payment"),
                new BankEntry("BT-2", new DateOnly(2026, 2, 3), Money.FromCents(2500), "Deposit"),
            },
            bank.Entries);
    }

    [Fact]
    public void Empty_array_is_allowed()
    {
        Assert.Empty(BankTransactionLoader.Parse("""{"bank_entries": []}""").Entries);
    }

    [Theory]
    [InlineData("\"date\": \"2026-02-02\"", "\"date\": \"2026-02-30\"", "BT-1 has invalid date '2026-02-30'")]
    [InlineData("\"date\": \"2026-02-02\"", "\"date\": \"2026-2-02\"", "BT-1 has invalid date '2026-2-02'")]
    [InlineData("\"amount\": \"-100.10\"", "\"amount\": \"-100.1\"", "BT-1 amount '-100.1' is not a valid amount")]
    [InlineData("\"id\": \"BT-2\"", "\"id\": \"BT-1\"", "BT-1 is defined more than once")]
    [InlineData("\"id\": \"BT-1\", ", "", "Bank entry #1 is missing 'id'")]
    [InlineData("\"bank_entries\"", "\"bank-entries\"", "must contain a 'bank_entries' array")]
    [InlineData("\"amount\": \"-100.10\"", "\"amount\": -100.10", "not valid JSON")]
    public void Invalid_file_fails_fast_with_a_clear_error(string find, string replace, string expectedError)
    {
        Assert.Contains(find, Valid);

        var ex = Assert.Throws<BankTransactionsValidationException>(() => BankTransactionLoader.Parse(Valid.Replace(find, replace)));

        Assert.Contains(ex.Errors, e => e.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_file_is_reported()
    {
        var ex = Assert.Throws<BankTransactionsValidationException>(() => BankTransactionLoader.LoadFile("does-not-exist.json"));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void App_refuses_to_start_with_an_invalid_bank_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Valid.Replace("\"amount\": \"25.00\"", "\"amount\": \"25\""));
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b => b.UseSetting("BankTransactions:Path", path));

            var ex = Assert.ThrowsAny<Exception>(() => factory.Services);

            Assert.IsType<BankTransactionsValidationException>(ex.GetBaseException());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
