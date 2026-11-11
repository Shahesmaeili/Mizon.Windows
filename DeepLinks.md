# دیپ‌لینک‌های Mizon

فرمت عمومی: `mizon://{destination}[/{publicId}][/{subdestination}][?query]`

مسیرها مستقل از زبان رابط کاربری‌اند. شناسه‌ها **PublicId از نوع GUID** هستند، نه Id عددی دیتابیس. قسمت‌های داخل `{}` باید با مقدار واقعی جایگزین شوند. این سند مسیرهای پیاده‌شده را فهرست می‌کند؛ مرجع اجرایی آن `Services/AppDeepLinkCatalog.cs` است.

## صفحه‌ها و مقصدها

| مقصد | لینک | رفتار |
| --- | --- | --- |
| خانه | `mizon://home` | پوستهٔ اصلی و صفحهٔ اصلی موسیقی |
| ورود | `mizon://login` | نمایش فرم ورود؛ بدون خروج از نشست فعلی یا ارسال فرم |
| ثبت‌نام | `mizon://register` | نمایش فرم ثبت‌نام؛ بدون ساخت حساب خودکار |
| صفحهٔ اصلی موسیقی | `mizon://music` | کشف موسیقی و ویجت‌های فعلی |
| فیلم و سریال | `mizon://movies` | صفحهٔ فعلی این بخش |
| اپ‌ها | `mizon://apps` | صفحهٔ فعلی این بخش |
| VPN | `mizon://vpn` | صفحهٔ VPN؛ بدون تغییر اتصال |
| همهٔ پلی‌لیست‌ها | `mizon://playlists` | فهرست پلی‌لیست‌های موسیقی کاربر |
| جزئیات پلی‌لیست | `mizon://playlist/{playlistPublicId}` | جزئیات و آهنگ‌های پلی‌لیست |
| جزئیات آهنگ | `mizon://music/{musicPublicId}` | نمایش آهنگ **بدون پخش خودکار** |
| پخش آهنگ | `mizon://music/{musicPublicId}?action=play` | باز کردن آهنگ و درخواست صریح پخش |
| دانلود آهنگ | `mizon://music/{musicPublicId}?action=download` | دانلود با سرویس و کش فعلی برنامه |
| نظر مشخص آهنگ | `mizon://music/{musicPublicId}/comments/{commentPublicId}` | باز کردن آهنگ و نمایش نظر موردنظر؛ بدون پخش خودکار |
| پروفایل کلاینت | `mizon://client/{clientPublicId}` | صفحهٔ پروفایل |
| استوری | `mizon://story/{storyPublicId}` | نمایش همان استوری در نمایشگر فعلی عکس/ویدیو |
| فعالیت دنبال‌شده‌ها | `mizon://activities` | صفحهٔ همهٔ فعالیت‌ها، محدود به موسیقی نیست |
| تنظیمات | `mizon://settings` | تنظیمات، بخش اکانت |
| جستجو | `mizon://search?query={encodedText}` | کادر جستجوی خود برنامه با متن ارسالی |

بخش‌های فیلم/سریال و اپ‌ها به همان صفحه‌های فعلی پروژه وصل‌اند؛ جزئیات فیلم، سریال، پرسن و دسته‌بندی که هنوز صفحهٔ واقعی ندارند، مسیر ساختگی ندارند. ولکام فقط مرحلهٔ داخلی بررسی نشست است و دیپ‌لینک ندارد؛ `mizon://welcome` و شکل‌های قدیمی آن پذیرفته نمی‌شوند. `MusicItemPage` یک نمونهٔ خالی و استفاده‌نشده است و مقصد عمومی نیست. پنجره‌های داخلی مثل PiP، پنجرهٔ مستقل تسک‌بار و دیالوگ‌های تأیید نیز صفحهٔ عمومی محسوب نمی‌شوند.

## بخش‌های تنظیمات

