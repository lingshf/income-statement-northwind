using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Northwind.IncomeStatement.Tests.Pages;

// Figures are asserted in full at service level; these tests cover only what the page adds:
// defaults, input binding, accounting formatting, row order, and the empty/error states.
public class IndexPageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<string> GetPage(string query = "")
    {
        var response = await factory.CreateClient().GetAsync("/" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Regex.Replace(await response.Content.ReadAsStringAsync(), @"\s+", " ");
    }

    private static string Row(string html, string label) =>
        Regex.Match(html, $@"<tr[^>]*> <th[^>]*>{Regex.Escape(label)}</th>.*?</tr>").Value;

    [Fact]
    public async Task Default_page_shows_Q1_2026_with_labeled_date_inputs()
    {
        var html = await GetPage();

        Assert.Contains("<label for=\"start\">Start date</label> <input id=\"start\" name=\"start\" type=\"date\" value=\"2026-01-01\" />", html);
        Assert.Contains("<label for=\"end\">End date</label> <input id=\"end\" name=\"end\" type=\"date\" value=\"2026-03-31\" />", html);
        Assert.Contains("Northwind Coffee Roasters", html);
        Assert.Contains("Currency: <span class=\"currency\">USD</span>", html);
        Assert.Contains("(44,480.14)", Row(html, "Net income"));
    }

    [Fact]
    public async Task Statement_rows_render_in_order_with_accounting_format()
    {
        var html = await GetPage("?start=2026-01-01&end=2026-03-31");

        Assert.Contains("<td class=\"account\">4000</td> <td>Product Revenue</td> <td class=\"amount\">35,650.75</td>", html);
        Assert.Contains("<td class=\"account\">4900</td> <td>Sales Returns &amp; Discounts</td> <td class=\"amount\">(800.25)</td>", html);
        Assert.Contains("(44,522.32)", Row(html, "Operating income"));

        string[] order =
        [
            "Revenue", "Net revenue", "Cost of goods sold", "Total cost of goods sold", "Gross profit",
            "Operating expenses", "Total operating expenses", "Operating income",
            "Other income", "Total other income", "Net income",
        ];
        var positions = order.Select(label => html.IndexOf($">{label}</th>", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public async Task Page_renders_the_requested_range()
    {
        var html = await GetPage("?start=2026-03-31&end=2026-03-31");

        Assert.Contains("<time datetime=\"2026-03-31\">2026-03-31</time> to <time datetime=\"2026-03-31\">2026-03-31</time>", html);
        Assert.Contains("(17,457.82)", Row(html, "Net income"));
        Assert.DoesNotContain("No posted activity", html);
    }

    [Fact]
    public async Task Empty_period_shows_a_message_and_a_zero_statement()
    {
        var html = await GetPage("?start=2030-01-01&end=2030-01-31");

        Assert.Contains("No posted activity in this period. All amounts are zero.", html);
        Assert.Contains("0.00", Row(html, "Net income"));
    }

    [Theory]
    [InlineData("?start=2026-03-31&end=2026-01-01", "Start date 2026-03-31 must be on or before end date 2026-01-01.")]
    [InlineData("?start=2026-01-01&end=", "The &#x27;end&#x27; date is required")]
    public async Task Invalid_period_shows_an_error_and_no_statement(string query, string expectedMessage)
    {
        var html = await GetPage(query);

        Assert.Contains("role=\"alert\"", html);
        Assert.Contains(expectedMessage, html);
        Assert.DoesNotContain("<table", html);
    }
}
