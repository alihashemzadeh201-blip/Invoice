# تحلیل پروژه Invoice (سامانه صدور صورتحساب الکترونیکی)

**تاریخ تحلیل:** ۱۴۰۵/۰۶/۰۶ (۲۰۲۶-۰۸-۲۸)
**محدوده بررسی:** کل مخزن در شاخه `arena/01a04769-invoice` (تنها یک کامیت: `Initial-commit`)

---

## ۱. خلاصه اجرایی

پروژه یک **ASP.NET Core Razor Pages** روی `.NET 8` است که به عنوان سامانه واسط صدور صورتحساب الکترونیکی (اتصال به سامانه مودیان/پایانه فروشگاهی به نشانی `tp.tax.gov.ir`) عمل می‌کند و امکاناتی مثل مدیریت فاکتور، واردات اکسل، مدیریت کاربران/نمایندگان (reseller)، کیف پول و حسابداری مشتریان را دارد.

| شاخص | مقدار |
|---|---|
| زبان / فریم‌ورک | C# / ASP.NET Core Razor Pages، `net8.0` |
| پایگاه داده | SQL Server (EF Core 6) |
| حجم کد دست‌نویس | ~۱۲٬۶۵۰ خط (`Code` ۲٬۰۸۹ + `Models` ۱٬۱۴۵ + `Controllers` ۵۰۶ + `Pages` ۸٬۹۱۲) |
| حجم Migrationها | ~۴۳٬۰۰۰ خط (۵۹ migration) |
| صفحات Razor | ۳۳ صفحه |
| تست / CI / README | **هیچ‌کدام وجود ندارد** |
| حجم `wwwroot` در گیت | ۴۵ مگابایت (فونت، PDF، کتابخانه‌ها) |

**ارزیابی کلی:** از نظر عملکردی پروژه‌ای نسبتاً کامل و «در حال کار در تولید» به نظر می‌رسد (دامنه، پروفایل انتشار، مایگریشن‌های فراوان)، اما از نظر **امنیت در وضعیت بحرانی** قرار دارد و از نظر **نگه‌داری پذیری بدهی فنی سنگینی** دارد. چند مورد از یافته‌ها (رمز عبور سراسریِ هاردکدشده، ارتقای سطح دسترسی در ثبت‌نام، API بدون احراز هویت) در سطح «نشت کامل داده / تسخیر کامل سامانه» هستند و باید **همین امروز** رفع شوند.

> ⚠️ نکته: در این محیط امکان کامپایل پروژه وجود نداشت (SDK دات‌نت نصب نیست و مهم‌تر از آن، ارجاع به `..\TaxCollectData.Library\TaxCollectData.Library.dll` خارج از مخزن است). تحلیل بر پایه خواندن مستقیم کد است، نه اجرا.

---

## ۲. یافته‌های امنیتی (بحرانی)

### 🔴 C1 — رمز عبور مدیر در سورس هاردکد شده و در هر بار استارت بازنویسی می‌شود
**فایل:** `Program.cs:36-52`

```csharp
var user = context.Users.SingleOrDefault(p => p.Username == "admin");
if (user == null) { user = new User { Username="admin", Password = Encryptor.MD5Hash("la#1631"), Role="admin" }; ... }
else { user.Password = Encryptor.MD5Hash("la#1631"); user.Role = "admin"; }
context.SaveChanges();
```

- یک **رمز عبور سراسری و تغییرناپذیر** برای حساب `admin` در سورس قرار دارد. هر کسی که به کد دسترسی داشته باشد (یا این مخزن را ببیند) می‌تواند وارد سامانه شود.
- چون در هر استارت برنامه اجرا می‌شود، **اگر مدیر رمزش را عوض کند، با اولین ری‌استارت دوباره به همین رمز برمی‌گردد**.
- همچنین این کد در مسیر استارت‌آپ است: اگر پایگاه داده در دسترس نباشد، **کل برنامه از کار می‌افتد** (هیچ try/catch و هیچ logging ی ندارد).

