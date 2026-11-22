using Microsoft.AspNetCore.Identity;

namespace PayDesk.Core.Models;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ExpenseFile> ExpenseFiles { get; set; } = new List<ExpenseFile>();
    public ICollection<MonthlyConsolidation> MonthlyConsolidations { get; set; } = new List<MonthlyConsolidation>();
}

