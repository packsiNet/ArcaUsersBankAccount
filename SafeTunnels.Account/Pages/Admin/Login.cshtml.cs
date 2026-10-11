using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SafeTunnels.Account.Pages.Admin;
public sealed class LoginModel(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [BindProperty, Required, StringLength(256), DataType(DataType.Password)] public string Password { get; set; } = "";
    public IActionResult OnGet() => User.IsInRole("Admin") ? RedirectToPage("/Admin/Dashboard") : Page();
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { Password = ""; ModelState.Remove(nameof(Password)); return Page(); }
        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is not null && await users.IsInRoleAsync(user, "Admin")) {
            var result = await signIn.PasswordSignInAsync(user, Password, false, lockoutOnFailure: true);
            if (result.Succeeded) return RedirectToPage("/Admin/Dashboard");
        } else {
            // Match password hash cost for unknown/non-admin accounts.
            _ = new PasswordHasher<IdentityUser>().HashPassword(new IdentityUser(), Password);
        }
        Password = ""; ModelState.Remove(nameof(Password));
        ModelState.AddModelError("", "ورود ناموفق بود. مشخصات را بررسی کنید یا پس از مدتی دوباره تلاش کنید.");
        return Page();
    }
}
