using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PayDesk.Core.Data;
using PayDesk.Core.Models;

namespace PayDesk.Web.Services;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PayDeskDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        // Wait and retry for database availability on startup (e.g. while postgres container initializes)
        const int maxRetries = 15;
        for (int retry = 1; retry <= maxRetries; retry++)
        {
            try
            {
                logger.LogInformation("Connecting to database (attempt {Attempt}/{MaxRetries})...", retry, maxRetries);
                if (await context.Database.CanConnectAsync())
                {
                    logger.LogInformation("Database reachable. Ensuring schema is created...");
                    await context.Database.EnsureCreatedAsync();
                    break;
                }
            }
            catch (Exception ex) when (retry < maxRetries)
            {
                logger.LogWarning("Database not ready yet ({Message}). Waiting 2s before retry...", ex.Message);
            }

            if (retry == maxRetries)
            {
                // Final attempt
                await context.Database.EnsureCreatedAsync();
                break;
            }

            await Task.Delay(2000);
        }

        // Seed Roles
        string[] roles = ["Admin", "User"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Seed Admin user
        string adminEmail = "admin@paydesk.local";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Administrator",
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(adminUser, "Admin@123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        // Seed standard users
        var defaultUsers = new (string Email, string Name, string Password)[]
        {
            ("alice@paydesk.local", "Alice Morgan", "User@123!"),
            ("bob@paydesk.local", "Bob Jenkins", "User@123!")
        };

        foreach (var (email, name, pwd) in defaultUsers)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = name,
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(user, pwd);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "User");
                }
            }
        }
    }
}
