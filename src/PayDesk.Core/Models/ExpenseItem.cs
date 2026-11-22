namespace PayDesk.Core.Models;

public class ExpenseItem
{
    public int Id { get; set; }
    public int ExpenseFileId { get; set; }
    public ExpenseFile? ExpenseFile { get; set; }

    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
}

