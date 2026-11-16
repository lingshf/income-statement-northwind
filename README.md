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

`GET /income-statement?start=YYYY-MM-DD&end=YYYY-MM-DD` — response shape to be documented
when implemented.
