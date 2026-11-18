# Northwind Income Statement

An income statement (P&L) for Northwind Coffee Roasters over any date range.
See [`spec.md`](spec.md) for the product brief.

## Requirements

- .NET SDK 9 (pinned via `global.json`, rolls forward to the latest 9.0 feature band);
  developed with SDK 9.0.321
- ASP.NET Core 9 (minimal API + Razor Pages), no other runtime dependencies
- Tests: xUnit 2.9.2, Microsoft.AspNetCore.Mvc.Testing 9.0
- CI: GitHub Actions on `ubuntu-latest` with .NET 9.0.x (`.github/workflows/ci.yml`)

## Commands

Run from the repository root:

```bash
dotnet restore
dotnet build
dotnet test                                          # all unit, API and page tests
dotnet run --project src/Northwind.IncomeStatement   # http://localhost:5208
```

- **UI:** open `http://localhost:5208/` — defaults to Q1 2026; pick any start/end date.
- **Bank reconciliation:** open `http://localhost:5208/reconciliation` or use the
  navigation link to see matched pairs and unmatched bank/journal records for all dates.
- **API:** `http://localhost:5208/income-statement?start=2026-01-01&end=2026-03-31` (see below).
- The ledger is validated at startup; an invalid `ledger.json` stops the app with a list of
  every problem. Override the path with `--Ledger:Path=<file>`.

See [`NOTES.md`](NOTES.md) for the Q1 2026 result, decisions, verification and extension points.

## Data

- `ledger.json` — chart of accounts and journal entries; the application's only accounting data source.
- `bank_transactions.json` — bank statement lines for reconciliation, kept separate from the
  ledger. Top level `bank-entries` array; each entry has a unique `id`, `date` (`YYYY-MM-DD`),
  `amount` (signed exact 2-decimal string: positive in, negative out), and `description`.
  Validated at startup like the ledger; override the path with `--BankTransactions:Path=<file>`.
- `ledger.csv` — the same journal, one row per line with account details joined in, for
  manual verification in a spreadsheet. Not read by the application.

## Bank reconciliation

The domain method `IncomeStatementService.MatchBankEntryWithCashEntry(bankEntries)` matches
the given bank entries against the ledger and returns
`Matches` (bank/journal pairs), `UnmatchedBankEntries`, and `UnmatchedJournalEntries`.
The read-only `/reconciliation` page uses this method, showing counts, record IDs,
dates, descriptions/memos, exact accounting-formatted amounts, and matched date offsets
(bank date minus journal date: negative means earlier, positive means later).
It includes explicit empty states. Reconciliation does not change the ledger or expose
a new API.

- Only posted journal entries touching Cash account `"1000"` are eligible.
- Each journal entry's cash amount is the sum of its cash debits minus cash credits.
  The signed amount must equal the bank amount exactly, including cents.
- The bank date must be within **3 calendar days before or after** the journal date
  (offset **-3 through +3, inclusive**); weekends and holidays are not skipped.
- Matching is one-to-one. Bank records and eligible journal entries are processed
  earliest-date first, retaining input order for ties. Each bank record consumes
  the earliest eligible remaining journal entry; duplicates are not reused.
- Unmatched lists use the same date/input ordering. Draft, void, and non-cash journal
  entries are excluded from reconciliation entirely.
- Bank data is read from `bank_transactions.json`; `ledger.json` contains no bank data.

## API

`GET /income-statement?start=YYYY-MM-DD&end=YYYY-MM-DD`

Both dates are required, must be real calendar dates in exactly `YYYY-MM-DD` form, and
`start <= end`. Both are **inclusive**. Only `posted` journal entries are counted.

### 200 OK

```json
{
  "company": "Northwind Coffee Roasters",
  "currency": "USD",
  "period": { "start": "2026-01-01", "end": "2026-03-31" },
  "revenue": {
    "title": "Revenue",
    "lines": [
      { "account": "4000", "name": "Product Revenue", "amount": "35650.75" },
      { "account": "4100", "name": "Subscription Revenue", "amount": "3000.00" },
      { "account": "4900", "name": "Sales Returns & Discounts", "amount": "-800.25" }
    ],
    "subtotal": "37850.50"
  },
  "netRevenue": "37850.50",
  "costOfGoodsSold": { "title": "Cost of goods sold", "lines": [ ... ], "subtotal": "14272.75" },
  "grossProfit": "23577.75",
  "operatingExpenses": { "title": "Operating expenses", "lines": [ ... ], "subtotal": "68100.07" },
  "operatingIncome": "-44522.32",
  "otherIncome": { "title": "Other income", "lines": [ ... ], "subtotal": "42.18" },
  "netIncome": "-44480.14",
  "hasActivity": true
}
```

- **Money** values are JSON strings with exactly 2 decimals and a leading `-` when negative
  (never JSON numbers, so no client-side floating-point rounding).
- **Signs:** revenue, contra revenue and other income are credits − debits (so contra revenue is
  negative); COGS and operating expenses are debits − credits (vendor credits make them smaller).
- **Lines** are ordered by account number. **Every** revenue/expense account is listed, including
  inactive ones and those with no activity in the period (shown as `"0.00"`), so the layout is
  the same for every range. Balance-sheet accounts never appear.
- `hasActivity` is `false` when no posted entry in the period touched an income-statement
  account. A period with no activity is still `200` with a zero-valued statement.

### 400 Bad Request

Invalid or missing dates return RFC 9110 problem details (`application/problem+json`) with a
readable `detail` and per-parameter `errors`:

```json
{
  "title": "Invalid reporting period",
  "status": 400,
  "detail": "Start date 2026-03-31 must be on or before end date 2026-01-01.",
  "errors": { "start": ["Start date 2026-03-31 must be on or before end date 2026-01-01."] }
}
```
