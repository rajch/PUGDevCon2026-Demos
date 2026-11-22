using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayDesk.Core.Data;

namespace PayDesk.Web.Pages;

[Authorize]
public class DownloadPayslipModel : PageModel
{
    private readonly PayDeskDbContext _db;

    public DownloadPayslipModel(PayDeskDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var payslip = await _db.MonthlyConsolidations.FindAsync(id);
        if (payslip == null)
        {
            return NotFound("Payslip not found.");
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        bool isAdmin = User.IsInRole("Admin");

        if (payslip.UserId != currentUserId && !isAdmin)
        {
            return Forbid();
        }

        if (!System.IO.File.Exists(payslip.PayslipFilePath))
        {
            return NotFound($"The PDF file for this payslip was not found at '{payslip.PayslipFilePath}'.");
        }

        string downloadName = Path.GetFileName(payslip.PayslipFilePath);
        return PhysicalFile(payslip.PayslipFilePath, "application/pdf", downloadName);
    }
}

