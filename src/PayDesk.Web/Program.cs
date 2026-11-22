using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PayDesk.Core.Data;
using PayDesk.Core.Models;
using PayDesk.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Database configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Port=5432;Database=paydesk_db;Username=paydesk;Password=paydesk_secret_password";

builder.Services.AddDbContext<PayDeskDbContext>(options =>
    options.UseNpgsql(connectionString));

// Identity configuration
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<PayDeskDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

// App Services
builder.Services.AddScoped<ConsolidatorRunner>();

builder.Services.AddRazorPages();

var app = builder.Build();

// Database initialization & seeding
using (var scope = app.Services.CreateScope())
{
    try
    {
        await DbInitializer.InitializeAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
