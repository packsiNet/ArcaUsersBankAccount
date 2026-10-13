using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SafeTunnels.Account.Data;
using SafeTunnels.Account.Services;
if (args.Contains("--self-test")) { SafeTunnels.Account.Diagnostics.SelfTests.Run(); return; }
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("Npgsql", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
builder.WebHost.UseSockets(o => o.Backlog = 2048);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 32768);
builder.Services.AddDbContextPool<ApplicationDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database configuration is required.")));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o => {
    o.Password.RequiredLength = 14; o.Password.RequireNonAlphanumeric = true;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); o.Lockout.MaxFailedAccessAttempts = 5;
    o.User.RequireUniqueEmail = true;
}).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => {
    o.LoginPath = "/admin/login"; o.AccessDeniedPath = "/admin/login";
    o.Cookie.Name = "SafeTunnels.Admin"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(20); o.SlidingExpiration = false;
});
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireRole("Admin")));
builder.Services.AddRazorPages(o => {
    o.Conventions.AuthorizeFolder("/Admin", "Admin");
    o.Conventions.AllowAnonymousToPage("/Admin/Login");
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.MaxModelBindingCollectionSize = 10);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => { o.ValueCountLimit = 100; o.ValueLengthLimit = 2048; o.MultipartBodyLengthLimit = 32768; });
builder.Services.AddAntiforgery(o => {
    o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? (builder.Environment.IsDevelopment() ? Path.Combine(builder.Environment.ContentRootPath, "..", ".local", "data-protection") : throw new InvalidOperationException("External DataProtection key path is required."));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName("SafeTunnels.Account");
builder.Services.Configure<ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1; o.KnownProxies.Add(IPAddress.Loopback); o.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => {
        if (!HttpMethods.IsPost(ctx.Request.Method)) return RateLimitPartition.GetNoLimiter("read");
        bool login = ctx.Request.Path.StartsWithSegments("/admin/login");
        return RateLimitPartition.GetFixedWindowLimiter((login ? "login:" : "post:") + ctx.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions { PermitLimit = login ? 10 : 1000, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    o.OnRejected = async (ctx, ct) => { ctx.HttpContext.Response.Headers.RetryAfter = "60"; await ctx.HttpContext.Response.WriteAsync("تعداد درخواست‌ها زیاد است. یک دقیقه دیگر تلاش کنید.", ct); };
});
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<ApplicantService>();
builder.Services.AddScoped<StoredIbanInquiryService>();
builder.Services.AddHttpClient<IbanInquiryService>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddResponseCompression(o => o.EnableForHttps = false);
builder.Services.AddHttpsRedirection(o => o.HttpsPort = 443);
var app = builder.Build();
if (args.Contains("--inquiry-storage-test")) {
    await SafeTunnels.Account.Diagnostics.StoredInquiryTests.RunAsync(app.Services);
    return;
}
if (args.Contains("--migrate") || args.Contains("--create-admin") || args.Contains("--ensure-admin"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (args.Contains("--migrate")) {
        await db.Database.MigrateAsync();
        var fingerprinted = await scope.ServiceProvider.GetRequiredService<ApplicantService>().BackfillFingerprintsAsync(CancellationToken.None);
        if (fingerprinted > 0) Console.WriteLine($"Fingerprinted {fingerprinted} existing bank accounts.");
    }
    if (args.Contains("--create-admin") || args.Contains("--ensure-admin")) {
        var email = builder.Configuration["Bootstrap:Email"] ?? throw new InvalidOperationException("Bootstrap email is required.");
        var password = builder.Configuration["Bootstrap:Password"] ?? throw new InvalidOperationException("Bootstrap password is required.");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (await users.FindByEmailAsync(email) is not null) {
            if (args.Contains("--ensure-admin")) { Console.WriteLine("Administrator already exists; credentials preserved."); return; }
            throw new InvalidOperationException("Administrator already exists.");
        }
        if (!await roles.RoleExistsAsync("Admin")) {
            var roleResult = await roles.CreateAsync(new IdentityRole("Admin"));
            if (!roleResult.Succeeded) throw new InvalidOperationException("Cannot create administrator role.");
        }
        var user = new IdentityUser { Email = email, UserName = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException("Cannot create administrator: " + string.Join(",", result.Errors.Select(x => x.Code)));
        var assigned = await users.AddToRoleAsync(user, "Admin");
        if (!assigned.Succeeded) { await users.DeleteAsync(user); throw new InvalidOperationException("Cannot assign administrator role."); }
        Console.WriteLine("Administrator created. Remove Bootstrap credentials from the environment.");
    }
    return;
}
_ = app.Services.GetRequiredService<EncryptionService>();
app.UseForwardedHeaders();
app.UseExceptionHandler("/Error");
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (ctx, next) => {
    ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
    if (!ctx.Request.Path.StartsWithSegments("/css") && !ctx.Request.Path.StartsWithSegments("/js") && !ctx.Request.Path.StartsWithSegments("/fonts")) ctx.Response.Headers.CacheControl = "no-store, max-age=0";
    if (HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.Path == "/" && ctx.Request.HasFormContentType) {
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var indexed = form.Keys.Select(k => System.Text.RegularExpressions.Regex.Match(k, @"^Input\.Accounts\[(\d+)\]\."))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToArray();
        if (indexed.Length > 10 || indexed.Any(i => !int.TryParse(i, out var n) || n >= 10) || form["Input.Accounts.Index"].Count > 10) {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("حداکثر ده حساب بانکی مجاز است.");
            return;
        }
    }
    await next();
});
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public, max-age=86400" });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health/ready", async (ApplicationDbContext db, HttpContext context) => {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(3));
    try {
        await db.Applicants.Select(a => a.Id).Take(1).ToArrayAsync(timeout.Token);
        return Results.Text("ready");
    } catch { return Results.StatusCode(503); }
}).AllowAnonymous();
app.MapRazorPages();
app.Run();





