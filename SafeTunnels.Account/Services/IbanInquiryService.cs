using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SafeTunnels.Account.Validation;

namespace SafeTunnels.Account.Services;

public sealed record IbanInquiryResult(bool Success, string Message, string? OwnerName = null,
    string? BankName = null, bool? Active = null, bool? NameMatches = null);

// API contract: https://github.com/api-ir/dotnet-sdk/blob/main/Services/IbanInfoService.cs
public sealed class IbanInquiryService(HttpClient http, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async Task<IbanInquiryResult> InquireAsync(string iban, string registeredName, CancellationToken ct)
    {
        var token = (configuration["ApiIr:Token"] ?? configuration["APIIR_TOKEN"])?.Trim();
        if (token?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true) token = token[7..].Trim();
        if (string.IsNullOrWhiteSpace(token)) return new(false, "کلید سرویس استعلام در سرور تنظیم نشده است.");
        if (!BankingValidation.ValidIban(iban)) return new(false, "شماره شبای ثبت‌شده معتبر نیست.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://s.api.ir/api/sw1/IbanInfo");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(new { iban });
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new(false, response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "کلید دسترسی سرویس استعلام معتبر نیست." : "سرویس استعلام در دسترس نیست یا درخواست را نپذیرفت.");
            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(JsonOptions, ct);
            if (envelope is not { Success: true, Data: not null })
                return new(false, "استعلام ناموفق بود؛ اعتبار و مجوز سرویس را در پنل api.ir بررسی کنید.");
            var data = envelope.Data;
            if (string.IsNullOrWhiteSpace(data.Name)) return new(false, "نام صاحب شبا در پاسخ سرویس موجود نیست.");
            return new(true, "استعلام انجام شد.", data.Name, data.BankName, data.Active,
                NormalizeName(data.Name) == NormalizeName(registeredName));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, "مهلت پاسخ سرویس تمام شد؛ دوباره تلاش کنید."); }
        catch (HttpRequestException) { return new(false, "ارتباط با سرویس استعلام برقرار نشد."); }
        catch (JsonException) { return new(false, "پاسخ سرویس استعلام قابل خواندن نیست."); }
    }

    private static string NormalizeName(string name) => string.Concat(BankingValidation.Name(name).Replace('ي', 'ی').Replace('ك', 'ک')
        .Where(c => !char.IsWhiteSpace(c) && c != '\u200c' && c != '\u200d' && c != '\u0640'
            && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark));
    private sealed record Envelope(bool Success, int Code, IbanData? Data);
    private sealed record IbanData(string? Name, string? BankName, bool? Active);
}
