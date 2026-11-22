using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayDesk.Core.Models;

namespace PayDesk.Web.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UsersModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UsersModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public class UserViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public DateTime CreatedAt { get; set; }
        public IList<string> Roles { get; set; } = new List<string>();
    }

    public List<UserViewModel> UsersList { get; set; } = new();

    [BindProperty]
    public CreateUserInput Input { get; set; } = new();

    public class CreateUserInput
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "User";
    }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        var users = await _userManager.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
        UsersList = new List<UserViewModel>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            UsersList.Add(new UserViewModel
            {
                Id = user.Id,
                Email = user.Email ?? "N/A",
                FullName = user.FullName,
                CreatedAt = user.CreatedAt,
                Roles = roles
            });
        }
    }

    public async Task<IActionResult> OnPostCreateUserAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadUsersAsync();
            return Page();
        }

        var existing = await _userManager.FindByEmailAsync(Input.Email);
        if (existing != null)
        {
            ErrorMessage = $"A user with email '{Input.Email}' already exists.";
            await LoadUsersAsync();
            return Page();
        }

        var newUser = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            FullName = Input.FullName,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(newUser, Input.Password);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join("; ", result.Errors.Select(e => e.Description));
            await LoadUsersAsync();
            return Page();
        }

        if (await _roleManager.RoleExistsAsync(Input.Role))
        {
            await _userManager.AddToRoleAsync(newUser, Input.Role);
        }

        SuccessMessage = $"User '{Input.FullName}' ({Input.Email}) created successfully as role '{Input.Role}'.";
        return RedirectToPage("/Admin/Users");
    }
}