| بخش | لینک |
| --- | --- |
| اکانت | `mizon://settings/account` |
| امنیت | `mizon://settings/security` |
| زبان | `mizon://settings/language` |
| تم و شفافیت | `mizon://settings/appearance` |
| نوتیفیکیشن | `mizon://settings/notifications` |
| فایل، دانلود و کش | `mizon://settings/storage` |
| آپدیت و نسخه | `mizon://settings/updates` |
| نسخه‌های دیگر برنامه | `mizon://settings/platforms` |
| اتصال‌های هوش مصنوعی | `mizon://settings/ai-connections` |

این لینک‌ها فقط بخش مربوطه را انتخاب می‌کنند و تنظیمات یا مجوزهای کاربر را تغییر نمی‌دهند.

## رفتار اجرا، ورود و درخواست‌های هم‌زمان

- پروتکل `mizon` در مانیفست MSIX ثبت شده است. برنامه باید نصب/Deploy شده باشد. باز کردن لینک از Win+R، مرورگر یا برنامهٔ دیگر از همان مسیر فعال‌سازی ویندوز استفاده می‌کند.
- اجرای دوم به نمونهٔ اصلی برنامه هدایت می‌شود؛ برای هر لینک یک برنامهٔ مستقل ساخته نمی‌شود.
- مقصدهای محتوایی و تنظیمات به نشست معتبر نیاز دارند. وقتی کاربر وارد نشده، مقصد تا ورود حفظ می‌شود؛ ولکام اعتبار نشست را بررسی می‌کند و سپس مقصد باز می‌شود.
- در چند لینک متوالی، آخرین درخواست جای درخواست منتظر قبلی را می‌گیرد. عملیات بارگذاری/پخش قبلی قابل لغو است. خروج صریح از حساب مقصدهای منتظر و عملیات بازکردن مقصد را لغو می‌کند؛ تا پایان خروج نیز لینک جدیدی اجرا یا برای بعد ذخیره نمی‌شود.
- لینک جزئیات آهنگ، پروفایل، نظر و پلی‌لیست به‌خودی‌خود پخش آهنگ را شروع نمی‌کند. باز کردن دوبارهٔ همان آهنگ از مسیر موجودِ بدون بازسازی صفحه استفاده می‌کند.
- نامعتبر بودن یک لینک، مقصد معتبرِ در انتظار را پاک نمی‌کند. شناسهٔ معتبر از نظر فرمت لزوماً به معنی وجود رکورد یا مجاز بودن دسترسی نیست؛ وضعیت نبودن/انقضا/خطای API با همان رفتار صفحهٔ مقصد مدیریت می‌شود.
- مسیرهای قدیمی `mizon://localhost/...` و `mizon://S-1-.../localhost/...` برای سازگاری حفظ شده‌اند؛ لینک جدید با فرمت کوتاه جدول ساخته شود.

## متن جستجو و ساخت لینک در دات‌نت

متن جستجو بعد از حذف فاصلهٔ ابتدا و انتها باید ۱ تا ۱۲۰ کاراکتر باشد. برای فارسی، فاصله و کاراکترهایی مثل `&` و `#` از URL encoding استفاده شود:

```csharp
var searchLink = "mizon://search?query=" + Uri.EscapeDataString("آهنگ جدید");
var musicLink = $"mizon://music/{musicPublicId:D}";
var commentLink = $"mizon://music/{musicPublicId:D}/comments/{commentPublicId:D}";
```

داخل خود پروژه، برای اعتبارسنجی مشترک و جلوگیری از ساخت مسیر ناسازگار:

```csharp
if (AppDeepLinkCatalog.TryCreate("music-details", musicPublicId, null, null, null, out var destination))
{
    string link = destination.ToUri()!;
}
```

پارامترهای ناشناخته، چندباره، اطلاعات ورود، پورت، fragment، مسیر فایل، آدرس API، شناسهٔ خالی و encoding خراب پذیرفته نمی‌شوند. دیپ‌لینک توکن یا پسورد دریافت نمی‌کند.

## پاسخ استاندارد دیپ‌لینک

