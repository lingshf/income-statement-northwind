namespace Northwind.IncomeStatement.Domain;

/// <summary>How an account's period activity is turned into a signed statement amount.</summary>
public enum SignRule
{
    /// <summary>Revenue, contra revenue, other income: credits − debits.</summary>
    CreditsMinusDebits,

    /// <summary>Expenses (COGS, operating): debits − credits.</summary>
    DebitsMinusCredits,
}

public sealed record SectionDefinition(string Title, IReadOnlyList<AccountSubtype> Subtypes, SignRule Sign);

/// <summary>
/// The income statement layout as data: which account subtypes feed each section and with which sign.
/// Subtypes not listed here (balance_sheet) never appear on the statement.
/// </summary>
public static class StatementLayout
{
    public static readonly SectionDefinition Revenue =
        new("Revenue", [AccountSubtype.OperatingRevenue, AccountSubtype.ContraRevenue], SignRule.CreditsMinusDebits);

    public static readonly SectionDefinition CostOfGoodsSold =
        new("Cost of goods sold", [AccountSubtype.Cogs], SignRule.DebitsMinusCredits);

    public static readonly SectionDefinition OperatingExpenses =
        new("Operating expenses", [AccountSubtype.OperatingExpense], SignRule.DebitsMinusCredits);

    public static readonly SectionDefinition OtherIncome =
        new("Other income", [AccountSubtype.OtherIncome], SignRule.CreditsMinusDebits);

    public static readonly IReadOnlyList<SectionDefinition> Sections = [Revenue, CostOfGoodsSold, OperatingExpenses, OtherIncome];

    private static readonly Dictionary<AccountSubtype, SectionDefinition> BySubtype =
        Sections.SelectMany(s => s.Subtypes, (section, subtype) => (subtype, section))
            .ToDictionary(x => x.subtype, x => x.section);

    public static SectionDefinition? SectionFor(AccountSubtype subtype) => BySubtype.GetValueOrDefault(subtype);
}
