# Northwind Income Statement

An income statement (P&L) for Northwind Coffee Roasters over any date range.
See [`spec.md`](spec.md) for the product brief.

## Requirements

- .NET SDK 9 (pinned via `global.json`, rolls forward to the latest 9.0 feature band)

## Commands

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Northwind.IncomeStatement
```

## Data

- `ledger.json` — chart of accounts and journal entries; the application's only data source.
- `ledger.csv` — the same journal, one row per line with account details joined in, for
  manual verification in a spreadsheet. Not read by the application.

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
