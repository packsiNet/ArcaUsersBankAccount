using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SafeTunnels.Account.Data;
using SafeTunnels.Account.Models;
using SafeTunnels.Account.Services;

namespace SafeTunnels.Account.Diagnostics;

internal static class StoredInquiryTests
{
    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        if (db.Database.GetDbConnection().Database != "safetunnels_test")
            throw new InvalidOperationException("Inquiry storage tests require safetunnels_test.");
        var encryption = scope.ServiceProvider.GetRequiredService<EncryptionService>();
        var applicant = new Applicant { FirstName = "آزمایش", LastName = "استعلام", IdempotencyKey = Guid.NewGuid(), TrackingCode = Guid.NewGuid().ToString("N").ToUpperInvariant() };
        BankAccount AddBank(string iban) {
            var bank = new BankAccount { BankName = "بانک ساختگی", ApplicantId = applicant.Id };
            bank.IBAN = encryption.Encrypt(iban, EncryptionService.Purpose(bank.Id, "iban"));
            applicant.BankAccounts.Add(bank);
            return bank;
        }
        var iban1 = "IR27" + "999" + new string('0', 19);
        const string iban2 = "IR820540102680020817909002";
        var first = AddBank(iban1);
        var second = AddBank(iban2);
        db.Applicants.Add(applicant);
        await db.SaveChangesAsync();
        var handler = new StubHandler { FailedIban = iban2 };
        using var http = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ApiIr:Token"] = "synthetic-test-token" }).Build();
        async Task<ApplicantInquiry> Call() {
            using var requestScope = services.CreateScope();
            var requestDb = requestScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var service = new StoredIbanInquiryService(requestDb, encryption, new IbanInquiryService(http, config));
            return (await service.InquireApplicantAsync(applicant.Id, CancellationToken.None))!;
        }
        void Check(bool condition) { if (!condition) throw new Exception("Stored inquiry integration test failed."); }
        try {
            var result = await Call();
            Check(result.Accounts.Count == 2 && !result.Complete && handler.Calls[iban1] == 1 && handler.Calls[iban2] == 1);
            var stored = await db.BankAccounts.AsNoTracking().SingleAsync(b => b.Id == first.Id);
            Check(stored.InquiryCompletedAt != null && stored.InquiryResult!.StartsWith("v1:") && !stored.InquiryResult.Contains("آزمایش"));
            Check((await db.BankAccounts.AsNoTracking().SingleAsync(b => b.Id == second.Id)).InquiryResult is null);
            handler.FailedIban = null;
            var concurrent = await Task.WhenAll(Call(), Call(), Call());
            Check(concurrent.All(r => r.Complete) && handler.Calls[iban1] == 1 && handler.Calls[iban2] == 2);
            Check((await Call()).Complete && handler.Calls.Values.Sum() == 3);
            Check((await db.BankAccounts.AsNoTracking().SingleAsync(b => b.Id == second.Id)).InquiryCompletedAt != null);
            var third = AddBank(iban1);
            db.BankAccounts.Add(third);
            await db.SaveChangesAsync();
            result = await Call();
            Check(result.Complete && result.Accounts.Count == 3 && handler.Calls[iban1] == 2 && handler.Calls[iban2] == 2);
            Check(result.Accounts.All(r => !r.Iban.Contains(iban1) && !r.Iban.Contains(iban2)));
            var mismatch = new IbanInquiryResult(true, "", "صاحب اصلی آزمایشی", "بانک ساختگی", true, false);
            foreach (var bank in new[] { first, second })
                bank.InquiryResult = encryption.Encrypt(System.Text.Json.JsonSerializer.Serialize(mismatch), EncryptionService.Purpose(bank.Id, "inquiry"));
            await db.SaveChangesAsync();
            var dashboard = ActivatorUtilities.CreateInstance<Pages.Admin.DashboardModel>(scope.ServiceProvider);
            dashboard.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = new DefaultHttpContext() };
            Check(dashboard.MismatchedOwners(applicant).SequenceEqual(new[] { "صاحب اصلی آزمایشی" }));
            Check(dashboard.ExportName(applicant) == "آزمایش استعلام (صاحب اصلی آزمایشی)");
            foreach (var export in new[] { await dashboard.OnPostExportExcelAsync(CancellationToken.None), await dashboard.OnPostExportFullExcelAsync(CancellationToken.None) }) {
                using var stream = new MemoryStream(((Microsoft.AspNetCore.Mvc.FileContentResult)export).FileContents);
                using var zip = new System.IO.Compression.ZipArchive(stream);
                using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
                Check((await reader.ReadToEndAsync()).Contains("آزمایش استعلام (صاحب اصلی آزمایشی)"));
            }
            Console.WriteLine("PASS: encrypted persistent inquiry results, all accounts, failed retries, concurrent deduplication and new accounts.");
        }
        finally { await db.Applicants.Where(a => a.Id == applicant.Id).ExecuteDeleteAsync(); }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public readonly ConcurrentDictionary<string, int> Calls = new();
        public string? FailedIban;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var document = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var iban = document.RootElement.GetProperty("iban").GetString()!;
            Calls.AddOrUpdate(iban, 1, (_, n) => n + 1);
            await Task.Delay(100, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(iban == FailedIban
                ? """{"success":false,"code":400}"""
                : """{"success":true,"code":200,"data":{"name":"آزمایش استعلام","bankName":"بانک ساختگی","active":true}}""", Encoding.UTF8, "application/json") };
        }
    }
}