**اقدام:** حذف کامل این بلوک؛ ساخت کاربر اولیه از طریق یک فرمان یک‌باره (seed script / migration) و خواندن رمز از متغیر محیطی؛ در غیر تولید اجرا نشود.

---

### 🔴 C2 — ارتقای سطح دسترسی (Privilege Escalation) در صفحه ثبت‌نام
**فایل:** `Pages/Account/Register.cshtml.cs`

```csharp
[AllowAnonymous]
public class RegisterModel : PageModel {
    [BindProperty] public Invoices.Models.User Input { get; set; }   // ← بایند مستقیم مدل دیتابیسی
    ...
    await Identity.SignIn(HttpContext, Input.Username, Input.Role);  // ← نقش از فرم می‌آید!
```

- کلاس `User` (مدل EF) مستقیماً به فرم بایند شده و در نتیجه ویژگی **`Role`** توسط کاربر ناشناس قابل تنظیم است. ارسال `Role=admin` در فرم ثبت‌نام = **دریافت فوری نقش ادمین**.
- همین بایند، حمله **Mass Assignment** هم هست: فیلدهای `Vallet` (موجودی کیف پول)، `PerInvoiceCost`، `MaxProfileCount`، `ExpireDate` و `ProfileId` همگی توسط کاربر قابل نوشتن هستند → یک کاربر عادی می‌تواند موجودی کیف پول خود را دلخواه بالا ببرد یا تاریخ انقضا را جابجا کند.

**اقدام:** استفاده از یک ViewModel/InputModel مجزا که فقط `Username`, `Password`, `ConfirmPassword`, `Email` را دارد؛ مقدار `Role` همیشه سمت سرور برابر `"user"` تنظیم شود؛ فیلدهای مالی با `[BindNever]` یا از طریق سرویس‌های دامنه تغییر کنند.

---

### 🔴 C3 — API کنترلر بدون هیچ احراز هویتی، تمام فاکتورهای همه مشتریان را برمی‌گرداند
**فایل:** `Controllers/InvoiceController.cs`

```csharp
[ApiController] [Route("api/[controller]")]
public class InvoiceController : Controller {          // ← هیچ [Authorize] ای نیست
    [HttpGet]
    public IActionResult GetInvoices(string? taxid, string? buyerTin, InvoiceStatus? status) {
        var invoices = _context.Invoices.Include(p => p.InvoiceItems);
        ...
        return Ok(list);                                // ← بدون فیلتر مالکیت، بدون صفحه‌بندی
    }
}
```

یک درخواست ساده `GET /api/Invoice` توسط **هر کسی بدون لاگین**، تمام صورتحساب‌های همه شرکت‌ها را همراه با آیتم‌ها (شماره اقتصادی خریدار/فروشنده، مبالغ، کد ملی/ملی خریدار) برمی‌گرداند. این یک **نشت کامل داده‌های مالیاتی customers** است و از نظر قانونی (حریم داده‌های مالیاتی مودیان) بسیار پرریسک.

**اقدام:** افزودن `[Authorize]`, فیلتر بر اساس `Sender`/`ProfileId` کاربر جاری، `Take/Skip` اجباری، و ترجیحاً بستن این مسیر تا زمان بازنگری.

---

### 🔴 C4 — رمزهای عبور با MD5 و بدون Salt
**فایل‌ها:** `Code/Identity.cs:12-33`، `Pages/Account/Login.cshtml.cs`، `Pages/Account/Register.cshtml.cs`

```csharp
MD5 md5 = new MD5CryptoServiceProvider();
md5.ComputeHash(ASCIIEncoding.ASCII.GetBytes(text));   // ASCII هم هست (یونیکد فارسی را خراب می‌کند)
```

MD5 یک تابع درهم‌سازِ سریع و شکسته‌شده است؛ با دسترسی به پایگاه داده، بازیابی رمزها با جدول رنگین‌کمانی/حمله بروت‌فورس روی GPU عملاً آنی است. همچنین استفاده از `ASCIIEncoding` باعث می‌شود رمزهای دارای کاراکتر غیرلاتین نادرست هش شوند.

