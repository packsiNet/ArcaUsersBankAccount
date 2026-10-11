using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SafeTunnels.Account.Data;
using SafeTunnels.Account.Models;
using SafeTunnels.Account.Services;
using SafeTunnels.Account.Validation;
namespace SafeTunnels.Account.Pages.Admin;
public sealed class DashboardModel(ApplicationDbContext db, EncryptionService encryption, SignInManager<IdentityUser> signIn) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public int People { get; private set; }
    public int Accounts { get; private set; }
    public DateTime? LastRegistration { get; private set; }
    public int TotalPages { get; private set; }
    public int FilteredPeople { get; private set; }
    public const int PageSize = 25;
    public List<Applicant> Applicants { get; private set; } = [];
    private IQueryable<Applicant> Query()
    {
        var q = db.Applicants.AsNoTracking();
        var s = BankingValidation.Name(Search);
        if (s.Length > 100) s = s[..100];
        Search = s;
        if (s.Length > 0) {
            var mobile = BankingValidation.Mobile(s);
            q = q.Where(x => (x.FirstName + " " + x.LastName).Contains(s) || (x.Mobile != null && mobile != null && x.Mobile.Contains(mobile)));
        }
        return q;
    }
    public async Task OnGetAsync(CancellationToken ct)
    {
        People = await db.Applicants.CountAsync(ct);
        Accounts = await db.BankAccounts.CountAsync(ct);
        LastRegistration = await db.Applicants.MaxAsync(x => (DateTime?)x.CreatedAt, ct);
        var q = Query();
        FilteredPeople = await q.CountAsync(ct);
        TotalPages = Math.Max(1, (int)Math.Ceiling(FilteredPeople / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Applicants = await q.Include(x => x.BankAccounts).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync(ct);
    }
    public string Mask(BankAccount bank) => EncryptionService.Mask(
        encryption.Decrypt(bank.IBAN, EncryptionService.Purpose(bank.Id, "iban")));
    public async Task<IActionResult> OnPostLogoutAsync() { await signIn.SignOutAsync(); return RedirectToPage("/Admin/Login"); }
    public async Task<IActionResult> OnPostExportAsync(CancellationToken ct)
    {
        // Fixed, bounded report. No names/mobile/full bank values are exported.
        var rows = await Query().Include(x => x.BankAccounts).OrderBy(x => x.CreatedAt).Take(10000).ToListAsync(ct);
        var csv = new StringBuilder("TrackingCode,Bank,IBANMasked,CreatedAtUtc\r\n");
        foreach (var a in rows) foreach (var b in a.BankAccounts)
            csv.AppendLine(string.Join(",", new[] { a.TrackingCode, b.BankName, Mask(b), a.CreatedAt.ToString("O") }.Select(CsvCell)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "accounts-masked.csv");
    }
    public static string CsvCell(string s)
    {
        if (s.TrimStart().StartsWith('=') || s.TrimStart().StartsWith('+') || s.TrimStart().StartsWith('-') || s.TrimStart().StartsWith('@')) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
