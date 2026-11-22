using Microsoft.AspNetCore.Identity;
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

        // Ensure database tables are created
        await context.Database.EnsureCreatedAsync();

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
