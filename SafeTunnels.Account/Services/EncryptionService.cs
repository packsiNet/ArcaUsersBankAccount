using System.Security.Cryptography;
using System.Text;
namespace SafeTunnels.Account.Services;
public sealed class EncryptionService
{
    private readonly Dictionary<string, byte[]> keys;
    private readonly string active;
    public EncryptionService(IConfiguration config)
    {
        active = config["Encryption:ActiveKeyId"] ?? throw new InvalidOperationException("Encryption key configuration is required.");
        if (active.Length > 32 || active.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new InvalidOperationException("Invalid encryption key identifier.");
        keys = config.GetSection("Encryption:Keys").GetChildren().ToDictionary(x => x.Key, x => Convert.FromBase64String(x.Value ?? ""));
        if (!keys.ContainsKey(active) || keys.Values.Any(k => k.Length != 32) || keys.Keys.Any(k => k.Length is < 1 or > 32 || k.Any(c => !char.IsAsciiLetterOrDigit(c)))) throw new InvalidOperationException("Each encryption key requires a valid identifier and 32 bytes.");
    }
    public string Encrypt(string value, string purpose)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], plain = Encoding.UTF8.GetBytes(value), cipher = new byte[plain.Length];
        try {
            using var aes = new AesGcm(keys[active], 16);
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));
            return $"v1:{active}:{Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray())}";
        } finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public string Decrypt(string value, string purpose)
    {
        var parts = value.Split(':');
        if (parts.Length != 3 || parts[0] != "v1" || !keys.TryGetValue(parts[1], out var key)) throw new CryptographicException("Unavailable encryption key.");
        var blob = Convert.FromBase64String(parts[2]);
        if (blob.Length < 28) throw new CryptographicException("Invalid encrypted record.");
        var plain = new byte[blob.Length - 28];
        try {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(28), blob.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(purpose));
            return Encoding.UTF8.GetString(plain);
        } finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static string Purpose(Guid account, string field) => $"SafeTunnels:{account:N}:{field}";
    public static string Mask(string value) => "•••• " + (value.Length > 4 ? value[^Math.Min(4, value.Length)..] : "••••");
}
