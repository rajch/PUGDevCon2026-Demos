namespace PayDesk.Core.Models;

public enum ExpenseFileStatus
{
    Pending,
    Processed
}

public class ExpenseFile
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public required string FileName { get; set; }
    public required string StoragePath { get; set; }
    public required string Month { get; set; } // Format: YYYY-MM
    public ExpenseFileStatus Status { get; set; } = ExpenseFileStatus.Pending;
    public decimal TotalAmount { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    public ICollection<ExpenseItem> Items { get; set; } = new List<ExpenseItem>();
}
