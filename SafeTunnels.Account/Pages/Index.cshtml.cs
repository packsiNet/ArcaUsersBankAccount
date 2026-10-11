using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeTunnels.Account.Services;
using SafeTunnels.Account.Validation;
using System.Security.Cryptography;
namespace SafeTunnels.Account.Pages;
public sealed class IndexModel(ApplicantService applicants, IDataProtectionProvider protection) : PageModel
{
    private readonly ITimeLimitedDataProtector tokens = protection.CreateProtector("Registration.Idempotency.v1").ToTimeLimitedDataProtector();
    [BindProperty] public RegistrationInput Input { get; set; } = new();
    [BindProperty] public string SubmissionToken { get; set; } = "";
    public void OnGet() => SubmissionToken = tokens.Protect(Guid.NewGuid().ToString(), TimeSpan.FromHours(12));
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Guid key;
        try { key = Guid.Parse(tokens.Unprotect(SubmissionToken, out _)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) {
            ModelState.AddModelError("", "مهلت فرم پایان یافته است؛ دوباره ثبت را انجام دهید.");
            SubmissionToken = tokens.Protect(Guid.NewGuid().ToString(), TimeSpan.FromHours(12));
            ModelState.Remove(nameof(SubmissionToken));
            return Page();
        }
        Input.Normalize();
        ModelState.Clear();
        TryValidateModel(Input, nameof(Input));
        if (!ModelState.IsValid) return Page();
        try {
            var tracking = await applicants.RegisterAsync(Input, key, ct);
            TempData["TrackingCode"] = tracking;
            return RedirectToPage("/Success");
        } catch (DuplicateIbanException ex) {
            foreach (var i in ex.Accounts) ModelState.AddModelError($"Input.Accounts[{i}].IBAN", "این شماره شبا قبلاً ثبت شده است.");
            ModelState.AddModelError("", "شماره شبای تکراری است و قبلاً ثبت شده است.");
            return Page();
        } catch (AccountLimitException) {
            ModelState.AddModelError("", "برای هر فرد حداکثر ده حساب بانکی قابل ثبت است.");
            return Page();
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // Never log exception instances: database messages may embed submitted values.
            ModelState.AddModelError("", "ثبت اطلاعات انجام نشد. اطلاعات فرم حفظ شده؛ لطفاً دوباره تلاش کنید.");
            return Page();
        }
    }
}