همهٔ مقصدهای جدول با `LaunchUriForResultsAsync` قابل فراخوانی‌اند. پروتکل با
`ReturnResults="optional"` ثبت شده؛ لینک عادی مرورگر/Win+R همچنان کار می‌کند، ولی
`LaunchUriAsync` فقط نتیجهٔ شروع برنامه را می‌دهد و پاسخ عملیات را دریافت نمی‌کند.
بعد از تغییر مانیفست، Deploy مجدد MSIX لازم است.

در ورودی `ValueSet` این دو مقدار ارسال شوند:

- `mizon.responseVersion`: عدد صحیح `1`.
- `mizon.requestId`: شناسهٔ GUID یکتای درخواست، به صورت رشته.

اگر این دو مقدار ارسال نشوند، نسخهٔ ۱ و شناسهٔ تولیدشده توسط برنامه استفاده
می‌شود؛ مقدار نامعتبر یا نسخهٔ پشتیبانی‌نشده خطاست. پاسخ JSON در کلید
`mizon.response` قرار می‌گیرد. شناسهٔ پاسخ باید با درخواست تطبیق داده شود؛ موفق
بودن `LaunchUriResult.Status` بدون پاسخ معتبر، موفقیت عملیات Mizon نیست.

```json
{
  "version": 1,
  "requestId": "882e51dc-a69f-4410-98fc-d46eff3e8e2f",
  "success": true,
  "code": "completed",
  "message": "The profile was opened and loaded.",
  "data": {
    "destination": "client",
    "uri": "mizon://client/75dc8868-fda0-47f8-aef4-820591742ada",
    "completionStage": "completed",
    "publicId": "75dc8868-fda0-47f8-aef4-820591742ada"
  }
}
```

`data` اختیاری و دارای قرارداد تایپ‌شده است: مقصد، URI استاندارد، مرحلهٔ انجام،
و در صورت نیاز `publicId`، `commentPublicId` و `settingsSection`. اطلاعات تکمیلی
آینده نیز با فیلد مشخص به همین قرارداد اضافه می‌شود؛ پسورد، توکن، مسیر فایل و
متن خام exception در پاسخ نیست. سقف پاسخ ۱۶ KiB و JSON بدون reflection است.

| نتیجه | معنی |
| --- | --- |
| `opened` | صفحه یا کادر جستجو باز شده؛ دریافت همهٔ داده‌های آن صفحه ممکن است هنوز ادامه داشته باشد. |
| `completed` | عملیات پخش/دانلود، دریافت پروفایل، آماده‌شدن محتوای استوری یا پیدا شدن و نمایش کامنت انجام شده است. |
| `invalid_uri` / `invalid_request` / `unsupported_version` | مسیر یا قرارداد درخواست قابل پذیرش نیست. |
| `requires_sign_in` | نشست معتبر وجود ندارد؛ درخواست پاسخ‌دار برای بعد از ورود ذخیره نمی‌شود. |
| `busy` | ارسال فرم ورود/ثبت‌نام یا خروج از حساب در حال انجام است. |
| `profile_unavailable` / `page_unavailable` / `page_changed` | دریافت مقصد ممکن نبود یا کاربر صفحه را عوض کرد. |
| `superseded` / `cancelled` | درخواست جدید جایگزین شده یا برنامه عملیات را لغو کرده است. |
| `timed_out` | زمان پاسخ تمام شده است. |
| `unavailable` / `app_closed` / `navigation_failed` | برنامه بسته شده یا باز کردن مقصد انجام نشد. |

پاسخ دقیقاً یک بار ارسال می‌شود. مهلت سمت دریافت‌کننده معمولاً ۵۰ ثانیه و برای
دانلود صریح ۵ دقیقه است. درخواست‌های معمولیِ بدون پاسخ همچنان رفتار قبلیِ حفظ
مقصد تا ورود را دارند. پاسخ `opened` تأیید وجود رکورد در API نیست؛ برای مثال
صفحهٔ جزئیات آهنگ می‌تواند باز شده باشد و سپس وضعیت خطای API را نمایش دهد.

نمونهٔ فراخوانی دسکتاپ از کد داخل مجموعهٔ Mizon:

