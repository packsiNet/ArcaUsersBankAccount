# سامانه ثبت اطلاعات بانکی SafeTunnels

یک پروژه ASP.NET Core 10 / Razor Pages با PostgreSQL، EF Core، Identity، رابط فارسی RTL، Tailwind کامپایل‌شده و JavaScript ساده. دامنه مقصد: `account.safetunnels.net`.

کد برنامه در [SafeTunnels.Account](SafeTunnels.Account) است. راهنمای امنیت: [deploy/SECURITY.md](deploy/SECURITY.md). راهنمای استقرار: [deploy/DEPLOYMENT.md](deploy/DEPLOYMENT.md).

## اجرای معمولی محلی

پیش‌نیاز: .NET SDK 10، PostgreSQL 17 یا جدیدتر و Node.js 22 یا جدیدتر برای بازسازی فایل‌های ظاهری. فایل‌های کامپایل‌شده CSS/JS و فونت در پروژه موجودند و `dotnet publish` به Node نیاز ندارد.

از ریشه پروژه:

```powershell
npm.cmd ci --cache .local/npm-cache
npm.cmd run build
dotnet restore SafeTunnels.Account
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ConnectionStrings__DefaultConnection='Host=127.0.0.1;Database=safetunnels;Username=YOUR_USER;Password=YOUR_PASSWORD;Maximum Pool Size=80;Include Error Detail=false;Log Parameters=false'
$env:Encryption__ActiveKeyId='k1'
$env:Encryption__Keys__k1=[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet run --project SafeTunnels.Account --no-launch-profile -- --migrate
```

کلید را در محل امن نگه دارید؛ تولید کلید جدید در اجرای بعدی، داده‌های قبلی را بازیابی نمی‌کند. برای توسعه می‌توانید مقادیر را در `SafeTunnels.Account/appsettings.Local.json` با دسترسی محدود نگه دارید؛ این فایل در Git و Publish قرار نمی‌گیرد. نام تنظیمات در [environment.example](deploy/environment.example) آمده است.

ساخت مدیر اولیه با رمز تصادفی شخصی (حداقل ۱۴ نویسه شامل حروف کوچک، بزرگ، رقم و علامت):

```powershell
$env:Bootstrap__Email='YOUR_ADMIN_EMAIL'
$adminSecret=Read-Host 'Admin password' -AsSecureString
$env:Bootstrap__Password=[System.Net.NetworkCredential]::new('', $adminSecret).Password
dotnet run --project SafeTunnels.Account --no-launch-profile -- --create-admin
Remove-Item Env:Bootstrap__Password
Remove-Item Env:Bootstrap__Email
dotnet run --project SafeTunnels.Account --no-launch-profile --urls http://127.0.0.1:5080
```

فرم: `http://127.0.0.1:5080/`؛ پنل: `http://127.0.0.1:5080/admin`.

## تست‌های قابل اجرا

تست مستقل اعتبارسنجی، رمزنگاری، دستکاری ciphertext و گردش کلید:

```powershell
dotnet run --project SafeTunnels.Account --no-launch-profile -- --self-test
```

تست مرورگر و تراکنش از دیتابیس مستقل با نام دقیق `safetunnels_test` استفاده می‌کند. تست rollback یک trigger موقت روی جدول حساب‌ها می‌سازد؛ هرگز آن را روی دیتابیس کاربران اجرا نکنید. برای محیط Windows بدون PostgreSQL نصب‌شده، اسکریپت زیر بسته PostgreSQL آزمایشی Zonky را در `.local` دریافت می‌کند. احراز هویت trust آن فقط برای داده ساختگی محلی است.

```powershell
npm.cmd ci --cache .local/npm-cache
python tools/prepare-test-postgres.py
./.local/postgres/bin/initdb.exe -D .local/pgdata -U postgres --auth=trust --encoding=UTF8 --locale=C
# در ترمینال جداگانه اجرا شود:
./.local/postgres/bin/postgres.exe -D D:/GetAccounts/.local/pgdata -h 127.0.0.1 -p 55432
```

در ترمینال اصلی:

```powershell
node tools/setup-local.mjs
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project SafeTunnels.Account --no-launch-profile -- --migrate --create-admin
# برنامه را در ترمینال جداگانه باز نگه دارید:
dotnet run --project SafeTunnels.Account --no-launch-profile --urls http://127.0.0.1:5080
$env:PLAYWRIGHT_BROWSERS_PATH="$PWD/.local/browsers"
npx.cmd playwright install chromium
npm.cmd test
```

اسکریپت setup اطلاعات ورود تصادفی را در `.local/test-settings.json` نگه می‌دارد؛ رمز پیش‌فرض ثابت وجود ندارد. Bootstrap را پس از ساخت مدیر از `appsettings.Local.json` حذف کنید. برای دیتابیس نصب‌شده، یک دیتابیس مستقل با همان نام و تنظیمات متناظر بسازید؛ متغیر `TEST_DATABASE_URL` باید به دیتابیس تست اشاره کند. `TEST_BASE_URL` آدرس برنامه آزمایش را تغییر می‌دهد.

برای خرابی TLS در ابزار NuGet ویندوز، دریافت رسمی بسته‌ها با `python tools/fetch-packages.py` و سپس `dotnet restore SafeTunnels.Account --source .packages -p:NuGetAudit=false` امکان‌پذیر است. این دستور بررسی آسیب‌پذیری NuGet را اجرا نمی‌کند؛ بررسی وابستگی‌ها را در محیط شبکه سالم انجام دهید.

