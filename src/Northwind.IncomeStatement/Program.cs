using Northwind.IncomeStatement.Domain;

var builder = WebApplication.CreateBuilder(args);

// Load and validate the ledger before the app starts so bad data fails fast.
var ledgerPath = builder.Configuration["Ledger:Path"] ?? Path.Combine(AppContext.BaseDirectory, "ledger.json");
builder.Services.AddSingleton(LedgerLoader.LoadFile(ledgerPath));
builder.Services.AddSingleton<IncomeStatementService>();

builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
