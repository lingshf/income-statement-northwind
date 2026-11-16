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

    [Fact]
    public async Task Q1_2026_returns_the_statement_shape_with_exact_string_amounts()
    {
        // Every figure is asserted by IncomeStatementServiceTests; this covers only the wire contract.
        var (status, body, _) = await Get("?start=2026-01-01&end=2026-03-31");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Northwind Coffee Roasters", body.GetProperty("company").GetString());
        Assert.Equal("USD", body.GetProperty("currency").GetString());
        Assert.Equal("2026-01-01", body.GetProperty("period").GetProperty("start").GetString());
        Assert.Equal("2026-03-31", body.GetProperty("period").GetProperty("end").GetString());

        foreach (var (key, title) in new[]
                 {
                     ("revenue", "Revenue"), ("costOfGoodsSold", "Cost of goods sold"),
                     ("operatingExpenses", "Operating expenses"), ("otherIncome", "Other income"),
                 })
        {
            var section = body.GetProperty(key);
            Assert.Equal(title, section.GetProperty("title").GetString());
            Assert.Equal(JsonValueKind.String, section.GetProperty("subtotal").ValueKind);
        }

        foreach (var total in new[] { "netRevenue", "grossProfit", "operatingIncome", "netIncome" })
        {
            Assert.Equal(JsonValueKind.String, body.GetProperty(total).ValueKind);
        }

        var contra = body.GetProperty("revenue").GetProperty("lines")[2];
        Assert.Equal("4900", contra.GetProperty("account").GetString());
        Assert.Equal("Sales Returns & Discounts", contra.GetProperty("name").GetString());
        Assert.Equal("-800.25", contra.GetProperty("amount").GetString());

        Assert.Equal("-44480.14", body.GetProperty("netIncome").GetString());
        Assert.True(body.GetProperty("hasActivity").GetBoolean());
    }

    [Fact]
    public async Task Empty_period_returns_a_zero_statement_not_an_error()
    {
        var (status, body, _) = await Get("?start=2030-01-01&end=2030-01-31");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("hasActivity").GetBoolean());
        Assert.Equal("0.00", body.GetProperty("netIncome").GetString());
    }

    [Theory]
    [InlineData("", "start")]
    [InlineData("?start=2026-01-01", "end")]
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
