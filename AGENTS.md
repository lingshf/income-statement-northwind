# Agent and contributor guidance

- Read `spec.md` first. It is the product brief. `ledger.json` is the data.
- **Data correctness is the top priority.** Use the `Money` value object for all
  financial arithmetic; never `double`/`float`. Account numbers are fixed-length strings.
- Do not hard-code reconciliation totals; compute them. Write expected values in tests
  independently of production code.
- Keep the domain calculation separate from HTTP and Razor code; API and UI share one
  calculation service.
- Workflow: one feature branch and one PR per task. Self-review the diff before
  requesting review. Merge only after CI passes **and** a human approves. After merging,
  confirm CI on `main` passes before starting the next task.
- Build and test: `dotnet build` and `dotnet test` from the repository root.
