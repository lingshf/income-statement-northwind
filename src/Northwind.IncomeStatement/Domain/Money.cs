using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Northwind.IncomeStatement.Domain;

/// <summary>
/// An exact monetary amount with exactly two decimal places, stored as a whole number of cents.
/// All financial arithmetic goes through this type; arithmetic is checked and throws on overflow.
/// </summary>
[JsonConverter(typeof(MoneyJsonConverter))]
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    private readonly long _cents;

    private Money(long cents) => _cents = cents;

    public static Money Zero => default;

    public static Money FromCents(long cents) => new(cents);

    public long Cents => _cents;

    public decimal Amount => _cents / 100m;

    public bool IsZero => _cents == 0;

    public bool IsNegative => _cents < 0;

    /// <summary>
    /// Parses an invariant decimal string with exactly two decimals, e.g. "1234.56" or "-0.05".
    /// Rejects signs other than a leading '-', thousands separators, exponents, whitespace and any other scale.
    /// </summary>
    public static Money Parse(string? text) =>
        TryParse(text, out var money)
            ? money
            : throw new FormatException($"'{text}' is not a valid money amount; expected digits with exactly 2 decimals, e.g. \"1234.56\".");

    public static bool TryParse([NotNullWhen(true)] string? text, out Money money)
    {
        money = Zero;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var negative = text[0] == '-';
        var digitsStart = negative ? 1 : 0;
        var dot = text.Length - 3;
        if (dot <= digitsStart || text[dot] != '.')
        {
            return false;
        }

        long cents = 0;
        try
        {
            for (var i = digitsStart; i < text.Length; i++)
            {
                if (i == dot)
                {
                    continue;
                }

                var c = text[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }

                cents = checked(cents * 10 + (c - '0'));
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        money = new Money(negative ? -cents : cents);
        return true;
    }

    public static Money operator +(Money left, Money right) => new(checked(left._cents + right._cents));

    public static Money operator -(Money left, Money right) => new(checked(left._cents - right._cents));

    public static Money operator -(Money value) => new(checked(-value._cents));

    public static Money Sum(IEnumerable<Money> values)
    {
        var total = Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    public static bool operator ==(Money left, Money right) => left._cents == right._cents;

    public static bool operator !=(Money left, Money right) => left._cents != right._cents;

    public static bool operator <(Money left, Money right) => left._cents < right._cents;

    public static bool operator >(Money left, Money right) => left._cents > right._cents;

    public static bool operator <=(Money left, Money right) => left._cents <= right._cents;

    public static bool operator >=(Money left, Money right) => left._cents >= right._cents;

    public bool Equals(Money other) => _cents == other._cents;

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public override int GetHashCode() => _cents.GetHashCode();

    public int CompareTo(Money other) => _cents.CompareTo(other._cents);

    /// <summary>Invariant, exact 2-decimal string without grouping, e.g. "-44480.14".</summary>
    public override string ToString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Accounting display: thousands separators and negatives in parentheses, e.g. "(44,480.14)".</summary>
    public string ToAccountingString()
    {
        var magnitude = Math.Abs(Amount).ToString("#,##0.00", CultureInfo.InvariantCulture);
        return IsNegative ? $"({magnitude})" : magnitude;
    }
}

public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Money must be a JSON string with exactly 2 decimals.");
        }

        var text = reader.GetString();
        return Money.TryParse(text, out var money)
            ? money
            : throw new JsonException($"'{text}' is not a valid money amount.");
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
