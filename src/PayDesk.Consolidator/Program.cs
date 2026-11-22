using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PayDesk.Consolidator.Services;
using PayDesk.Core.Data;
using PayDesk.Core.Models;

namespace PayDesk.Consolidator;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("      PAYDESK CONSOLIDATION ENGINE (CLI)          ");
        Console.WriteLine("==================================================");

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        string? monthArg = null;
        string? dataDirArg = null;
        string? connectionStringArg = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--month" || args[i] == "-m") && i + 1 < args.Length)
            {
                monthArg = args[++i];
            }
            else if (args[i] == "--data-dir" && i + 1 < args.Length)
            {
                dataDirArg = args[++i];
            }
            else if (args[i] == "--connection-string" && i + 1 < args.Length)
            {
                connectionStringArg = args[++i];
            }
            else if (!args[i].StartsWith('-') && monthArg == null)
            {
                monthArg = args[i];
            }
        }

        if (string.IsNullOrWhiteSpace(monthArg) || !Regex.IsMatch(monthArg, @"^\d{4}-(0[1-9]|1[0-2])$"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Target month is required in 'YYYY-MM' format (e.g., 2026-11).");
            Console.ResetColor();
            Console.WriteLine("Usage: PayDesk.Consolidator --month <YYYY-MM> [--data-dir <path>]");
            return 1;
        }

        string connectionString = connectionStringArg 
            ?? config.GetConnectionString("DefaultConnection") 
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=paydesk_db;Username=paydesk;Password=paydesk_secret_password";

        string configuredDataDir = dataDirArg 
            ?? config["Storage:DataDirectory"] 
            ?? Environment.GetEnvironmentVariable("PAYDESK_DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "../../data");

        string dataDir = Path.GetFullPath(configuredDataDir);
        string payslipsDir = Path.Combine(dataDir, "payslips", monthArg);

        Console.WriteLine($"[Info] Processing month: {monthArg}");
        Console.WriteLine($"[Info] Payslip output directory: {payslipsDir}");

        var optionsBuilder = new DbContextOptionsBuilder<PayDeskDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        try
        {
            using var db = new PayDeskDbContext(optionsBuilder.Options);

            // Ensure database is accessible
            if (!await db.Database.CanConnectAsync())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Error] Cannot connect to PostgreSQL database.");
                Console.ResetColor();
                return 2;
            }

            // Find all pending expense files for this month
            var pendingFiles = await db.ExpenseFiles
                .Include(f => f.Items)
                .Include(f => f.User)
                .Where(f => f.Month == monthArg && f.Status == ExpenseFileStatus.Pending)
                .ToListAsync();

            if (pendingFiles.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Notice] No pending expense files found for {monthArg}. Nothing to consolidate.");
                Console.ResetColor();
                return 0;
            }

            Console.WriteLine($"[Info] Found {pendingFiles.Count} pending file(s) across user(s).");

            var userGroups = pendingFiles.GroupBy(f => f.UserId).ToList();

            foreach (var group in userGroups)
            {
                var user = group.First().User;
                if (user == null)
                {
                    user = await db.Users.FindAsync(group.Key);
                }

                if (user == null)
                {
                    Console.WriteLine($"[Warning] Skipping user ID '{group.Key}' because user record was not found.");
                    continue;
                }

                var allItems = group.SelectMany(f => f.Items).OrderBy(i => i.Date).ToList();
                decimal totalExpenses = allItems.Sum(i => i.Amount);
                decimal totalPayout = totalExpenses;

                string sanitizedUserName = string.Join("_", (user.UserName ?? user.Id).Split(Path.GetInvalidFileNameChars()));
                string payslipFileName = $"{sanitizedUserName}_{monthArg}_payslip.pdf";
                string payslipPath = Path.Combine(payslipsDir, payslipFileName);

                Console.WriteLine($"[Info] Generating payslip for {user.UserName} ({allItems.Count} item(s), Total: ${totalPayout:N2})...");

                PdfPayslipGenerator.GeneratePayslip(user, monthArg, allItems, totalPayout, payslipPath);

                // Update or create MonthlyConsolidation
                var existingConsolidation = await db.MonthlyConsolidations
                    .FirstOrDefaultAsync(c => c.UserId == user.Id && c.Month == monthArg);

                if (existingConsolidation != null)
                {
                    existingConsolidation.TotalExpenses += totalExpenses;
                    existingConsolidation.TotalPayout = existingConsolidation.TotalExpenses;
                    existingConsolidation.PayslipFilePath = payslipPath;
                    existingConsolidation.ConsolidatedAt = DateTime.UtcNow;
                }
                else
                {
                    db.MonthlyConsolidations.Add(new MonthlyConsolidation
                    {
                        UserId = user.Id,
                        Month = monthArg,
                        TotalExpenses = totalExpenses,
                        TotalPayout = totalPayout,
                        PayslipFilePath = payslipPath,
                        ConsolidatedAt = DateTime.UtcNow
                    });
                }

                // Mark processed files
                foreach (var file in group)
                {
                    file.Status = ExpenseFileStatus.Processed;
                    file.ProcessedAt = DateTime.UtcNow;
                }
            }

            await db.SaveChangesAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Success] Consolidation completed successfully for {monthArg}.");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Consolidation failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            return 3;
        }
    }
}
