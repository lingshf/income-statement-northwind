using System.Globalization;
using System.Text.Json;
using Northwind.IncomeStatement.Domain;

namespace Northwind.IncomeStatement.Tests.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData("0.00", 0)]
    [InlineData("0.01", 1)]
    [InlineData("1234.56", 123456)]
    [InlineData("35650.75", 3565075)]
    [InlineData("-800.25", -80025)]
    [InlineData("-0.00", 0)]
    [InlineData("007.10", 710)]
    public void Parse_accepts_exactly_two_decimals(string text, long expectedCents)
    {
        Assert.Equal(expectedCents, Money.Parse(text).Cents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.000")]
    [InlineData(".50")]
    [InlineData("-.50")]
    [InlineData("1.")]
    [InlineData("+1.00")]
    [InlineData(" 1.00")]
    [InlineData("1.00 ")]
    [InlineData("1,000.00")]
    [InlineData("1000,00")]
    [InlineData("1e3")]
    [InlineData("1.0e")]
    [InlineData("--1.00")]
    [InlineData("1-.00")]
    [InlineData("abc")]
    [InlineData("１.００")] // full-width digits
    [InlineData("99999999999999999999.00")] // overflows long cents
    public void Parse_rejects_anything_that_is_not_a_two_decimal_amount(string? text)
    {
        Assert.False(Money.TryParse(text, out _));
        Assert.Throws<FormatException>(() => Money.Parse(text));
    }

    [Fact]
    public void Addition_is_exact_to_the_cent()
    {
        // 0.1 + 0.2 != 0.3 in binary floating point; Money must be exact.
        Assert.Equal(Money.Parse("0.30"), Money.Parse("0.10") + Money.Parse("0.20"));
        Assert.Equal(Money.Parse("37850.50"), Money.Parse("35650.75") + Money.Parse("3000.00") + Money.Parse("-800.25"));
    }

    [Fact]
    public void Subtraction_and_negation_handle_signs()
    {
        Assert.Equal(Money.Parse("-44522.32"), Money.Parse("23577.75") - Money.Parse("68100.07"));
        Assert.Equal(Money.Parse("800.25"), -Money.Parse("-800.25"));
        Assert.Equal(Money.Parse("-0.01"), Money.Zero - Money.Parse("0.01"));
    }

    [Fact]
    public void Sum_adds_every_value_and_is_zero_when_empty()
    {
        Assert.Equal(Money.Zero, Money.Sum([]));
        Assert.Equal(
            Money.Parse("1099.97"),
            Money.Sum([Money.Parse("299.99"), Money.Parse("399.99"), Money.Parse("399.99")]));
    }

    [Fact]
    public void Arithmetic_overflow_throws_instead_of_wrapping()
    {
        var max = Money.FromCents(long.MaxValue);
        Assert.Throws<OverflowException>(() => max + Money.Parse("0.01"));
        Assert.Throws<OverflowException>(() => -Money.FromCents(long.MinValue));
    }

    [Fact]
    public void Equality_and_comparison_are_by_value()
    {
        Assert.Equal(Money.Zero, Money.Parse("-0.00"));
        Assert.True(Money.Parse("1.00") == Money.Parse("1.00"));
        Assert.True(Money.Parse("1.00") != Money.Parse("1.01"));
        Assert.True(Money.Parse("-0.01") < Money.Zero);
        Assert.True(Money.Parse("10.00") > Money.Parse("9.99"));
        Assert.True(Money.Parse("1.00") >= Money.Parse("1.00"));
        Assert.Equal(Money.Parse("5.00").GetHashCode(), Money.Parse("5.00").GetHashCode());
        Assert.Equal(-1, Money.Parse("1.00").CompareTo(Money.Parse("2.00")));
        Assert.False(Money.Parse("1.00").Equals((object)1.00m));
    }

    [Theory]
    [InlineData("0.00", "0.00")]
    [InlineData("-0.00", "0.00")]
    [InlineData("0.05", "0.05")]
    [InlineData("-0.05", "-0.05")]
    [InlineData("1234567.80", "1234567.80")]
    [InlineData("-44480.14", "-44480.14")]
    public void ToString_is_invariant_two_decimals_without_grouping(string input, string expected)
    {
        Assert.Equal(expected, Money.Parse(input).ToString());
    }

    [Theory]
    [InlineData("0.00", "0.00")]
    [InlineData("42.18", "42.18")]
    [InlineData("-800.25", "(800.25)")]
    [InlineData("1234567.80", "1,234,567.80")]
    [InlineData("-44480.14", "(44,480.14)")]
    [InlineData("-0.01", "(0.01)")]
    public void ToAccountingString_groups_thousands_and_uses_parentheses_for_negatives(string input, string expected)
    {
        Assert.Equal(expected, Money.Parse(input).ToAccountingString());
    }

    [Fact]
    public void Formatting_and_parsing_ignore_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var money = Money.Parse("1234.56");
            Assert.Equal("1234.56", money.ToString());
            Assert.Equal("1,234.56", money.ToAccountingString());
            Assert.False(Money.TryParse("1234,56", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Json_round_trips_as_an_exact_string()
    {
        Assert.Equal("\"-44480.14\"", JsonSerializer.Serialize(Money.Parse("-44480.14")));
        Assert.Equal(Money.Parse("9.99"), JsonSerializer.Deserialize<Money>("\"9.99\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("9.99"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("\"9.9\""));
    }
}
