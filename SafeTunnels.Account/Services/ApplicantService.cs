using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SafeTunnels.Account.Data;
using SafeTunnels.Account.Models;
using SafeTunnels.Account.Validation;
namespace SafeTunnels.Account.Services;
// Raised when submitted IBANs are already registered; Accounts holds the indexes of the offending form rows.
public sealed class DuplicateIbanException(IReadOnlyList<int> accounts) : Exception("Duplicate IBAN.")
{
    public IReadOnlyList<int> Accounts { get; } = accounts;
}
public sealed class AccountLimitException() : Exception("Account limit exceeded.");
public sealed class ApplicantService(ApplicationDbContext db, EncryptionService encryption)
{
    public const int MaxAccounts = 10;
    public async Task<string> RegisterAsync(RegistrationInput input, Guid key, CancellationToken ct)
    {
        input.Normalize();
        Validator.ValidateObject(input, new ValidationContext(input), true);
        foreach (var a in input.Accounts) Validator.ValidateObject(a, new ValidationContext(a), true);
        var previous = await db.Applicants.AsNoTracking().Where(x => x.IdempotencyKey == key).Select(x => x.TrackingCode).SingleOrDefaultAsync(ct);
        if (previous is not null) return previous;
        var candidates = input.Accounts.Select(a => encryption.Fingerprints(a.IBAN)).ToList();
        var lookup = candidates.SelectMany(x => x).ToList();
        var matches = await db.BankAccounts.AsNoTracking().Where(x => x.IbanHash != null && lookup.Contains(x.IbanHash)).Select(x => new { x.ApplicantId, x.IbanHash }).ToListAsync(ct);
        var known = matches.Select(x => x.IbanHash!).ToHashSet();
        var duplicates = Enumerable.Range(0, input.Accounts.Count).Where(i => candidates[i].Any(known.Contains)).ToList();
        Applicant applicant;
        if (duplicates.Count == 0)
        {
            applicant = new Applicant { IdempotencyKey = key, TrackingCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), FirstName = input.FirstName, LastName = input.LastName, Mobile = input.Mobile };
            db.Applicants.Add(applicant);
        }
        else
        {
            // A known IBAN only extends its owner's record: same person, and at least one new account to add.
            var owners = matches.Select(x => x.ApplicantId).Distinct().ToList();
            if (duplicates.Count == input.Accounts.Count || owners.Count != 1) throw new DuplicateIbanException(duplicates);
            applicant = await db.Applicants.Include(x => x.BankAccounts).SingleAsync(x => x.Id == owners[0], ct);
            if (applicant.FirstName != input.FirstName || applicant.LastName != input.LastName) throw new DuplicateIbanException(duplicates);
            if (applicant.BankAccounts.Count + input.Accounts.Count - duplicates.Count > MaxAccounts) throw new AccountLimitException();
            applicant.IdempotencyKey = key;
            applicant.Mobile = input.Mobile ?? applicant.Mobile;
            applicant.UpdatedAt = DateTime.UtcNow;
        }
        foreach (var a in input.Accounts.Where((_, i) => !duplicates.Contains(i)))
        {
            var bank = new BankAccount { ApplicantId = applicant.Id, BankName = a.BankName == "سایر بانک‌ها" ? a.OtherBankName! : a.BankName, IbanHash = encryption.Fingerprint(a.IBAN) };
            bank.IBAN = encryption.Encrypt(a.IBAN, EncryptionService.Purpose(bank.Id, "iban"));
            db.BankAccounts.Add(bank);
        }
        // One SaveChanges is an atomic relational transaction for applicant and all accounts.
        try { await db.SaveChangesAsync(ct); return applicant.TrackingCode; }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var existing = await db.Applicants.AsNoTracking().Where(x => x.IdempotencyKey == key).Select(x => x.TrackingCode).SingleOrDefaultAsync(ct);
            // Not a replay of this submission, so another request registered one of these IBANs first.
            if (existing is null) throw new DuplicateIbanException([]);
            return existing;
        }
    }
    // Fingerprints accounts stored before IbanHash existed. Legacy repeats of an IBAN stay without one.
    public async Task<int> BackfillFingerprintsAsync(CancellationToken ct)
    {
        var known = (await db.BankAccounts.AsNoTracking().Where(x => x.IbanHash != null).Select(x => x.IbanHash!).ToListAsync(ct)).ToHashSet();
        var pending = await db.BankAccounts.Where(x => x.IbanHash == null).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        var updated = 0;
        foreach (var bank in pending)
        {
            var iban = encryption.Decrypt(bank.IBAN, EncryptionService.Purpose(bank.Id, "iban"));
            if (encryption.Fingerprints(iban).Any(known.Contains)) continue;
            bank.IbanHash = encryption.Fingerprint(iban);
            known.Add(bank.IbanHash);
            updated++;
        }
        await db.SaveChangesAsync(ct);
        return updated;
    }
}
