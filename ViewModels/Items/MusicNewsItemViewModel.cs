using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace Mizon.Windows.ViewModels.Items;

// Local editorial samples; neither dates nor artist announcements are presented as live news.
public sealed class MusicNewsItemViewModel
{
    private BitmapImage? _image;

    private MusicNewsItemViewModel(string title, string summary, string body, string category, string editionLabel,
        string readingTimeText, string imagePath)
    {
        Title = title;
        Summary = summary;
        Body = body;
        Category = category;
        EditionLabel = editionLabel;
        ReadingTimeText = readingTimeText;
        ImagePath = imagePath;
    }

    public string Title { get; }
    public string Summary { get; }
    public string Body { get; }
    public string Category { get; }
    public string EditionLabel { get; }
    public string ReadingTimeText { get; }
    public string ImagePath { get; }
    // x:Bind requests this on the UI thread only for a displayed slide/dialog.
    // Keep a bounded decode per article instead of decoding all four at page load.
    public BitmapImage Image => _image ??= new BitmapImage { DecodePixelWidth = 720, UriSource = new Uri(ImagePath) };
    public string AutomationName => $"خبر نمونه؛ {Category}؛ {Title}؛ {Summary}؛ {ReadingTimeText}";

    public static MusicNewsItemViewModel[] CreateSamples() =>
    [
        new(
            "شبی که صحنه با نور و موسیقی جان می‌گیرد",
            "روایتی تصویری از لحظه‌های پیش از اجرا؛ جایی که نور، صدا و هیجان تماشاگران به هم می‌رسند.",
            """
            در این روایت نمونه، چراغ‌های سالن آرام خاموش می‌شوند و نخستین نت، سکوت جمعیت را می‌شکند. نور آبی روی صحنه حرکت می‌کند و هر ساز، بخشی از داستان شب را پیش می‌برد. تصویر فقط همراه موسیقی نیست؛ انگار خودش هم در اجرای قطعه سهم دارد.

            این مطلب برای پیش‌نمایش طراحی اخبار میزون نوشته شده و گزارش یک کنسرت واقعی نیست. تصور کن بعدها همین‌جا عکس‌ها، روایت اجرا و خبرهای تازهٔ هنرمندان محبوبت را دنبال کنی.
            """,
            "روی صحنه", "پروندهٔ ۰۱", "۲ دقیقه مطالعه",
            "ms-appx:///Assets/Discovery/slow-motion.jpg"),
        new(
            "از اولین تمرین تا آخرین تشویق یک اجرای زنده",
            "نگاهی به جزئیات کمتر دیده‌شدهٔ یک کنسرت؛ از چیدمان صحنه و تمرین گروه تا لحظهٔ شروع اجرا.",
            """
            پشت یک اجرای خیالی، ساعت‌ها تمرین و هماهنگی جریان دارد. گروه صدا سازها را بررسی می‌کند، نورها با ضرب‌آهنگ هماهنگ می‌شوند و نوازنده‌ها آخرین تغییرها را مرور می‌کنند. روی صحنه هنوز خبری از تماشاگر نیست، اما شکل نهایی اجرا کم‌کم پیدا می‌شود.

            این متن نمونه برای نمایش بخش اخبار است؛ روایتی از توجه به جزئیاتی که معمولاً از چشم مخاطب دور می‌مانند. از شروع تمرین تا آخرین تشویق، هر لحظه می‌تواند داستانی برای شنیدن داشته باشد.
            """,
            "پشت صحنه", "پروندهٔ ۰۲", "۳ دقیقه مطالعه",
            "ms-appx:///Assets/Discovery/artist-stage-02.jpg"),
        new(
            "وقتی موزیک‌ویدیو به یک فیلم کوتاه تبدیل می‌شود",
            "نورهای شهر، قاب‌های سینمایی و یک داستان کوتاه؛ تجربه‌ای که تصویر را به ادامهٔ موسیقی تبدیل می‌کند.",
            """
            در موزیک‌ویدیوی خیالی این پرونده، یک سفر کوتاه در خیابان‌های شب روایت می‌شود. رنگ چراغ‌ها، حرکت دوربین و مکث شخصیت اصلی، با فراز و فرود موسیقی همراه‌اند. هر قاب چیزی از داستان را می‌گوید، بدون آنکه لازم باشد همه‌چیز با کلمه توضیح داده شود.

            این مطلب یک نمونهٔ طراحی است و به اثر منتشرشده‌ای اشاره ندارد. ایدهٔ آن، نمایش خبرهایی است که میان موسیقی و سینما پل می‌زنند و مخاطب را به دیدن جزئیات تازه دعوت می‌کنند.
            """,
            "موزیک‌ویدیو", "پروندهٔ ۰۳", "۲ دقیقه مطالعه",
            "ms-appx:///Assets/Discovery/city-lights.jpg"),
        new(
            "موج آرامش؛ صداهایی برای فاصله گرفتن از شلوغی",
            "در میان صدای باران و ملودی‌های آرام، فرصتی برای کشف موسیقی‌هایی که به روزهای شلوغ فضای نفس‌کشیدن می‌دهند.",
            """
            این پروندهٔ نمونه، یک مکث کوتاه را تصویر می‌کند: پنجره‌ای رو به باران، صدای آرام پیانو و چند دقیقه دور از شلوغی روز. قطعه‌ها در این روایت خیالی فرصت دارند نفس بکشند؛ فاصلهٔ میان نت‌ها هم به اندازهٔ خود ملودی شنیده می‌شود.

            این متن برای آزمایش ظاهر بخش اخبار نوشته شده و معرفی یک مجموعهٔ واقعی نیست. بعدها می‌توان چنین روایت‌هایی را کنار خبر انتشارها و پیشنهادهای شنیدنی گذاشت تا کشف موسیقی، تجربه‌ای آرام و شخصی باشد.
            """,
            "تجربهٔ شنیدن", "پروندهٔ ۰۴", "۲ دقیقه مطالعه",
            "ms-appx:///Assets/Discovery/quiet-room.jpg")
    ];
}
