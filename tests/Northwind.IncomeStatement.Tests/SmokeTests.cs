using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Northwind.IncomeStatement.Tests;

public class SmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Home_page_renders()
    {
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("Income statement", html);
    }

    [Fact]
    public void Ledger_is_copied_to_output()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "ledger.json")));
    }
}
