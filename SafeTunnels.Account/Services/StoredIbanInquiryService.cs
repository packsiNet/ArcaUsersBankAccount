using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SafeTunnels.Account.Data;
using SafeTunnels.Account.Models;

namespace SafeTunnels.Account.Services;

public sealed record StoredAccountInquiry(string Iban, string RegisteredBank, IbanInquiryResult Result);
public sealed record ApplicantInquiry(List<StoredAccountInquiry> Accounts, bool Complete);

public sealed class StoredIbanInquiryService(ApplicationDbContext db, EncryptionService encryption, IbanInquiryService inquiry)
{
    public async Task<ApplicantInquiry?> InquireApplicantAsync(Guid applicantId, CancellationToken ct)
    {
        var banks = await db.BankAccounts.AsNoTracking().Where(b => b.ApplicantId == applicantId)
            .OrderBy(b => b.CreatedAt).ThenBy(b => b.Id).ToListAsync(ct);
        if (!await db.Applicants.AnyAsync(a => a.Id == applicantId, ct)) return null;
        var results = new List<StoredAccountInquiry>();
        foreach (var bank in banks) {
            var result = await InquireAsync(bank.Id, ct);
            results.Add(new(EncryptionService.Mask(encryption.Decrypt(bank.IBAN,
                EncryptionService.Purpose(bank.Id, "iban"))), bank.BankName, result));
        }
        return new(results, results.Count > 0 && results.All(r => r.Result.Success));
    }

    public IbanInquiryResult? Read(BankAccount bank) => bank.InquiryResult is null ? null :
        JsonSerializer.Deserialize<IbanInquiryResult>(encryption.Decrypt(bank.InquiryResult,
            EncryptionService.Purpose(bank.Id, "inquiry")));

    public async Task<IbanInquiryResult> InquireAsync(Guid bankId, CancellationToken ct)
    {
        // A database row lock also prevents duplicate paid requests across app instances.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var bank = await db.BankAccounts.FromSqlInterpolated(
            $"SELECT * FROM \"BankAccounts\" WHERE \"Id\" = {bankId} FOR UPDATE").SingleAsync(ct);
        // Reload after acquiring the lock, including when this context already tracked the account.
        await db.Entry(bank).ReloadAsync(ct);
        var stored = Read(bank);
        if (stored is not null) { await transaction.CommitAsync(ct); return stored; }
        var applicant = await db.Applicants.SingleAsync(a => a.Id == bank.ApplicantId, ct);
        var iban = encryption.Decrypt(bank.IBAN, EncryptionService.Purpose(bank.Id, "iban"));
        var result = await inquiry.InquireAsync(iban, applicant.FirstName + " " + applicant.LastName, ct);
        if (result.Success) {
            bank.InquiryResult = encryption.Encrypt(JsonSerializer.Serialize(result), EncryptionService.Purpose(bank.Id, "inquiry"));
            bank.InquiryCompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return result;
    }
}