## آزمون بار

سناریوهای `tests/load.k6.js` فقط با `CONFIRM_SYNTHETIC_DATABASE=yes` اجرا می‌شوند. آستانه‌ها اهداف تست هستند، نه ادعای عملکرد. برای تعداد ۱۰۰، ۳۰۰ و ۶۰۰ هرکدام جداگانه اجرا کنید:

```bash
mkdir -p test-results
k6 run -e BASE_URL=https://STAGING_HOST -e CONFIRM_SYNTHETIC_DATABASE=yes -e VUS=100 -e ONCE=1 tests/load.k6.js
k6 run -e BASE_URL=https://STAGING_HOST -e CONFIRM_SYNTHETIC_DATABASE=yes -e VUS=300 -e ONCE=1 tests/load.k6.js
k6 run -e BASE_URL=https://STAGING_HOST -e CONFIRM_SYNTHETIC_DATABASE=yes -e VUS=600 -e ONCE=1 tests/load.k6.js
k6 run -e BASE_URL=https://STAGING_HOST -e CONFIRM_SYNTHETIC_DATABASE=yes -e VUS=600 -e BURST=1 tests/load.k6.js
```

بین اجرای موج‌ها حداقل یک دقیقه فاصله بگذارید تا محدودیت IP مشترک به‌درستی اعمال شود. بدون `ONCE=1` سناریوی پایدار ۶۰ ثانیه‌ای اجرا می‌شود؛ برای بررسی ظرفیت خام، بار را از IPهای متعدد تولید کنید، یا تنها در staging حد نرخ ثبت را با تصمیم مشخص تغییر دهید. نرخ خطا شامل 429 نیز هست و نباید پنهان شود. در burst کاربران پس از دریافت فرم، سه ثانیه منتظر می‌مانند؛ شروع POSTها تقریباً هم‌زمان است.

هم‌زمان روی سرور هدف `pidstat -u -r -d 1`، `vmstat 1` و `iostat -xz 1` را ذخیره کنید. گزارش k6 شامل latency و error rate و نرخ `http_reqs` (RPS) است. گزارش سیستم باید CPU، RAM و I/O برنامه و PostgreSQL را دربرگیرد. تست Windows محلی جایگزین آزمون Ubuntu روی 4 vCPU/8GB نیست.

## انتشار

### استعلام نام صاحب شبا

در پنل مدیریت، دکمهٔ «استعلام» هر ردیف، شباهای همان فرد را با سرویس [IbanInfo از api.ir](https://github.com/api-ir/dotnet-sdk) استعلام می‌کند. نام صاحب شبا، بانک، وضعیت فعال بودن حساب و برابری متنی نام با نام ثبت‌شده نمایش داده می‌شوند. تفاوت حروف ی/ک عربی و فارسی، فاصله و نیم‌فاصله نادیده گرفته می‌شود؛ این مقایسه تأیید هویت نیست. نتیجه در صفحه نمایش داده می‌شود و در دیتابیس ذخیره نمی‌شود.

کلید فقط در سرور با `ApiIr__Token` (یا `APIIR_TOKEN`) تنظیم شود. در محیط محلی می‌توان از `ApiIr:Token` در فایل نادیده‌گرفته‌شدهٔ `SafeTunnels.Account/appsettings.Local.json` استفاده کرد؛ این فایل منتشر نمی‌شود. برای سرور، کلید را در فایل محیط سرویس تنظیم و سرویس را راه‌اندازی مجدد کنید. استعلام واقعی به‌ازای هر حساب هزینه دارد؛ تست‌های داخلی از پاسخ ساختگی استفاده می‌کنند. درخواست استعلام فقط برای مدیر واردشده و با POST و توکن ضد جعل پذیرفته می‌شود؛ خطاها خودکار تکرار نمی‌شوند.

```powershell
dotnet publish SafeTunnels.Account -c Release -o artifacts/publish
```

یک پروژه برنامه وجود دارد؛ تست‌ها پروژه .NET جداگانه ایجاد نمی‌کنند. مهاجرت Identity و اطلاعات ثبت‌شده در `SafeTunnels.Account/Migrations` موجود است. فرم در مرکز صفحه قرار دارد و هر حساب فقط نام بانک و شبا دارد؛ شماره کارت و شماره حساب دریافت یا ذخیره نمی‌شوند. شبا رمزنگاری می‌شود. Migration جدید `RemoveCardAndAccountNumbers` ستون‌های قبلی را حذف می‌کند. بررسی checksum اثبات مالکیت حساب نیست.

## استقرار خودکار روی Ubuntu 26.04

با push به `main` یا اجرای دستی workflow در GitHub Actions، پس از موفقیت آزمون‌ها، نصب وابستگی‌ها، تنظیم PostgreSQL و Nginx، ساخت مدیر اولیه، صدور SSL با Certbot و راه‌اندازی سرویس به‌صورت خودکار انجام می‌شود. تنظیمات `APP_URL`، `SERVER_HOST`، `SERVER_USER` و `SERVER_SSH_KEY` استفاده می‌شوند. دامنه: `https://account.zanatech.online`.

DNS باید به سرور اشاره کند و پورت‌های ۸۰ و ۴۴۳ در فایروال ارائه‌دهنده باز باشند. رمز اولیه مدیر فقط روی سرور در `/root/safetunnels-admin.txt` ذخیره می‌شود. راهنمای تنظیمات، بررسی خطا، تمدید SSL و بازیابی در [deploy/DEPLOYMENT.md](deploy/DEPLOYMENT.md) است.
