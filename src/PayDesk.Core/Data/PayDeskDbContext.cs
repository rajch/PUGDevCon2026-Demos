using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PayDesk.Core.Models;

namespace PayDesk.Core.Data;

public class PayDeskDbContext : IdentityDbContext<ApplicationUser>
{
    public PayDeskDbContext(DbContextOptions<PayDeskDbContext> options) : base(options)
    {
    }

    public DbSet<ExpenseFile> ExpenseFiles => Set<ExpenseFile>();
    public DbSet<ExpenseItem> ExpenseItems => Set<ExpenseItem>();
    public DbSet<MonthlyConsolidation> MonthlyConsolidations => Set<MonthlyConsolidation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ExpenseFile>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.Month, e.Status });
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.HasOne(e => e.User)
                  .WithMany(u => u.ExpenseFiles)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ExpenseItem>(entity =>
        {
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.HasOne(e => e.ExpenseFile)
                  .WithMany(f => f.Items)
                  .HasForeignKey(e => e.ExpenseFileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MonthlyConsolidation>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.Month }).IsUnique();
            entity.Property(e => e.TotalExpenses).HasPrecision(18, 2);
            entity.Property(e => e.TotalPayout).HasPrecision(18, 2);
            entity.HasOne(e => e.User)
                  .WithMany(u => u.MonthlyConsolidations)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
