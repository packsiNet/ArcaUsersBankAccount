using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using SafeTunnels.Account.Services;
using SafeTunnels.Account.Validation;
namespace SafeTunnels.Account.Diagnostics;
public static class SelfTests
{
    public static void Run()
    {
        var tests = new List<(string, Action)> {
            ("Persian/Arabic digit normalization", () => Equal("00123456789", BankingValidation.Number("۰۰١٢٣٤٥٦٧٨٩"))),
            ("Leading zero preservation", () => Equal("00012", BankingValidation.Number("00 0-12"))),
            ("Name whitespace", () => Equal("علی رضا", BankingValidation.Name("  علی   رضا  "))),
            ("International mobile", () => Equal("09123456789", BankingValidation.Mobile("+۹۸۹۱۲۳۴۵۶۷۸۹"))),
            ("Empty mobile", () => Equal(null, BankingValidation.Mobile("  "))),
            ("IBAN checksum/prefix", () => { Assert(BankingValidation.ValidIban(BankingValidation.Iban("27" + "999" + new string('0',19)))); Assert(!BankingValidation.ValidIban("IR00000000000000000000000000")); }),
            ("Account count bounds", () => { var i = new RegistrationInput { FirstName="Test",LastName="Test",Accounts=[] };Assert(!Validator.TryValidateObject(i,new ValidationContext(i),[],true));i.Accounts=Enumerable.Range(0,11).Select(_=>new BankInput()).ToList();Assert(!Validator.TryValidateObject(i,new ValidationContext(i),[],true)); }),
            ("Authenticated encryption and key rotation", EncryptionTest),
            ("IBAN inquiry contract, name comparison and failures", IbanInquiryTests.Run),
            ("CSV formula neutralization", () => Assert(Pages.Admin.DashboardModel.CsvCell(" =cmd").StartsWith("\"'"))),
            ("Short values are fully masked", () => { Assert(!EncryptionService.Mask("123").Contains("123")); })
        };
        foreach (var (name, action) in tests) { action(); Console.WriteLine("PASS: " + name); }
        Console.WriteLine($"{tests.Count} self-tests passed.");
    }
    private static void EncryptionTest()
    {
        var keys = new Dictionary<string, string?> { ["Encryption:ActiveKeyId"]="k1",["Encryption:Keys:k1"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),["Encryption:Keys:k2"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var e = new EncryptionService(new ConfigurationBuilder().AddInMemoryCollection(keys).Build());
        var purpose = EncryptionService.Purpose(Guid.NewGuid(), "iban");
        var c=e.Encrypt("IR27999000000000000000000000",purpose);
        Equal("IR27999000000000000000000000",e.Decrypt(c,purpose));Assert(c!=e.Encrypt("IR27999000000000000000000000",purpose));
        try { e.Decrypt(c,purpose+"other"); throw new Exception("Expected authentication failure"); } catch(CryptographicException) { }
        var parts=c.Split(':');var blob=Convert.FromBase64String(parts[2]);blob[^1]^=1;parts[2]=Convert.ToBase64String(blob);
        try { e.Decrypt(string.Join(':',parts),purpose); throw new Exception("Expected tamper rejection"); } catch(CryptographicException) { }
        keys["Encryption:ActiveKeyId"]="k2";
        var rotated=new EncryptionService(new ConfigurationBuilder().AddInMemoryCollection(keys).Build());
        Equal("IR27999000000000000000000000",rotated.Decrypt(c,purpose));Assert(rotated.Encrypt("new",purpose).StartsWith("v1:k2:"));
    }
    private static void Assert(bool value) { if(!value) throw new Exception("Self-test assertion failed."); }
    private static void Equal(string? a,string? b) => Assert(a==b);
}
