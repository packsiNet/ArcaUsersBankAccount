using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SafeTunnels.Account.Pages;
public sealed class SuccessModel : PageModel
{
    public string TrackingCode { get; private set; } = "";
    public IActionResult OnGet()
    {
        TrackingCode = TempData["TrackingCode"] as string ?? "";
        return TrackingCode.Length == 0 ? RedirectToPage("/Index") : Page();
    }
}
