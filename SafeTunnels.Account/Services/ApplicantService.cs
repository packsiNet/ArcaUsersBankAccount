using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SafeTunnels.Account.Data;
using SafeTunnels.Account.Models;
using SafeTunnels.Account.Validation;
namespace SafeTunnels.Account.Services;
public sealed class ApplicantService(ApplicationDbContext db, EncryptionService encryption)
{
    public async Task<string> RegisterAsync(RegistrationInput input, Guid key, CancellationToken ct)
    {
        input.Normalize();
        Validator.ValidateObject(input, new ValidationContext(input), true);
        foreach (var a in input.Accounts) Validator.ValidateObject(a, new ValidationContext(a), true);
        var previous = await db.Applicants.AsNoTracking().Where(x => x.IdempotencyKey == key).Select(x => x.TrackingCode).SingleOrDefaultAsync(ct);
        if (previous is not null) return previous;
        var applicant = new Applicant { IdempotencyKey = key, TrackingCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), FirstName = input.FirstName, LastName = input.LastName, Mobile = input.Mobile };
        foreach (var a in input.Accounts)
        {
            var bank = new BankAccount { BankName = a.BankName == "سایر بانک‌ها" ? a.OtherBankName! : a.BankName };
            bank.IBAN = encryption.Encrypt(a.IBAN, EncryptionService.Purpose(bank.Id, "iban"));
            applicant.BankAccounts.Add(bank);
        }
        // One SaveChanges is an atomic relational transaction for applicant and all accounts.
        db.Applicants.Add(applicant);
        try { await db.SaveChangesAsync(ct); return applicant.TrackingCode; }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var existing = await db.Applicants.AsNoTracking().Where(x => x.IdempotencyKey == key).Select(x => x.TrackingCode).SingleOrDefaultAsync(ct);
            if (existing is null) throw;
            return existing;
        }
    }
}
