using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayDesk.Core.Data;
using PayDesk.Core.Models;

namespace PayDesk.Web.Pages;

public class IndexModel : PageModel
{
    private readonly PayDeskDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        PayDeskDbContext db,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger<IndexModel> logger)
    {
        _db = db;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty]
    [Required(ErrorMessage = "Please select a target expense month.")]
    [RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$", ErrorMessage = "Month must be in YYYY-MM format (e.g. 2026-11)")]
    public string TargetMonth { get; set; } = DateTime.UtcNow.ToString("yyyy-MM");

    [BindProperty]
    [Required(ErrorMessage = "Please choose a CSV expense file to upload.")]
    public IFormFile? UploadFile { get; set; }

    public List<ExpenseFile> PendingFiles { get; set; } = new();
    public List<MonthlyConsolidation> AvailablePayslips { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != null)
            {
                PendingFiles = await _db.ExpenseFiles
                    .Include(f => f.Items)
                    .Where(f => f.UserId == userId && f.Status == ExpenseFileStatus.Pending)
                    .OrderByDescending(f => f.UploadedAt)
                    .ToListAsync();

                AvailablePayslips = await _db.MonthlyConsolidations
                    .Where(c => c.UserId == userId)
                    .OrderByDescending(c => c.Month)
                    .ToListAsync();
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid || UploadFile == null)
        {
            await OnGetAsync();
            return Page();
        }

        // Validate that consolidation hasn't already occurred for this month
        bool alreadyConsolidated = await _db.MonthlyConsolidations.AnyAsync(c => c.Month == TargetMonth);
        if (alreadyConsolidated)
        {
            ErrorMessage = $"Cannot upload expenses: Consolidation has already run for month '{TargetMonth}'. No further expenses can be logged for that month.";
            await OnGetAsync();
            return Page();
        }

        if (UploadFile.Length == 0)
        {
            ErrorMessage = "Uploaded file is empty.";
            await OnGetAsync();
            return Page();
        }

        // Parse CSV lines
        var items = new List<ExpenseItem>();
        int lineIndex = 0;

        using (var reader = new StreamReader(UploadFile.OpenReadStream()))
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineIndex++;
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                // Format: <YYYY-MM-DD Date of Expense>,<Description of Expense>,<CurrencyAmount>
                var parts = line.Split(',', 3);
                if (parts.Length < 3)
                {
                    ErrorMessage = $"CSV format error at line {lineIndex}: Expected '<YYYY-MM-DD>,<Description>,<Amount>' but got: '{line}'";
                    await OnGetAsync();
                    return Page();
                }

                if (!DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    ErrorMessage = $"Invalid date format at line {lineIndex}: '{parts[0].Trim()}'. Expected YYYY-MM-DD.";
                    await OnGetAsync();
                    return Page();
                }

                string description = parts[1].Trim();
                if (string.IsNullOrEmpty(description))
                {
                    ErrorMessage = $"Description cannot be empty at line {lineIndex}.";
                    await OnGetAsync();
                    return Page();
                }

                string amountRaw = parts[2].Trim().TrimStart('$');
                if (!decimal.TryParse(amountRaw, NumberStyles.Currency | NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                {
                    ErrorMessage = $"Invalid currency amount at line {lineIndex}: '{parts[2].Trim()}'. Must be a positive number.";
                    await OnGetAsync();
                    return Page();
                }

                items.Add(new ExpenseItem
                {
                    Date = date,
                    Description = description,
                    Amount = amount
                });
            }
        }

        if (items.Count == 0)
        {
            ErrorMessage = "The uploaded CSV contained no valid expense rows.";
            await OnGetAsync();
            return Page();
        }

        // Save file to disk
        string configuredDataDir = _configuration["Storage:DataDirectory"] 
            ?? Environment.GetEnvironmentVariable("PAYDESK_DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "../../data");
        string dataDir = Path.GetFullPath(configuredDataDir);
        string uploadDir = Path.Combine(dataDir, "uploads", TargetMonth);

        Directory.CreateDirectory(uploadDir);

        string originalFileName = Path.GetFileName(UploadFile.FileName);
        string sanitizedFileName = string.Join("_", originalFileName.Split(Path.GetInvalidFileNameChars()));
        string storageFileName = $"{userId}_{Guid.NewGuid():N}_{sanitizedFileName}";
        string storagePath = Path.Combine(uploadDir, storageFileName);

        using (var fileStream = new FileStream(storagePath, FileMode.Create))
        {
            await UploadFile.CopyToAsync(fileStream);
        }

        // Create database record
        var expenseFile = new ExpenseFile
        {
            UserId = userId,
            FileName = originalFileName,
            StoragePath = storagePath,
            Month = TargetMonth,
            Status = ExpenseFileStatus.Pending,
            TotalAmount = items.Sum(i => i.Amount),
            UploadedAt = DateTime.UtcNow,
            Items = items
        };

        _db.ExpenseFiles.Add(expenseFile);
        await _db.SaveChangesAsync();

        SuccessMessage = $"Successfully uploaded '{originalFileName}' with {items.Count} expense item(s) totalling ${expenseFile.TotalAmount:N2} for month {TargetMonth}.";
        return RedirectToPage("/Index");
    }
}
