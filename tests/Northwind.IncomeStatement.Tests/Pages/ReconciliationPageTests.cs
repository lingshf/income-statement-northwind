using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Northwind.IncomeStatement.Domain;

namespace Northwind.IncomeStatement.Tests.Pages;

public class ReconciliationPageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Page_shows_company_currency_rules_and_computed_counts()
    {
        var html = await GetPage(factory);

        Assert.Contains("<h1 id=\"reconciliation-heading\">Bank reconciliation</h1>", html);
        Assert.Contains("Northwind Coffee Roasters", html);
        Assert.Contains("Cash account 1000 &middot; All dates &middot; USD", html);
        Assert.Contains("up to 3 calendar days before or after the journal date, inclusive", html);
        Assert.Contains("negative means earlier, positive means later", html);
        Assert.Contains("<dt>Matched pairs</dt><dd>5</dd>", html);
        Assert.Contains("<dt>Unmatched bank entries</dt><dd>2</dd>", html);
        Assert.Contains("<dt>Unmatched journal entries</dt><dd>1</dd>", html);
    }

    [Fact]
    public async Task Tables_show_each_record_once_with_dates_and_accounting_amounts()
    {
        var html = await GetPage(factory);
        var matched = Table(html, "matched-entries");
        var banks = Table(html, "unmatched-bank-entries");
        var journals = Table(html, "unmatched-journal-entries");

        Assert.Equal(5, DataRowCount(matched));
        Assert.Equal(2, DataRowCount(banks));
        Assert.Equal(1, DataRowCount(journals));
        Assert.Contains("BT-01", matched);
        Assert.Contains("JE-007", matched);
        Assert.Contains("ACH DEBIT LANDMARK PROPERTIES", matched);
        Assert.Contains("<time datetime=\"2026-01-02\">2026-01-02</time>", matched);
        Assert.Contains("<time datetime=\"2026-01-01\">2026-01-01</time>", matched);
        Assert.Contains("<td>1</td> <td class=\"amount\">(9,000.00)</td>", matched);
        Assert.Contains("BT-06", matched);
        Assert.DoesNotContain("BT-07", matched);
        Assert.Contains("BT-07", banks);
        Assert.DoesNotContain("BT-06", banks);
        Assert.Contains("MONTHLY SERVICE FEE", banks);
        Assert.Contains("(35.00)", banks);
        Assert.Contains("12,450.75", matched);
        Assert.Contains("JE-013", matched);
        Assert.Contains("<td>-1</td> <td class=\"amount\">(18,500.00)</td>", matched);
        Assert.Contains("<td>3</td> <td class=\"amount\">12,450.75</td>", matched);
        Assert.Contains("JE-023", journals);
        Assert.Contains("42.18", journals);
        Assert.DoesNotContain("JE-019", html);
        Assert.DoesNotContain("JE-025", html);
    }

    [Fact]
    public async Task Reports_link_to_each_other()
    {
        var reconciliation = await GetPage(factory);
        using var client = factory.CreateClient();
        var statement = await client.GetStringAsync("/");

        Assert.Contains("href=\"/\">Income statement</a>", reconciliation);
        Assert.Contains("href=\"/reconciliation\">Bank reconciliation</a>", statement);
    }

    [Fact]
    public async Task Empty_ledger_shows_explicit_empty_states_without_tables()
    {
        using var emptyFactory = WithData(new Ledger("Empty Co", "USD", [], []), []);
        var html = await GetPage(emptyFactory);

        Assert.Contains("<dt>Matched pairs</dt><dd>0</dd>", html);
        Assert.Contains("<dt>Unmatched bank entries</dt><dd>0</dd>", html);
        Assert.Contains("<dt>Unmatched journal entries</dt><dd>0</dd>", html);
        Assert.Contains("No matching bank and journal entries were found.", html);
        Assert.Contains("No unmatched bank entries.", html);
        Assert.Contains("No unmatched journal entries.", html);
        Assert.DoesNotContain("<table", html);
    }

    [Fact]
    public async Task Unmatched_cash_amount_is_netted_and_free_text_is_html_encoded()
    {
        var date = new DateOnly(2026, 1, 1);
        var journal = new JournalEntry("JE-test", date, EntryStatus.Posted, "<script>memo</script>",
        [
            new JournalLine("1000", Money.Parse("100.10"), Money.Zero),
            new JournalLine("1000", Money.Zero, Money.Parse("20.05")),
            new JournalLine("4000", Money.Zero, Money.Parse("80.05")),
        ]);
        var bank = new BankEntry("BT-test", date, Money.Parse("-35.00"), "<script>description</script>");
        using var customFactory = WithData(new Ledger("<Test Co>", "USD", [], [journal]), [bank]);

        var html = await GetPage(customFactory);

        Assert.Contains("80.05", Table(html, "unmatched-journal-entries"));
        Assert.Contains("(35.00)", Table(html, "unmatched-bank-entries"));
        Assert.Contains("&lt;Test Co&gt;", html);
        Assert.Contains("&lt;script&gt;memo&lt;/script&gt;", html);
        Assert.Contains("&lt;script&gt;description&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(3)]
    public async Task Fully_matched_ledger_shows_no_unmatched_records_at_date_boundaries(int offset)
    {
        var date = new DateOnly(2026, 1, 1);
        var journal = new JournalEntry("JE-test", date, EntryStatus.Posted, "Sale",
        [
            new JournalLine("1000", Money.Parse("100.10"), Money.Zero),
            new JournalLine("4000", Money.Zero, Money.Parse("100.10")),
        ]);
        var bank = new BankEntry("BT-test", date.AddDays(offset), Money.Parse("100.10"), "Deposit");
        using var customFactory = WithData(new Ledger("Test Co", "USD", [], [journal]), [bank]);

        var html = await GetPage(customFactory);

        Assert.Contains("<dt>Matched pairs</dt><dd>1</dd>", html);
        Assert.Contains($"<td>{offset}</td> <td class=\"amount\">100.10</td>", Table(html, "matched-entries"));
        Assert.Contains("No unmatched bank entries.", html);
        Assert.Contains("No unmatched journal entries.", html);
        Assert.DoesNotContain("No matching bank and journal entries were found.", html);
    }

    private WebApplicationFactory<Program> WithData(Ledger ledger, IEnumerable<BankEntry> bankEntries) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<Ledger>();
            services.AddSingleton(ledger);
            services.RemoveAll<BankTransactions>();
            services.AddSingleton(new BankTransactions(bankEntries));
        }));

    private static async Task<string> GetPage(WebApplicationFactory<Program> app)
    {
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/reconciliation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Regex.Replace(await response.Content.ReadAsStringAsync(), @"\s+", " ");
    }

    private static string Table(string html, string id)
    {
        var table = Regex.Match(html, $"""<table id="{Regex.Escape(id)}">.*?</table>""").Value;
        Assert.NotEmpty(table);
        return table;
    }

    private static int DataRowCount(string table) =>
        Regex.Matches(Regex.Match(table, "<tbody>.*?</tbody>").Value, "<tr>").Count;
}