```csharp
var requestId = Guid.NewGuid();
var options = new Windows.System.LauncherOptions
{
    TargetApplicationPackageFamilyName = mainAppPackageFamilyName
};
WinRT.Interop.InitializeWithWindow.Initialize(options, callerWindowHandle);
var input = new Windows.Foundation.Collections.ValueSet
{
    [AppDeepLinkResultProtocol.VersionKey] = 1,
    [AppDeepLinkResultProtocol.RequestIdKey] = requestId.ToString("D")
};
var response = await Windows.System.Launcher.LaunchUriForResultsAsync(uri, options, input);
if (response.Status == Windows.System.LaunchUriStatus.Success
    && response.Result is { } values
    && values.TryGetValue(AppDeepLinkResultProtocol.ResponseKey, out var raw)
    && AppDeepLinkResultProtocol.TryParse(raw as string, requestId, out var result))
{
    // result.Success: نتیجهٔ عملیات؛ result.Data: اطلاعات تکمیلی تایپ‌شده.
}
```

کامپنیون تسک‌بار PFN واقعی برنامه را از snapshot حساب می‌گیرد؛ HWND خود
کامپنیون را به ویندوز می‌دهد، نه HWND اکسپلورر. با کلیک پروفایل، حلقهٔ لودینگ
تا پاسخ معتبر یا خطا فعال است؛ کلیک تکراری درخواست دوم نمی‌سازد. مهلت کلاینت
۶۰ ثانیه است. خروج/تعویض حساب، تخریب پنجره و Dispose، درخواست و انیمیشن را
آزاد می‌کنند. پاسخ دیررس نمی‌تواند وضعیت درخواست جدید را تغییر دهد. حلقه با
هاور عکس بزرگ نمی‌شود و فاصلهٔ بج آنلاین بودن حفظ می‌شود.

منابع API:
[WinUI 3 protocol-for-results receiver](https://learn.microsoft.com/en-us/windows/ai/app-actions/actions-uri-launch)،
[Launcher and manifest contract](https://learn.microsoft.com/en-us/uwp/launch-resume/how-to-launch-an-app-for-results)،
[desktop HWND initialization sample](https://github.com/dotnet/maui/blob/main/src/Essentials/src/MediaPicker/MediaPicker.windows.cs).

## MCP

- `mizon_get_navigation`: مقصد فعلی، URI استاندارد، مقصد منتظر و امکان بازگشت.
- `mizon_open_destination`: انتخاب مقصد از فهرست مشخص با `destination`، و در صورت نیاز `publicId`، `commentPublicId`، `searchQuery` یا `settingsSection`.
- مجوزهای «خواندن محل فعلی» و «باز کردن مقصد» در گروه جداگانهٔ ناوبری، داخل خود Mizon داده می‌شوند. مجوزهای قبلی ورود یا ظاهر به این گروه تعمیم داده نمی‌شوند.
- این ابزار URI دلخواه، اجرای دستور، پخش/دانلود آهنگ یا ارسال فرم ندارد. مقصدهای `music-play` و `music-download` فقط برای دیپ‌لینک صریح کاربر هستند و در این مجوز MCP ارائه نمی‌شوند.
- اگر نشست لازم موجود نباشد، MCP نتیجهٔ `requires_sign_in` می‌دهد و برای آینده کاری در صف نمی‌گذارد؛ لغو مجوز نباید باعث اجرای دیرهنگام یک درخواست قدیمی شود.

## بررسی‌های توسعه

```powershell
dotnet run --project WindowsSearch/Mizon.Windows.Activation.Tests -c Release
dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release -- --isolated
dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release
```

تست‌های parser/catalog در انتشار واقعی NativeAOT ویندوز x64 نیز اجرا می‌شوند. تست‌های MCP از پروتکل، سرویس مجوز و آداپتر اصلی با داده و نشست مصنوعی استفاده می‌کنند؛ تأیید مجوز واقعی یا ارسال فرم حساب واقعی انجام نمی‌دهند. این بررسی‌ها جای تست دیداری انیمیشن و صفحه‌های دارای API واقعی را نمی‌گیرند.
