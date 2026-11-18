# Northwind Income Statement — Product Brief

## Purpose

Give an accountant a clear, accurate income statement (profit and loss, P&L) for
Northwind Coffee Roasters for any selected date range. **Data correctness is the top
priority**; readable, changeable code comes next; visual polish matters least.

## Scope

1. **Backend** — loads `ledger.json` at startup (no database required) and serves
   `GET /income-statement?start=YYYY-MM-DD&end=YYYY-MM-DD`.
2. **Frontend** — a page with a start date, an end date, and the statement for that
   range. Server-rendered pages are fine. It must be readable by an accountant.
3. **Tests** — automated tests that would catch a real accounting mistake, not only a typo.
4. **Bank reconciliation** — loads `bank_transactions.json` at startup and shows, on a
   read-only page, which bank lines match posted cash journal entries (see
   [Bank reconciliation](#bank-reconciliation)).

### Statement layout

Sections and calculated lines, in this order, each section with one line per account
and a subtotal:

```
Revenue                (operating revenue + contra revenue → Net revenue)
Cost of goods sold
Gross profit
Operating expenses
Operating income
Other income
Net income
```

### Non-goals

Authentication, deployment, Docker, ledger editing, importing arbitrary files,
multi-company or multi-currency reporting, balance sheet, and elaborate styling.

## Tech stack

- C# on .NET 9 (`net9.0`), SDK pinned by `global.json`.
- ASP.NET Core minimal API for `/income-statement`; Razor Pages for the UI, in the same
  web project.
- xUnit for unit and HTTP integration tests (`Microsoft.AspNetCore.Mvc.Testing`).
- GitHub Actions CI on .NET 9: restore, build, test on every push and pull request.

## Data contract (`ledger.json`)

Top level: `company`, `currency`, `accounts`, `journal_entries`.

**Accounts**

| Field | Meaning |
| --- | --- |
| `number` | Account identifier. A string; all account numbers have the same length. Journal lines reference it. Never parse it as a number. |
| `name` | Display name |
| `type` | `asset`, `liability`, `equity`, `revenue`, or `expense` |
| `subtype` | Statement placement (below) |
| `is_active` | Whether new postings are allowed today. **Not** a reporting filter: inactive accounts still report their history. |

| `subtype` | Section |
| --- | --- |
| `operating_revenue` | Revenue |
| `contra_revenue` | Revenue. Returns, refunds, discounts; reduces revenue |
| `cogs` | Cost of goods sold |
| `operating_expense` | Operating expenses |
| `other_income` | Other income |
| `balance_sheet` | Not on the income statement |

**Journal entries**

| Field | Meaning |
| --- | --- |
| `id` | Entry id |
| `date` | Accounting date, `YYYY-MM-DD`. No time, no time zone |
| `status` | `posted` (in the books), `draft` (awaiting approval), `void` (cancelled) |
| `memo` | Free text. Context only, never a calculation input |
| `lines[].account` | Account number |
| `lines[].debit`, `lines[].credit` | Decimal strings with exactly 2 decimals. One of the two is `"0.00"` on each line |

Every entry has two or more lines and total debits equal total credits.

`ledger.csv` is a flattened, one-row-per-journal-line copy of the same data (with
account name/type/subtype joined in) for manual verification in a spreadsheet. The
application reads only `ledger.json`.

## Bank transactions (`bank_transactions.json`)

Bank statement lines, kept in their own file (never in `ledger.json`) and in the same
snake_case style. Top level: `bank_entries`, an array (may be empty).

| Field | Meaning |
| --- | --- |
| `id` | Bank entry id. Required and unique |
| `date` | Bank posting date, `YYYY-MM-DD`. No time, no time zone |
| `amount` | Signed decimal string with exactly 2 decimals: positive is money in, negative is money out |
| `description` | Free text from the bank. Context only, never a matching input |

## Accounting rules

- **Double entry.** Each line is a debit or a credit to one account.
- **Normal balance.** Assets and expenses increase with debits; liabilities, equity, and
  revenue increase with credits.
- **Period, not point in time.** The statement sums activity between `start` and `end`,
  both **inclusive**. It is not a cumulative balance.
- Include **only `posted`** entries. Ignore `draft` and `void`.
- Only `revenue` and `expense` accounts appear. Balance-sheet accounts are excluded even
  though they appear in the same entries.
- Revenue, contra revenue, and other income: amount = **credits − debits**. Contra-revenue
  debits therefore show as negative revenue (do not flip them positive).
- Expenses (COGS, operating): amount = **debits − credits**. Vendor credits reduce expense.
- Net revenue = operating revenue + contra revenue (signed).
- Gross profit = net revenue − COGS.
- Operating income = gross profit − operating expenses.
- Net income = operating income + other income.
- **Report entries as recorded.** Do not re-accrue, spread, defer, or reclassify anything
  (e.g. a quarter's rent paid in January stays in January; an annual subscription billed to
  deferred revenue is not revenue until recognized by its own entries).

## Money

- All amounts are exact with **2 decimal places**. Never use binary floating point
  (`double`/`float`) for money.
- Implement a **`Money` value object** that wraps the logic: parse from the ledger's
  decimal string (reject anything that is not a valid 2-decimal amount), add, subtract,
  negate, compare, zero, equality, and format as an invariant 2-decimal string. All
  financial arithmetic in the domain goes through `Money`.
- API money values are serialized as exact decimal strings (e.g. `"-44480.14"`).
- The UI shows amounts to 2 decimals, right-aligned, with thousands separators, and
  negatives in accounting parentheses, e.g. `(800.25)`.

## API

`GET /income-statement?start=YYYY-MM-DD&end=YYYY-MM-DD`

- Both parameters required; exact `YYYY-MM-DD`; must be real calendar dates; `start <= end`.
  Otherwise respond `400` with a clear problem-details message.
- A valid range with no activity returns a zero-valued statement (not an error).
- The response shape is a design choice; it must include company, currency, period,
  account lines (number, name, amount) per section, section subtotals, gross profit,
  operating income, and net income. Document the shape in `README.md`.
- Account lines are ordered by account number. Document whether zero-activity accounts are
  shown.

## UI

- Default range: 2026-01-01 to 2026-03-31.
- Labeled date inputs; submitting shows the statement for that range.
- Shows company, currency, and period; account number distinct from name; subtotals and
  profit lines easy to scan; clear message for invalid input or empty periods.
- API and UI use the **same** calculation service.

## Ledger validation

On load, fail fast with a clear error if: an entry is unbalanced, a line references an
unknown account, an amount is malformed, a line has both or neither of debit/credit
non-zero, a date is invalid, or a status/type/subtype is unknown.

`bank_transactions.json` is validated the same way: fail fast if the file is missing or
not valid JSON, `bank_entries` is absent, an `id` is missing or duplicated, a date is
invalid, or an amount is not a valid 2-decimal string.

## Bank reconciliation

Match bank entries to posted journal entries that move cash, to show what the bank and
the books agree on and what needs review. Reconciliation is read-only: it never changes
the ledger, and it does not affect the income statement.

**Matching rules**

- Eligible journal entries: `posted` entries with at least one line on Cash account
  `1000`. Draft, void, and non-cash entries are excluded.
- A journal entry's cash amount = its cash **debits − credits** (signed, so a payment is
  negative, matching the bank's sign). It must equal the bank amount **exactly**, to the cent.
- Date offset = bank date − journal date, in calendar days. It must be between **−3 and +3,
  inclusive**. Weekends and holidays are not skipped.
- Matching is **one-to-one**: a bank entry or journal entry is used at most once, so a
  duplicated bank line stays unmatched.
- Deterministic order: bank entries are processed earliest date first (file order for
  ties); each takes the earliest-dated remaining eligible journal entry (ledger order for
  ties).
- Memos and descriptions are never used for matching.

**Page (`/reconciliation`)**

- Covers all dates. Shows company, currency, the cash account, and the rules in plain words.
- Counts of matched pairs, unmatched bank entries, and unmatched journal entries.
- Three tables: matched pairs (both ids, both dates, description/memo, date offset,
  amount); unmatched bank entries; unmatched journal entries (with cash amount).
- Amounts use the same accounting format as the income statement. Each empty table is
  replaced by a clear message.
- Linked from the income statement page, and vice versa.

**Checks for the supplied files** (computed, not hard-coded): 5 matched pairs —
BT-01/JE-007, BT-02/JE-006, BT-03/JE-013, BT-04/JE-015, BT-06/JE-022; unmatched bank
entries BT-05 (service fee, not in the books) and BT-07 (duplicate payroll); unmatched
journal entry JE-023 (interest, not on the statement).

## Reconciliation targets

The supplied ledger has 15 accounts, 25 entries, 51 lines. For 2026-01-01 to 2026-03-31:

| Account or calculated line | USD |
| --- | ---: |
| 4000 Product Revenue | 35,650.75 |
| 4100 Subscription Revenue | 3,000.00 |
| 4900 Sales Returns & Discounts | (800.25) |
| **Net revenue** | **37,850.50** |
| 5000 Cost of Goods Sold | 14,272.75 |
| **Gross profit** | **23,577.75** |
| 6000 Salaries | 55,500.00 |
| 6100 Rent | 9,000.00 |
| 6200 Software | 1,099.97 |
| 6300 Marketing (legacy) | 2,500.10 |
| **Operating expenses** | **68,100.07** |
| **Operating income** | **(44,522.32)** |
| 7000 Interest Income | 42.18 |
| **Other income** | **42.18** |
| **Net income** | **(44,480.14)** |

These are checks, not values to hard-code. The report must be correct for any valid range,
including single days and ranges outside the data.

## Tests that matter

Exact cents; `Money` parsing/arithmetic; signed contra revenue; expense credits;
posted-only filtering (draft JE-019 and void JE-009/JE-025 excluded); inclusive start and
end boundaries; inactive account (6300) included; balance-sheet exclusion; no
re-recognition (rent, annual subscription); multi-line entry (JE-016); empty period;
API validation (400s) and the Q1 response values; the page renders the requested range.
Bank reconciliation: date offsets −4/−3/+3/+4, exact cents and sign, posted-cash-only
eligibility, one-to-one handling of duplicates, bank-file validation errors, and the
supplied-file checks above.
Expected values in tests are written out independently, not derived from production code.

## Documentation

- `README.md`: tool versions, commands to run the app and the tests, API response shape.
- `NOTES.md` (≤ one page): Q1 2026 net income, decisions and data assumptions, how the
  numbers were independently checked, limitations and next steps.
