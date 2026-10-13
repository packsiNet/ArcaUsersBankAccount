using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SafeTunnels.Account.Data;
using SafeTunnels.Account.Models;
using SafeTunnels.Account.Services;
using SafeTunnels.Account.Validation;
namespace SafeTunnels.Account.Pages.Admin;
public sealed class DashboardModel(ApplicationDbContext db, EncryptionService encryption, SignInManager<IdentityUser> signIn, ILogger<DashboardModel> logger, StoredIbanInquiryService inquiry) : PageModel
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
    public IbanInquiryResult? StoredInquiry(BankAccount bank) => inquiry.Read(bank);
    public async Task<IActionResult> OnPostInquireAsync(Guid applicantId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await inquiry.InquireApplicantAsync(applicantId, ct);
        return result is null ? NotFound() : new JsonResult(result);
    }
    public async Task<IActionResult> OnPostLogoutAsync() { await signIn.SignOutAsync(); return RedirectToPage("/Admin/Login"); }
    // One row per person: name, mobile, then a bank-name/IBAN column pair per account. The header row comes first.
    private async Task<List<string[]>> ExportRowsAsync(bool masked, CancellationToken ct)
    {
        var applicants = await Query().Include(x => x.BankAccounts).OrderBy(x => x.CreatedAt).Take(10000).ToListAsync(ct);
        var rows = applicants.Select(a => new[] { a.FirstName + " " + a.LastName, a.Mobile ?? "" }
            .Concat(a.BankAccounts.OrderBy(b => b.CreatedAt).SelectMany(b => new[] { b.BankName, masked ? Mask(b) : encryption.Decrypt(b.IBAN, EncryptionService.Purpose(b.Id, "iban")) })).ToArray()).ToList();
        var header = new[] { "نام و نام خانوادگی", "شماره همراه" }
            .Concat(Enumerable.Range(0, rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => (r.Length - 2) / 2))).SelectMany(i => new[] { "نام بانک " + Ordinal(i), "شماره شبا " + Ordinal(i) })).ToArray();
        rows.Insert(0, header);
        return rows;
    }
    public async Task<IActionResult> OnPostExportAsync(CancellationToken ct)
    {
        var rows = await ExportRowsAsync(masked: true, ct);
        var csv = new StringBuilder();
        // Pad short rows so every line has the same number of columns.
        foreach (var row in rows) csv.AppendLine(string.Join(",", row.Concat(Enumerable.Repeat("", rows[0].Length - row.Length)).Select(CsvCell)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "accounts-masked.csv");
    }
    public async Task<IActionResult> OnPostExportExcelAsync(CancellationToken ct) =>
        Xlsx(await ExportRowsAsync(masked: true, ct), rightToLeft: true, "accounts-masked.xlsx");
    public async Task<IActionResult> OnPostExportFullExcelAsync(CancellationToken ct)
    {
        // Unmasked report for administrators: full name, mobile and the complete decrypted IBAN.
        var rows = await ExportRowsAsync(masked: false, ct);
        logger.LogWarning("Unmasked account export of {Count} rows by {Admin}", rows.Count - 1, User.Identity?.Name);
        Response.Headers.CacheControl = "no-store";
        return Xlsx(rows, rightToLeft: true, "accounts-full.xlsx");
    }
    private static readonly string[] Ordinals = ["اول", "دوم", "سوم", "چهارم", "پنجم", "ششم", "هفتم", "هشتم", "نهم", "دهم"];
    private static string Ordinal(int index) => index < Ordinals.Length ? Ordinals[index] : (index + 1).ToString();
    private FileContentResult Xlsx(IEnumerable<string[]> rows, bool rightToLeft, string fileName)
    {
        // Minimal single-sheet workbook; every cell is an inline string, so nothing is evaluated as a formula.
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (rightToLeft) sheet.Append("<sheetViews><sheetView rightToLeft=\"1\" workbookViewId=\"0\"/></sheetViews>");
        sheet.Append("<sheetData>");
        foreach (var row in rows)
            sheet.Append("<row>").AppendJoin("", row.Select(XlsxCell)).Append("</row>");
        sheet.Append("</sheetData></worksheet>");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string xml) { using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); w.Write(xml); }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Accounts\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
    public static string XlsxCell(string s) =>
        "<c t=\"inlineStr\"><is><t xml:space=\"preserve\">" + SecurityElement.Escape(new string(s.Where(XmlConvert.IsXmlChar).ToArray())) + "</t></is></c>";
    public static string CsvCell(string s)
    {
        if (s.TrimStart().StartsWith('=') || s.TrimStart().StartsWith('+') || s.TrimStart().StartsWith('-') || s.TrimStart().StartsWith('@')) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
