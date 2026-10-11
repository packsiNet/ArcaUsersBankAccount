using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
namespace SafeTunnels.Account.Validation;
public sealed class RegistrationInput : IValidatableObject
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Mobile { get; set; }
    public List<BankInput> Accounts { get; set; } = [new()];
    public void Normalize()
    {
        FirstName = BankingValidation.Name(FirstName);
        LastName = BankingValidation.Name(LastName);
        Mobile = BankingValidation.Mobile(Mobile);
        foreach (var a in Accounts) a.Normalize();
    }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(FirstName) || FirstName.Length > 100) yield return new("نام الزامی است و باید حداکثر ۱۰۰ نویسه باشد.", [nameof(FirstName)]);
        if (string.IsNullOrWhiteSpace(LastName) || LastName.Length > 100) yield return new("نام خانوادگی الزامی است و باید حداکثر ۱۰۰ نویسه باشد.", [nameof(LastName)]);
        if (Mobile is not null && !Regex.IsMatch(Mobile, @"^09[0-9]{9}$")) yield return new("شماره همراه معتبر وارد کنید.", [nameof(Mobile)]);
        if (Accounts.Count is < 1 or > 10) yield return new("ثبت یک تا ده حساب بانکی الزامی است.", [nameof(Accounts)]);
        for (var i = 1; i < Accounts.Count; i++)
            if (Accounts[i].IBAN.Length > 2 && Accounts.Take(i).Any(a => a.IBAN == Accounts[i].IBAN)) yield return new("این شماره شبا در همین فرم تکراری است.", [$"{nameof(Accounts)}[{i}].{nameof(BankInput.IBAN)}"]);
    }
}
public sealed class BankInput : IValidatableObject
{
    public string BankName { get; set; } = "";
    public string? OtherBankName { get; set; }
    public string IBAN { get; set; } = "";
    public void Normalize()
    {
        BankName = (BankName ?? "").Trim();
        OtherBankName = BankName == "سایر بانک‌ها" ? BankingValidation.Name(OtherBankName) : null;
        IBAN = BankingValidation.Iban(IBAN);
    }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!BankCatalog.Names.Contains(BankName)) yield return new("بانک را از فهرست انتخاب کنید.", [nameof(BankName)]);
        if (BankName == "سایر بانک‌ها" && (string.IsNullOrWhiteSpace(OtherBankName) || OtherBankName.Length > 100)) yield return new("نام بانک مورد نظر را وارد کنید (حداکثر ۱۰۰ نویسه).", [nameof(OtherBankName)]);
        if (!BankingValidation.ValidIban(IBAN)) yield return new("شماره شبای معتبر ایران وارد کنید.", [nameof(IBAN)]);
    }
}
public static class BankingValidation
{
    public static string Digits(string? s) => string.Concat((s ?? "").Select(c => c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c));
    public static string Number(string? s) => Regex.Replace(Digits(s), @"[\s-]", "");
    public static string Name(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ");
    public static string? Mobile(string? s)
    {
        var n = Number(s);
        if (n.Length == 0) return null;
        return n.StartsWith("+98", StringComparison.Ordinal) ? "0" + n[3..] : n;
    }
    public static string Iban(string? s)
    {
        var n = Number(s).ToUpperInvariant();
        return n.StartsWith("IR", StringComparison.Ordinal) ? n : "IR" + n;
    }
    public static bool ValidIban(string s)
    {
        if (!Regex.IsMatch(s, @"^IR[0-9]{24}$")) return false;
        int remainder = 0;
        foreach (var c in s[4..] + "1827" + s[2..4]) remainder = (remainder * 10 + c - '0') % 97;
        return remainder == 1;
    }
}
