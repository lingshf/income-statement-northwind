using Microsoft.AspNetCore.Mvc.RazorPages;
using Northwind.IncomeStatement.Domain;

namespace Northwind.IncomeStatement.Pages;

public class ReconciliationModel(IncomeStatementService service, Ledger ledger, BankTransactions bankTransactions) : PageModel
{
    public string Company => ledger.Company;

    public string Currency => ledger.Currency;

    public BankReconciliationResult Result { get; } = service.MatchBankEntryWithCashEntry(bankTransactions.Entries);
}
