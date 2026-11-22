namespace PayDesk.Core.Models;

public class MonthlyConsolidation
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public required string Month { get; set; } // Format: YYYY-MM
    public decimal TotalExpenses { get; set; }
    public decimal TotalPayout { get; set; }
    public required string PayslipFilePath { get; set; }
    public DateTime ConsolidatedAt { get; set; } = DateTime.UtcNow;
}