**اقدام:** انتقال به `PasswordHasher<TUser>` (ASP.NET Core Identity، مبتنی بر PBKDF2 با ۱۰۰هزار تکرار) یا `Rfc2898DeriveBytes`/Argon2id؛ مهاجرت تدریجی رمزهای قدیمی در هنگام لاگین موفق.

---

### 🔴 C5 — کلید خصوصی امضای مالیاتی به صورت متن ساده در DB + امکان دستکاری پروفایل دیگران
**فایل‌ها:** `Models/Profile.cs:22` (`public string? PrivateKey`), `Pages/Account/UserProfile.cshtml.cs`

```csharp
[BindProperty] public Profile Profile { get; set; }   // Id هم از سمت کلاینت می‌آید
...
if (Profile.Id > 0) { _context.Attach(Profile).State = EntityState.Modified; }  // بدون چک مالکیت
```

- کلید خصوصی امضای الکترونیکی مودیان (که معادل امضای مالی یک شرکت است) **بدون رمزنگاری** در ستون `Profile.PrivateKey` ذخیره می‌شود.
- بدتر: چون `Profile.Id` از فرم بایند می‌شود و **هیچ بررسی مالکیتی** انجام نمی‌شود، هر کاربر لاگین‌کرده می‌تواند با ارسال `Profile.Id` دلخواه، پروفایلِ هر مودی دیگری را (شامل `TinCode`, `MemoryId`, `PrivateKey`) **بازنویسی کند** — یعتی جایگزینی کلید امضا و ارسال صورتحساب به نام شرکت قربانی.

**اقدام:** (الف) همیشه پروفایل را بر اساس `User.Identity.Name` (یا `ProfileId` معتبرِ کاربر) بخوان و سپس فیلدهای مجاز را کپی کن؛ هرگز `Id` را از فرم قبول نکن. (ب) کلید خصوصی را رمزنگاری‌شده (Data Protection API / Azure Key Vault) ذخیره کن.

---

### 🔴 C6 — ۲۰ صفحه از ۳۳ صفحه بدون `[Authorize]` و هیچ سیاست سراسری هم وجود ندارد
**فایل:** `Program.cs:11` — `builder.Services.AddRazorPages().AddRazorRuntimeCompilation();` (بدون `Conventions.AuthorizeFolder` و بدون `FallbackPolicy`)

بنابراین هر صفحه‌ای که `[Authorize]` نداشته باشد **کاملاً عمومی** است. نمونه‌های پرمخاطره:

| صفحه | ریسک |
|---|---|
| `Pages/InvoiceManage/Payments.cshtml.cs` | `InvoiceId` از query-string می‌آید → هر کسی با حدس زدن یک شناسه، تمام پرداخت‌های آن فاکتور را می‌بیند (IDOR) |
| `Pages/Admin/Requests/Details.cshtml.cs` | جزئیات درخواست‌های کارپوشه مالیاتی (داده ادمین) با `?id=` در دسترس همه |
| `Pages/InvoiceManage/ImportInvoiceItems.cshtml.cs` | **آپلود فایل اکسل بدون احراز هویت** + ایجاد آیتم فاکتور روی هر `InvoiceId` دلخواه |
| `Pages/Credit/Expired.cshtml.cs`، `Pages/Request/*`، `Pages/Product/Details.cshtml.cs` | دسترسی عمومی به داده‌هایی که احتمالاً باید محافظت شوند |

**اقدام (فوری و ارزان):** در `Program.cs` یک خط اضافه کن تا پیش‌فرض همه چیز محافظت شود و بعد صفحات عمومی را استثنا کن:

```csharp
builder.Services.AddRazorPages(o => {
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToFolder("/Account");
}).AddRazorRuntimeCompilation();
```

---

### 🟠 C7 — نقض دسترسی در ویرایش/حذف آیتم‌های فاکتور (IDOR)
**فایل:** `Pages/InvoiceManage/InvoiceItems.cshtml.cs`

