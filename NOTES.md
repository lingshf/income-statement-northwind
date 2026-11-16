# Notes

## Q1 2026 result

**Net income for 2026-01-01 – 2026-03-31: −44,480.14 USD** (a loss). It is computed, not hard-coded.
The other totals: net revenue 37,850.50, COGS 14,272.75, gross profit 23,577.75, operating
expenses 68,100.07, operating income −44,522.32, other income 42.18.

## Decisions and data assumptions

- **Only `posted` entries count.** The `draft` bonus accrual (JE-019) and both `void` entries
  (JE-009, JE-025) are excluded. JE-009 duplicates JE-010, so including it would double-count a sale.
- **Both dates are inclusive** and must be strict `YYYY-MM-DD`. If `start > end`, the API returns 400
  and the UI shows an error. A valid period with no activity gives a zero statement, not an error.
- **Signs:** revenue, contra revenue and other income are credits − debits; COGS and operating expenses
  are debits − credits. So returns (4900) reduce revenue, and vendor credits (JE-020) reduce expense.
- **The ledger is reported as booked; nothing is re-accrued.** Prepaid rent (JE-007) is expensed on
  2026-01-01. The annual subscription billed to deferred revenue (JE-004) counts only when a monthly
  recognition entry posts.
- **Every revenue and expense account is listed**, including inactive 6300 and accounts with zero
  activity, ordered by account number (compared as a string). This keeps the layout stable across
  ranges. Balance-sheet accounts never appear.
- **Money** is a `readonly struct` holding a `long` count of cents. Parsing accepts only
  `-?digits.dd` and is culture-invariant, arithmetic throws on overflow, and nothing uses
  float/double. The API emits amounts as JSON strings. Account numbers stay strings and must all be
  the same length.
- **The ledger is validated once, at startup.** The app refuses to start, listing every error, if it
  finds an unbalanced entry, an unknown or duplicate account or entry, a malformed or negative amount,
  a line with both or neither of debit and credit, a bad date, or an unknown status, type or subtype.

## How the numbers were independently checked

1. Q1 was re-summed by hand from `ledger.csv` (posted entries in range, grouped by account,
   then the sign rules) and matches the reconciliation table in `spec.md` to the cent.
2. Tests use literal expected values from that hand calculation, never values from production code.
   They cover:
   - the full Q1 table
   - each edge case on a single day: the draft, the voids, the contra sale (JE-012), the multi-line
     sale (JE-016/017), the vendor credit (JE-020), the inclusive first and last days, and entries
     just outside Q1
   - the empty period
   - the whole-ledger total (−30,380.14 = −44,480.14 + 5,000.00 + 9,100.00)
3. The API, the UI and the service tests all agree on −44,480.14, and the UI was also checked in a
   browser.

## Limitations and next steps

- The ledger is read once, at startup; edits to `ledger.json` need a restart.
- There is only one currency (USD) and one company. There is no authentication.
- Possible next steps: a CSV/PDF export, and a period picker with presets (month, quarter, YTD).

## Extension points (designed for, not implemented)

1. **Comparative periods (columns side by side).** `IncomeStatementService.Calculate(start, end)`
   depends only on its date range and the immutable `Ledger`. To add columns, call it once per period
   and zip the reports by account number; every account is always listed, so the rows line up. Only the
   API DTO and the Razor table would change.
2. **Including draft entries (or filtering by another entry or line attribute).** Every inclusion rule
   lives in one private predicate, `IncomeStatementService.IsIncluded`. Making it a parameter (for
   example a set of included statuses) changes only that seam; the request would pass the option
   through.
3. **New sections or account subtypes** (for example "Other expense" or "Income tax").
   `StatementLayout.Sections` is data: each section lists its subtypes and its sign rule, and
   `SectionFor` derives the subtype-to-section map from it. To add one, add the subtype enum value,
   one `SectionDefinition` and a report and UI row; the calculation loop doesn't change.
