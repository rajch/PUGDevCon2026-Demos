using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayDesk.Core.Data;
using PayDesk.Core.Models;
using PayDesk.Web.Services;

namespace PayDesk.Web.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ConsolidationModel : PageModel
{
    private readonly PayDeskDbContext _db;
    private readonly ConsolidatorRunner _consolidatorRunner;

    public ConsolidationModel(PayDeskDbContext db, ConsolidatorRunner consolidatorRunner)
    {
        _db = db;
        _consolidatorRunner = consolidatorRunner;
    }

    public class MonthPendingSummary
    {
        public string Month { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public int UserCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public List<MonthPendingSummary> PendingSummaries { get; set; } = new();
    public List<MonthlyConsolidation> CompletedConsolidations { get; set; } = new();

    [BindProperty]
    public string? SelectedMonth { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string? ConsolidationLogs { get; set; }
    public bool? ConsolidationSuccess { get; set; }

    public async Task OnGetAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var pendingFiles = await _db.ExpenseFiles
            .Where(f => f.Status == ExpenseFileStatus.Pending)
            .ToListAsync();

        PendingSummaries = pendingFiles
            .GroupBy(f => f.Month)
            .Select(g => new MonthPendingSummary
            {
                Month = g.Key,
                FileCount = g.Count(),
                UserCount = g.Select(f => f.UserId).Distinct().Count(),
                TotalAmount = g.Sum(f => f.TotalAmount)
            })
            .OrderByDescending(s => s.Month)
            .ToList();

        CompletedConsolidations = await _db.MonthlyConsolidations
            .Include(c => c.User)
            .OrderByDescending(c => c.ConsolidatedAt)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostConsolidateAsync(string month)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            ErrorMessage = "Please specify a valid month to consolidate.";
            await LoadDataAsync();
            return Page();
        }

        var (success, output, error) = await _consolidatorRunner.RunAsync(month);

        ConsolidationSuccess = success;
        ConsolidationLogs = $"--- PROCESS OUTPUT ---\n{output}\n--- PROCESS ERRORS ---\n{error}";

        if (success)
        {
            SuccessMessage = $"Consolidation executable executed successfully for month '{month}'.";
        }
        else
        {
            ErrorMessage = $"Consolidation executable failed for month '{month}'. Check execution logs below.";
        }

        await LoadDataAsync();
        return Page();
    }
}