- در `OnGetAsync` بررسی مالکیت وجود دارد: `if (Invoice.Sender != User.Identity.Name) return Forbid();`
- اما در **`OnPostAsync` و `OnGetDeleteAsync` هیچ بررسی‌ای نیست**: آیتم با `FindAsync(Id)` از دیتابیس خوانده می‌شود و مستقیماً ویرایش یا حذف می‌گردد. هر کاربر لاگین‌کرده‌ای می‌تواند با ارسال `InvoiceItem.Id` متعلق به شرکت دیگر، **ردیف‌های فاکتورِ دیگران را تغییر دهد یا حذف کند** (دستکاری مبلغ/مالیات پیش از ارسال به سامانه مالیاتی).

**اقدام:** در مسیرهای نوشتن/حذف هم مالکیت را چک کن (از طریق `Include(Invoice)` یا یک کوئری شرطی روی `Sender`).

---

### 🟠 C8 — رشته اتصال پایگاه داده با رمز واقعی، هم در `appsettings.json` و هم هاردکد در `Program.cs`
**فایل‌ها:** `appsettings.json:3` و `Program.cs:14`

```csharp
builder.Services.AddDbContext<DataContext>(p => p.UseSqlServer("server=<سرور>;database=<پایگاه‌داده>;user id=<کاربر>;password=<رمز عبور واقعی در مخزن بود>;"));
```

- آدرس IP سرور، نام پایگاه داده و رمز عبور حساب `sa`-مانند در مخزن کامیت شده است.
- کاملاً بی‌استفاده هم هست: یک خط بالاتر `var connection = builder.Configuration.GetConnectionString("default");` خوانده شده ولی بعداً استفاده نشده است!

**اقدام:** (۱) `UseSqlServer(connection)` — استفاده از مقدار کانفیگ. (۲) انتقال رشته اتصال به User-Secrets / متغیرهای محیطی / Key Vault. (۳) **رمز عبور فعلی را فوراً تغییر بده** (در تاریخچه گیت هم هست) و دسترسی `sa` را محدود کن.

---

### 🟠 C9 — وضعیت مشترک (Singleton) در کلاینت سامانه مالیاتی → نشت بین مشتریان همزمان
**فایل:** `Code/InvoiceApiHelper.cs:26-33`

```csharp
private void initApi(string memoryId, string privateKey) {
    TaxApiService.Instance.Init(memoryId, new SignatoryConfig(privateKey, null), ...);
    _api = TaxApiService.Instance.TaxApis;
}
```

یک شیء **Singleton سراسری** در هر درخواست با کلید خصوصیِ مودیِ فعلی مقداردهی می‌شود. در حالت عادیِ وب (درخواست‌های همزمان)، دو کاربر مختلف می‌توانند همزمان `Init` را با کلیدهای متفاوت صدا بزنند و در نتیجه فاکتورِ یک شرکت **با کلید امضای شرکت دیگر** امضا و ارسال شود. این یک باگ بسیار پرهزینه در سامانه مالیاتی است.

مشکلات دیگر این فایل:
- `Thread.Sleep(5000)` در مسیر درخواست → قفل شدن thread و افت شدید زیر بار.
- `Console.WriteLine("success login, token: " + token.Token)` → **چاپ توکن احراز هویت در لاگ**.
- `new Random()` برای تولید سریال مالیاتی (احتمال تکرار/تداخل).
- `status.Value` روی `InvoiceStatus?` که ممکن است null باشد → `NullReferenceException`.

**اقدام:** نمونه‌سازی per-request (Scoped) از کلاینت به جای Singleton؛ استفاده از `await Task.Delay`؛ حذف `Console.WriteLine`؛ لاگ با `ILogger`.

---

## ۳. کیفیت کد و معماری

