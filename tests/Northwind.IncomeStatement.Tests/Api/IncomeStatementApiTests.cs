using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Northwind.IncomeStatement.Tests.Api;

/// <summary>Asserts on the raw JSON so the wire contract (money as exact strings) is tested, not the C# DTOs.</summary>
public class IncomeStatementApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<(HttpStatusCode Status, JsonElement Body, string? ContentType)> Get(string query)
    {
        var response = await factory.CreateClient().GetAsync("/income-statement" + query);
        var json = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(json).RootElement, response.Content.Headers.ContentType?.MediaType);
    }

    private static Dictionary<string, string> Lines(JsonElement section) =>
        section.GetProperty("lines").EnumerateArray()
            .ToDictionary(l => l.GetProperty("account").GetString()!, l => l.GetProperty("amount").GetString()!);

    [Fact]
    public async Task Q1_2026_returns_the_reconciled_statement_with_exact_string_amounts()
    {
        var (status, body, _) = await Get("?start=2026-01-01&end=2026-03-31");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Northwind Coffee Roasters", body.GetProperty("company").GetString());
        Assert.Equal("USD", body.GetProperty("currency").GetString());
        Assert.Equal("2026-01-01", body.GetProperty("period").GetProperty("start").GetString());
        Assert.Equal("2026-03-31", body.GetProperty("period").GetProperty("end").GetString());

        var revenue = body.GetProperty("revenue");
        Assert.Equal("Revenue", revenue.GetProperty("title").GetString());
        Assert.Equal(
            new Dictionary<string, string> { ["4000"] = "35650.75", ["4100"] = "3000.00", ["4900"] = "-800.25" },
            Lines(revenue));
        Assert.Equal("37850.50", revenue.GetProperty("subtotal").GetString());
        Assert.Equal("37850.50", body.GetProperty("netRevenue").GetString());

        Assert.Equal(new Dictionary<string, string> { ["5000"] = "14272.75" }, Lines(body.GetProperty("costOfGoodsSold")));
        Assert.Equal("14272.75", body.GetProperty("costOfGoodsSold").GetProperty("subtotal").GetString());
        Assert.Equal("23577.75", body.GetProperty("grossProfit").GetString());

        Assert.Equal(
            new Dictionary<string, string> { ["6000"] = "55500.00", ["6100"] = "9000.00", ["6200"] = "1099.97", ["6300"] = "2500.10" },
            Lines(body.GetProperty("operatingExpenses")));
        Assert.Equal("68100.07", body.GetProperty("operatingExpenses").GetProperty("subtotal").GetString());
        Assert.Equal("-44522.32", body.GetProperty("operatingIncome").GetString());

        Assert.Equal(new Dictionary<string, string> { ["7000"] = "42.18" }, Lines(body.GetProperty("otherIncome")));
        Assert.Equal("42.18", body.GetProperty("otherIncome").GetProperty("subtotal").GetString());

        Assert.Equal("-44480.14", body.GetProperty("netIncome").GetString());
        Assert.True(body.GetProperty("hasActivity").GetBoolean());
    }

    [Fact]
    public async Task Account_lines_carry_number_and_name_in_account_order()
    {
        var (_, body, _) = await Get("?start=2026-01-01&end=2026-03-31");

        var lines = body.GetProperty("operatingExpenses").GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(["6000", "6100", "6200", "6300"], lines.Select(l => l.GetProperty("account").GetString()));
        Assert.Equal("Marketing (legacy)", lines[3].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Empty_period_returns_a_zero_statement_not_an_error()
    {
        var (status, body, _) = await Get("?start=2030-01-01&end=2030-01-31");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("hasActivity").GetBoolean());
        Assert.Equal("0.00", body.GetProperty("netIncome").GetString());
        Assert.All(Lines(body.GetProperty("revenue")).Values, v => Assert.Equal("0.00", v));
    }

    [Fact]
    public async Task Single_day_range_is_inclusive()
    {
        var (_, body, _) = await Get("?start=2026-03-31&end=2026-03-31");

        Assert.Equal("-17457.82", body.GetProperty("netIncome").GetString());
    }

    [Theory]
    [InlineData("", "start")]
    [InlineData("?start=2026-01-01", "end")]
    [InlineData("?end=2026-03-31", "start")]
    [InlineData("?start=&end=2026-03-31", "start")]
    [InlineData("?start=2026-1-1&end=2026-03-31", "start")]
    [InlineData("?start=01/01/2026&end=2026-03-31", "start")]
    [InlineData("?start=2026-01-01T00:00:00&end=2026-03-31", "start")]
    [InlineData("?start=%202026-01-01&end=2026-03-31", "start")]
    [InlineData("?start=2026-01-01&end=2026-02-30", "end")]
    [InlineData("?start=2025-02-29&end=2026-03-31", "start")]
    [InlineData("?start=2026-13-01&end=2026-03-31", "start")]
    [InlineData("?start=2026-03-31&end=2026-01-01", "start")]
    public async Task Invalid_period_returns_400_problem_details(string query, string invalidField)
    {
        var (status, body, contentType) = await Get(query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("application/problem+json", contentType);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("Invalid reporting period", body.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.True(body.GetProperty("errors").TryGetProperty(invalidField, out _));
    }

    [Fact]
    public async Task Start_after_end_explains_the_problem()
    {
        var (_, body, _) = await Get("?start=2026-03-31&end=2026-01-01");

        Assert.Equal(
            "Start date 2026-03-31 must be on or before end date 2026-01-01.",
            body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Leap_day_in_a_leap_year_is_accepted()
    {
        var (status, _, _) = await Get("?start=2024-02-29&end=2024-02-29");

        Assert.Equal(HttpStatusCode.OK, status);
    }
}
