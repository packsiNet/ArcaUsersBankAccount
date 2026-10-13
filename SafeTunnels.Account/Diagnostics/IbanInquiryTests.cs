using System.Net;
using System.Text;
using SafeTunnels.Account.Services;

namespace SafeTunnels.Account.Diagnostics;

internal static class IbanInquiryTests
{
    public static void Run()
    {
        const string iban = "IR820540102680020817909002";
        var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ApiIr:Token"] = "Bearer test-token"
        }).Build();
        var service = new IbanInquiryService(http, config);
        IbanInquiryResult Call(string name = "علی رضایی") => service.InquireAsync(iban, name, CancellationToken.None).GetAwaiter().GetResult();
        void Check(bool value) { if (!value) throw new Exception("IBAN inquiry test failed."); }
        handler.Body = """{"success":true,"code":"200","data":{"name":"علي رضايي","bankName":"پارسیان","active":false}}""";
        var result = Call();
        Check(result.Success && result.NameMatches == true && result.Active == false && result.BankName == "پارسیان");
        Check(!result.Correct && result.Status == "اطلاعات اشتباه");
        Check(new IbanInquiryResult(true, "", NameMatches: true, Active: true).Status == "صحت اطلاعات");
        Check(new IbanInquiryResult(true, "", NameMatches: false, Active: true).Status == "اطلاعات اشتباه");
        Check(new IbanInquiryResult(false, "").Status == "استعلام نشده");
        Check(handler.Url == "https://s.api.ir/api/sw1/IbanInfo" && handler.Method == HttpMethod.Post
            && handler.Authorization == "Bearer test-token" && handler.RequestBody.Contains(iban));
        Check(Call("نام دیگر").NameMatches == false);
        handler.Body = """{"success":false,"code":400,"message":"private provider details","data":{"name":"علی رضایی"}}""";
        result = Call();
        Check(!result.Success && result.OwnerName is null && !result.Message.Contains("private"));
        handler.Body = """{"success":true,"data":{"name":""}}""";
        Check(!Call().Success);
        handler.Body = "not json";
        Check(!Call().Success);
        handler.Status = HttpStatusCode.Unauthorized;
        Check(!Call().Success);
        handler.Status = HttpStatusCode.OK;
        handler.Failure = new HttpRequestException("secret");
        Check(!Call().Success);
        handler.Failure = new TaskCanceledException();
        Check(!Call().Success);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { service.InquireAsync(iban, "name", cts.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation."); }
        catch (OperationCanceledException) { }
        var before = handler.Calls;
        config["ApiIr:Token"] = "";
        Check(!Call().Success && before == handler.Calls);
        config["ApiIr:Token"] = "test-token";
        Check(!service.InquireAsync("invalid", "name", CancellationToken.None).GetAwaiter().GetResult().Success && before == handler.Calls);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Body = "";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Exception? Failure;
        public int Calls;
        public string? Url, Authorization;
        public string RequestBody = "";
        public HttpMethod? Method;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            ct.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            Url = request.RequestUri?.ToString();
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}