### نقاط قوت 👍
- **ساختار پوشه‌ای معقول**: تفکیک `Models` / `Code` (سرویس‌ها) / `Controllers` / `Pages` / `Migrations` مطابق قراردادهای ASP.NET Core.
- **مدل دامنه غنی و مستندسازی‌شده با Data Annotation**: `Models/Invoice.cs` با `Display(Name=...)` فارسی و `RegularExpression`/`RequiredIf` برای فیلدهای سامانه مودیان، خوانایی خوبی دارد.
- **Enumهای فارسیِ تطبیق‌یافته با استاندارد مالیاتی** (`InvoicePattern`, `InvoiceTopic`, `VatRate`, …) که دامنه مسئله را شفاف می‌کند.
- **تزریق وابستگی و اینترفیس‌ها** برای سرویس‌های جدید (`IImportTemplateService`, `IInvoiceService`, `ICustomerFinanceService`) — این بخش‌ها مدرن‌تر از بقیه کد نوشته شده‌اند.
- **بررسی مالکیت در برخی صفحات جدید** (مثل `CustomerLedger` و `CustomerFinanceController` برای نقش reseller) نشان می‌دهد آگاهی امنیتی در حال شکل‌گیری است — فقط باید به کل پروژه تعمیم یابد.
- پوشش سناریوهای واقعیِ کسب‌وکار: واردات گروهی اکسل، قالب‌های نگاشت ستون، کیف پول، بدهی/پرداخت مشتری، درخواست شناسه کارپوشه.

### نقاط ضعف 👎

1. **هیچ تستی وجود ندارد** — نه پروژه تست، نه CI، نه حتی یک فایل `README`. برای سامانه‌ای که با پول و مالیات سروکار دارد، این بزرگ‌ترین ریسکِ تغییرپذیری است.
2. **نبود لایه‌بندی واقعی**: منطق تجاری درون PageModelها پخش شده (برای نمونه `CustomerLedger.cshtml.cs` ۲۳۶ خط و `InvoiceItems.cshtml.cs` شامل کوئری، اعتبارسنجی، نگاشت و ذخیره‌سازی با هم). سرویس‌ها (`Code/`) فقط بخش کوچکی را پوشش می‌دهند.
3. **تکرار (Duplication)**: کوئری `Products.Where(p => p.Creator == User.Identity.Name || p.Creator == "admin")` و ساخت `SelectList` ارزها/واحدها در `InvoiceItems` چند بار کپی شده؛ همچنین سه فایل لایه‌یافت (`_Layout.cshtml`, `_Layout_b.cshtml`, `_Layout-CONFLICT-1.cshtml` با ۴۰۰ خط — ظاهراً باقیمانده‌ی یک تداخل ادغام).
4. **مدیریت خطای ابتدایی**: بلوک‌های `catch (Exception) { }` خالی در `Utils.CopyProperties` و تبدیل همه خطاها به پیام‌های عمومی فارسی در `InvoiceApiHelper` — دیباگ در تولید را بسیار سخت می‌کند و خطای واقعی را پنهان می‌سازد.
5. **کدِ کامنت‌شدهٔ زیاد در مسیرهای حساس** (مثلاً کل منطق آپلود کلید خصوصی در `Register.cshtml.cs`) که هم خوانایی را پایین می‌آورد و هم تردید ایجاد می‌کند که کدام مسیر واقعاً فعال است.
6. **یک فایل با انکدینگ UTF-16** (`Pages/InvoiceManage/InvoiceItems.cshtml.cs`) — گیت آن را به عنوان فایل باینری می‌بیند، در نتیجه `diff`/`blame`/مرور کد روی آن عملاً از کار می‌افتد. باید به UTF-8 تبدیل شود.
7. **بدون ثبت وقایع (logging) معنادار**: به جز چند مورد در کنترلر مالی، هیچ لاگی از رویدادهای حساس (لاگین ناموفق، ارسال فاکتور، تغییر پروفایل) وجود ندارد.
8. **نبود Rate Limiting / قفل شدن حساب** در صفحه لاگین + **افشای نام کاربری** (پیام «نام کاربری یافت نشد» در مقابل پیامِ سراسری) → حمله بروت‌فورس و شناسایی شماره موبایل کاربران بسیار آسان است.
9. **صفحهٔ بلااستفاده و خطرناک**: `Pages/GetPublicPrivateKey.cshtml` یک صفحهٔ عمومی است که در هر بازدید یک جفت کلید RSA می‌سازد و در `Console` چاپ می‌کند؛ نه خروجی دارد و نه کاربردی. باید حذف شود.
10. **`Thread.Sleep` و کارهای همزمانِ بلوکه‌کننده** در مسیر ارسال به سامانه مالیاتی.

---

## ۴. وابستگی‌ها و زیرساخت

| بسته | نسخه | وضعیت |
|---|---|---|
| `TargetFramework` | `net8.0` | ✅ |
| `Microsoft.EntityFrameworkCore*` | **6.0.25 / 6.0.23** | ⚠️ ناهماهنگ با net8.0؛ EF Core 6 روی .NET 8 پشتیبانی رسمی ندارد — ارتقا به 8.x |
| `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` | **6.0.25** | ⚠️ باید 8.x باشد؛ نسخه‌های ناهماهنگِ ASP.NET Core روی net8.0 می‌توانند خطای زمان اجرا ایجاد کنند |
| `Nancy` | 2.0.0 | ❌ فریم‌ورک متروک و منقرض‌شده؛ فقط برای چند متد افزونه (`IsAuthenticated()`, `Nancy.Json`) استفاده شده — با یک خط کد قابل حذف است |
| `Portable.BouncyCastle` | 1.9.0 | ⚠️ قدیمی/بدون نگه‌داری؛ جایگزین رسمی `BouncyCastle.Cryptography` است |
| `OfficeOpenXml.Core.ExcelPackage` | 1.0.0 | ⚠️ بستهٔ غیررسمیِ بازنشرِ EPPlus؛ در عمل استفاده نشده (کد مربوطه کامنت است) → حذف شود |
| `DocumentFormat.OpenXml` | 3.0.1 | ⚠️ نسخهٔ اصلی (Major) تغییر کرده و `Code/ExcelHelper.cs` با APIهای 2.x نوشته شده — ریسک شکست کامپایل |
| `TaxCollectData.Library.dll` | ارجاع به `..\TaxCollectData.Library\` | ❌ **خارج از مخزن است** → کلونِ تمیز پروژه کامپایل نمی‌شود. باید به NuGet/Private feed یا پوشه `lib/` داخل مخزن منتقل شود |
| `FontAwesome` (wwwroot/lib) | فونت‌های ۱–۳ مگابایتی | ⚠️ ۴۵ مگابایت فایل باینری داخل گیت؛ انتقال به CDN یا npm |

سایر موارد زیرساختی:
- `appsettings.json` شامل یک خط کامنت‌شده با `//` است — بدنهٔ JSON استاندارد نیست (کانفیگ‌خوانِ دات‌نت آن را نادیده می‌گیرد، ولی ابزارهای دیگر نه). بهتر است به `appsettings.Development.json` منتقل شود.
- تنها یک کامیت در تاریخچه (`Initial-commit`) با ۲۱ مگابایت حجم `.git`؛ یعنی تاریخچه توسعه عملاً از دست رفته است.
- هیچ `Dockerfile`، `.github/workflows` یا فایل پیکربندی استقرار خودکار وجود ندارد (فقط پروفایل Web Deploy ویندوزی).
- کوکی‌ها: `Secure = Always` و `HttpOnly = Always` خوب است، ولی `MinimumSameSitePolicy = None` برای **همه** کوکی‌ها اعمال شده که سطح حمله CSRF را بزرگ‌تر می‌کند؛ برای کوکی احراز هویت بهتر است `Lax` باشد.
- در `Program.cs` هم `MapRazorPages/MapControllers` فراخوانی شده و هم یک بلوک قدیمی `UseEndpoints` — تکراری و مربوط به الگوی قدیمی `Startup`؛ حذف شود.

---

## ۵. برنامه اقدام پیشنهادی

### 🚨 همین امروز (۰ تا ۲۴ ساعت)
1. **تغییر رمز عبور پایگاه داده** و خارج کردن رشته اتصال از مخزن (و پاک‌سازی تاریخچه).
2. حذف بلوک ساخت/بازنشانی کاربر `admin` از `Program.cs` و **تغییر رمز مدیر** پس از آن.
3. اضافه کردن `[Authorize]` به `Controllers/InvoiceController.cs` (یا غیرفعال کردن موقت این مسیر).
4. جایگزینی `User Input` در `Register` با یک InputModel امن (رفع ارتقای سطح دسترسی و Mass Assignment).
5. افزودن `Conventions.AuthorizeFolder("/")` در `Program.cs` برای محافظتِ پیش‌فرضِ همه صفحات.

### 🟠 این هفته (۱ تا ۲ هفته)
6. رفع IDOR در `InvoiceItems` (POST/Delete) و `UserProfile` (بررسی مالکیت پروفایل) و `Payments`.
7. جایگزینی MD5 با `PasswordHasher` + مهاجرت رمزهای موجود.
8. رمزنگاری `Profile.PrivateKey` در پایگاه داده.
9. تبدیل کلاینت سامانه مالیاتی از Singleton به per-request/scoped؛ حذف `Thread.Sleep` و چاپ توکن.
10. قفل شدن حساب / محدودسازی نرخ در لاگین و یکسان‌سازی پیام خطا.

### 🟡 این ماه (۳ تا ۴ هفته)
11. ارتقای بسته‌ها: EF Core 6 → 8، RuntimeCompilation → 8.x، حذف `Nancy` و `OfficeOpenXml.Core.ExcelPackage`، بررسی `DocumentFormat.OpenXml` 3.0.
12. داخل‌مخزن‌کردن (یا NuGet ی‌کردن) `TaxCollectData.Library.dll` تا پروژه از کلون تمیز کامپایل شود.
13. تبدیل `InvoiceItems.cshtml.cs` به UTF-8 و حذف فایل‌های تکراری لایه و صفحه `GetPublicPrivateKey`.
14. نوشتن نخستین تست‌ها برای مسیرهای بحرانی (محاسبه مالیات/جمع فاکتور، واردات اکسل، بررسی مالکیت) و راه‌اندازی CI برای کامپایل خودکار.
15. انتقال منطق تجاری از PageModelها به لایه سرویس (شروع از `CustomerLedger` و `InvoiceItems`).
16. افزودن لاگ ساختاریافته برای رویدادهای حساس (ورود، ارسال فاکتور، تغییر پروفایل/کلید).

---

## ۶. جمع‌بندی

پروژه از نظر **پوشش عملکردی** بالغ است و به نظر می‌رسد در حال ارائه خدمت واقعی باشد؛ زحمت زیادی برای تطبیق با استاندارد صورتحساب الکترونیکی کشیده شده. اما از نظر **امنیت و نگه‌داری‌پذیری** در نقطه‌ای است که ادامه توسعه بدون یک دور پاک‌سازی، ریسک را به‌شدت بالا می‌برد: رمزهای عبور ضعیف و هاردکد، نبود محافظت پیش‌فرض روی صفحات و APIها، دسترسی مستقیم به داده‌ی دیگران با تغییر یک شناسه در URL، و نگه‌داری کلید امضای مالیاتی به صورت متن ساده.

خبر خوب اینکه بیشتر این موارد **اصلاحات کوچک و موضعی** هستند (چند خط در `Program.cs` و چند صفت/ViewModel) و با صرف دو تا سه روزِ متمرکز می‌توان سامانه را از وضعیت «بحرانی» به «قابل قبول» رساند. پیشنهاد می‌شود اولویت مطلق با پنج موردِ بخش «همین امروز» باشد، به‌ویژه مورد C2 (ارتقای سطح دسترسی) و C3 (API عمومی) که در صورت سوءاستفاده، مستقیماً به افشای داده‌های مالیاتی و جعل صورتحساب منجر می‌شوند.

---
*تهیه‌شده توسط دستیار تحلیل کد — بر پایه خواندن مستقیم منابع مخزن (بدون اجرای برنامه).*
