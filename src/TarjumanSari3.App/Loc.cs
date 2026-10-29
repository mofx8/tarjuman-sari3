using System.Windows;
using System.Windows.Controls;
using TarjumanSari3.Core.Translation;

namespace TarjumanSari3.App;

/// <summary>
/// Interface localisation. Arabic is the source language of every UI string; when the UI language is
/// English the string is looked up here (missing entries simply stay Arabic).
/// </summary>
internal static class Loc
{
    public static bool English { get; private set; }

    public static void Init(string uiLanguage)
    {
        English = string.Equals(uiLanguage, "en", StringComparison.OrdinalIgnoreCase);
        Languages.UseEnglish = English;
    }

    public static string T(string ar) => English && En.TryGetValue(ar, out var en) ? en : ar;

    /// <summary>Translates every string in a window's element tree and flips it to LTR for English.</summary>
    public static void Apply(Window window)
    {
        if (!English) return;
        window.FlowDirection = System.Windows.FlowDirection.LeftToRight;
        if (window.Title is { } title) window.Title = T(title);
        Walk(window);
    }

    private static void Walk(DependencyObject node)
    {
        switch (node)
        {
            case TextBlock tb when tb.Text.Length > 0: tb.Text = T(tb.Text); break;
            case HeaderedContentControl hc when hc.Header is string h: hc.Header = T(h); break;
            case ContentControl cc when cc.Content is string s: cc.Content = T(s); break;
        }
        if (node is FrameworkElement fe && fe.ToolTip is string tip) fe.ToolTip = T(tip);
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d) Walk(d);
    }

    private static readonly Dictionary<string, string> En = new()
    {
        [" (من الكاش)"] = " (cached)",
        ["، "] = ", ",
        [". غيّرها من الإعدادات."] = ". Change them in Settings.",
        [". لقراءة لغة أخرى ثبّت حزمتها من: الإعدادات ← الوقت واللغة ← اللغة والمنطقة."] = ". To read another language install its pack: Settings > Time & language > Language & region.",
        ["(بدون اختصار)"] = "(none)",
        ["(يحتاج مفتاحاً/رابطاً)"] = "(needs a key/URL)",
        ["أضف Ctrl أو Alt أو Shift أو Win مع المفتاح."] = "Add Ctrl, Alt, Shift or Win with the key.",
        
        ["إظهار الأيقونة بجانب الساعة"] = "Show the tray icon",
        ["إظهار بطاقة النص الأصلي مع زر النسخ"] = "Show the original-text card with a Copy button",
        ["إظهار/إخفاء النافذة"] = "Show/hide window",
        ["إعادة استخدام"] = "Reuse",
        ["إعادة ترجمة آخر منطقة"] = "Re-translate last area",
        ["إغلاق"] = "Close",
        ["إغلاق تلقائي بعد (ثوانٍ، 0 = حتى النقر):"] = "Auto-close after (seconds, 0 = until click):",
        ["ابحث في السجل"] = "Search history",
        ["استخدام الكاش الدائم (لا تُرسل نفس الجملة مرتين)"] = "Use the persistent cache (never send the same sentence twice)",
        ["استدارة الزوايا"] = "Corner radius",
        ["استعادة كل الاختصارات الافتراضية"] = "Restore all default shortcuts",
        ["اسحب لتحديد منطقة الترجمة المباشرة • Esc أو زر الفأرة الأيمن للإلغاء"] = "Drag to choose the live-translation area • Esc or right-click to cancel",
        ["افتراضي"] = "Default",
        ["الإعدادات"] = "Settings",
        ["الاختصارات"] = "Shortcuts",
        ["البدء مصغّراً في منطقة الإشعارات"] = "Start minimized to the tray",
        ["الترجمة"] = "Translate",
        ["الترجمة والمحركات"] = "Translation & engines",
        ["التشغيل بصلاحية المدير (يلزم ليعمل الاختصار فوق البرامج/الألعاب التي تعمل كمدير)"] = "Run as administrator (needed for shortcuts to work over apps/games running as admin)",
        ["التطبيقات المستثناة (أسماء العمليات مفصولة بفاصلة، مثل: notepad, game)"] = "Ignored apps (process names separated by commas, e.g. notepad, game)",
        ["التقاط الشاشة و OCR"] = "Screen capture & OCR",
        ["الحافظة لا تحتوي نصاً."] = "The clipboard has no text.",
        ["الحركات الانتقالية"] = "Animations",
        ["السجل والمفضلة"] = "History & favorites",
        ["السمة"] = "Theme",
        ["القاموس الشخصي — كل سطر: كلمة=الترجمة المفضلة"] = "Personal glossary — one per line: word=preferred translation",
        ["اللغة الهدف:"] = "Target language:",
        ["المحركات (رتّبها واختر المفعّل):"] = "Engines (reorder and enable):",
        ["المظهر والنتيجة"] = "Appearance & result",
        ["المفضلة فقط"] = "Favorites only",
        ["الوضع الافتراضي لاختصار «ترجمة منطقة»"] = "Default mode for the “translate area” shortcut",
        ["انقر على الحقل ثم اضغط الاختصار الجديد. (Delete للمسح، Esc للإلغاء)"] = "Click the field, then press the new shortcut. (Delete clears, Esc cancels)",
        ["انقر على نص لترجمته • اسحب لتحديد منطقة • Space إخفاء/إظهار • R إعادة • Esc إغلاق"] = "Click text to translate • drag to select an area • Space hide/show • R repeat • Esc close",
        ["تأخير التشغيل (ثوانٍ):"] = "Startup delay (seconds):",
        ["تأكيد"] = "Confirm",
        ["تبديل اللغتين"] = "Swap languages",
        ["تحت المؤشر (ترجمة فورية للنص القريب)"] = "Under cursor (instant translation of nearby text)",
        ["تحديث الحالة"] = "Refresh status",
        ["ترجمان سريع"] = "Tarjuman Sari3",
        ["ترجمة (Ctrl+Enter)"] = "Translate (Ctrl+Enter)",
        ["ترجمة أي نص أنسخه تلقائياً (يتطلب إعادة تشغيل التطبيق)"] = "Translate anything I copy automatically (restart required)",
        ["ترجمة الحافظة"] = "Translate clipboard",
        ["ترجمة النص المحدد"] = "Translate selected text",
        ["ترجمة النص تحت المؤشر"] = "Translate text under cursor",
        ["ترجمة مباشرة لمنطقة"] = "Live-translate an area",
        ["ترجمة مباشرة لمنطقة (تشغيل/إيقاف)"] = "Live-translate an area (on/off)",
        ["ترجمة منطقة من الشاشة"] = "Translate a screen area",
        ["تشغيل التطبيق مع بدء ويندوز"] = "Start with Windows",
        ["تعذّر تسجيل بعض الاختصارات: "] = "Some shortcuts could not be registered: ",
        ["تعذّر تسجيل: "] = "Could not register: ",
        ["تعذّرت الترجمة: "] = "Translation failed: ",
        ["تعذّرت العملية: "] = "Operation failed: ",
        ["تكبير المناطق الصغيرة قبل القراءة (أدق)"] = "Upscale small areas before reading (more accurate)",
        ["تم مسح الكاش."] = "Cache cleared.",
        ["تمت الترجمة — انقر نصاً آخر، أو Space لإظهار الأصل، أو Esc للإغلاق"] = "Done — click other text, Space to show the original, Esc to close",
        ["تنبيه: اختصار مكرر بين: "] = "Warning: duplicate shortcut between: ",
        ["توزيع الطلبات بالتناوب بين المحركات (يرفع الحد العملي كثيراً)"] = "Rotate requests between engines (greatly raises the practical limit)",
        ["جارٍ الترجمة…"] = "Translating…",
        ["جارٍ قراءة المنطقة…"] = "Reading the area…",
        ["جارٍ قراءة النص…"] = "Reading text…",
        ["حجم الخط"] = "Font size",
        ["حدث خطأ: "] = "Error: ",
        ["حذف"] = "Delete",
        ["حساسية دمج الأسطر في فقرة (0.5 أقل – 2 أكثر)"] = "Paragraph merge sensitivity (0.5 less – 2 more)",
        ["حسب النظام"] = "Follow system",
        ["حفظ"] = "Save",
        ["حفظ سجل الترجمات محلياً"] = "Keep translation history locally",
        ["خروج"] = "Exit",
        ["خط الترجمة"] = "Translation font",
        ["داكن"] = "Dark",
        ["زر الإغلاق (×) يخفي التطبيق بدلاً من إنهائه"] = "The close button (×) hides the app instead of quitting",
        ["زر الفأرة الأيمن للإيقاف"] = "Right-click to stop",
        ["سحب مستطيل"] = "Drag a rectangle",
        ["شفافية لوحة الترجمة"] = "Translation panel opacity",
        ["عام والتشغيل"] = "General & startup",
        ["عند التثبيت تُنشأ مهمة مجدولة بأعلى الصلاحيات فلا يظهر طلب UAC عند كل تشغيل."] = "A highest-privilege scheduled task is created so UAC is not shown on every launch.",
        ["فاتح"] = "Light",
        ["فاصل التحديث في الترجمة المباشرة (مللي ثانية):"] = "Live translation refresh interval (ms):",
        ["فتح النافذة"] = "Open window",
        ["فتح مجلد البيانات"] = "Open data folder",
        ["فحص التحديثات تلقائياً"] = "Check for updates automatically",
        ["قوة تعتيم الشاشة"] = "Screen dim strength",
        ["لا يوجد — أضف حزمة لغة من إعدادات ويندوز"] = "None — add a language pack in Windows settings",
        ["لا يوجد نص قرب المؤشر — انقر على نص أو اسحب لتحديد منطقة"] = "No text near the cursor — click text or drag an area",
        ["لصق"] = "Paste",
        ["لغات OCR المثبّتة في ويندوز: "] = "OCR languages installed in Windows: ",
        ["لغة المصدر:"] = "Source language:",
        ["لغة قراءة النص (OCR)"] = "OCR language",
        ["لم يتم العثور على نص في هذه المنطقة"] = "No text found in this area",
        ["لم يتم العثور على نص محدد."] = "No selected text found.",
        ["لون التمييز"] = "Accent color",
        ["لون الخلفية (ARGB)"] = "Background color (ARGB)",
        ["لون النص"] = "Text color",
        ["مجلد البيانات: "] = "Data folder: ",
        ["مسح"] = "Clear",
        ["مسح السجل"] = "Clear history",
        ["مسح السجل (المفضلة تبقى)؟"] = "Clear history (favorites are kept)?",
        ["مسح الكاش"] = "Clear cache",
        ["مفاتيح اختيارية (تُخزَّن مشفّرة على جهازك فقط) لرفع الحد بشكل كبير:"] = "Optional keys (stored encrypted on your PC only) to raise the limit a lot:",
        ["نسخ"] = "Copy",
        ["نسخ الترجمة"] = "Copy translation",
        ["نسخ الترجمة تلقائياً"] = "Copy the translation automatically",
        ["ترجمة منطقة"] = "Translate area",
        ["تحت المؤشر"] = "Under cursor",
        ["النص المحدد"] = "Selection",
        ["اللغة المكتشفة"] = "Detected language",
        ["المحرك"] = "Engine",
        ["مستريح"] = "resting",
        ["طلب"] = "requests",
        ["فشل"] = "failed",
        ["الإعدادات — ترجمان سريع"] = "Settings — Tarjuman Sari3",
        ["الترجمة دون إنترنت"] = "Offline translation",
        ["نماذج ترجمة تعمل على جهازك بلا إنترنت ولا حدود (Opus-MT). نزّل الأساس أولاً ثم لغات المصدر التي تحتاجها."] = "Translation models that run on your PC with no internet and no limits (Opus-MT). Download the base first, then the source languages you need.",
        ["تفضيل الترجمة المحلية على الإنترنت (أبطأ قليلاً وأقل دقة، لكن بلا حدود وبخصوصية كاملة)"] = "Prefer local translation over online (slightly slower and less accurate, but unlimited and fully private)",
        ["تنزيل وتثبيت المحدد"] = "Download & install selected",
        ["حذف المحدد"] = "Remove selected",
        ["إلغاء التنزيل"] = "Cancel download",
        ["غير مثبّت"] = "Not installed",
        ["جارٍ التنزيل…"] = "Downloading…",
        ["اكتمل التثبيت."] = "Installation complete.",
        ["أُلغي التنزيل."] = "Download cancelled.",
        ["فشل التنزيل: "] = "Download failed: ",
        ["يتوفر إصدار جديد: "] = "New version available: ",        ["عند النقر على أيقونة التطبيق (بجانب الساعة / الأيقونات المخفية):"] = "When the app icon is clicked (next to the clock / hidden icons):",
        ["نقرة واحدة:"] = "Single click:",
        ["نقرتان:"] = "Double click:",
        ["لا شيء"] = "Nothing",
        ["فتح الإعدادات"] = "Open settings",
        ["تثبيت النافذة فوق كل النوافذ"] = "Keep window on top",        ["ترجمة صورة من الحافظة"] = "Translate image from clipboard",
        ["ترجمة مباشرة بمنطقة جديدة"] = "Live-translate with a new area",
        ["ترجمة صورة من ملف"] = "Translate image from file",
        ["ترجمة صورة"] = "Translate image",
        ["لا توجد صورة في الحافظة (انسخ لقطة شاشة أولاً، مثل Win+Shift+S)."] = "No image on the clipboard (copy a screenshot first, e.g. Win+Shift+S).",
        ["تعذّر فتح الصورة: "] = "Could not open the image: ",
        ["نسخ النص المترجم"] = "Copy translated text",
        ["نسخ الصورة المترجمة"] = "Copy translated image",
        ["إظهار الأصل"] = "Show original",
        ["تم النسخ."] = "Copied.",
        ["تم نسخ الصورة المترجمة."] = "Translated image copied.",
        ["تعذّر نسخ الصورة: "] = "Could not copy the image: ",
        ["لم يتم العثور على نص في الصورة"] = "No text found in the image",
        ["تمت الترجمة"] = "Translated",
        ["استخدام المنطقة المحفوظة للبرنامج تلقائياً عند بدء الترجمة المباشرة"] = "Use the application's saved area automatically when live translation starts",
        ["المناطق المحفوظة للترجمة المباشرة (لكل برنامج). لرسم منطقة جديدة استخدم اختصار «ترجمة مباشرة بمنطقة جديدة»:"] = "Saved live-translation areas (per application). To draw a new area use the “Live-translate with a new area” shortcut:",
        ["حذف المنطقة"] = "Delete area",
        ["حذف الكل"] = "Delete all",        ["إظهار الترجمة"] = "Show translation",
        ["في ترجمة الصور: مطابقة لون خلفية الصورة ولون النص تلقائياً"] = "Image translation: match the picture's background and text colour automatically",
        ["استبدال النص المحدد بترجمته"] = "Replace selected text with its translation",
        ["ترجمة النص بكل المحركات ومقارنتها"] = "Translate the text with every engine and compare",
        ["مقارنة المحركات"] = "Compare engines",
        ["النص:"] = "Text:",
        ["قارن"] = "Compare",
        ["لا يوجد محرك متاح."] = "No engine available.",
        ["محركات أجابت"] = "engines answered",
        ["فشل: "] = "Failed: ",
        ["استخدم هذه"] = "Use this",
        ["أمثلة"] = "Examples",
        ["نطق"] = "Speak",
        ["اسحب صورة هنا لترجمتها"] = "Drop an image here to translate it",
        ["وضع القاموس للكلمة الواحدة (معانٍ متعددة ونطق وأمثلة)"] = "Dictionary mode for single words (meanings, pronunciation, examples)",
        ["اختصار «استبدال النص المحدد» يكتب بلغة:"] = "The “replace selected text” shortcut writes in:",
        ["إذا كان النص المحدد بهذه اللغة أصلاً فيُترجم إلى اللغة الهدف بدلاً من ذلك."] = "If the selected text is already in this language it is translated to the target language instead.",        ["اللوق"] = "Log",
        ["كل الرسائل"] = "All messages",
        ["التحذيرات والأخطاء"] = "Warnings and errors",
        ["الأخطاء فقط"] = "Errors only",
        ["تمرير تلقائي"] = "Auto-scroll",
        ["مسح اللوق"] = "Clear log",
        ["نسخ اللوق"] = "Copy log",
        ["فتح مجلد الملفات"] = "Open files folder",
        ["تصدير CSV"] = "Export CSV",
        ["تم التصدير."] = "Exported.",
        ["تعذّر التصدير: "] = "Export failed: ",
        ["أُضيفت إلى المفضلة"] = "Added to favorites",
        ["خطأ"] = "Error",
        ["تحذير"] = "Warning",
        ["معلومة"] = "Info",
        ["القاموس"] = "Dictionary",
        ["تعذّر النطق: "] = "Speech failed: ",        ["مباشر — جارٍ القراءة…"] = "Live — reading…",
        ["مثبّت"] = "Installed",
        ["عام"] = "General",
        ["دون إنترنت"] = "Offline",
        ["المظهر"] = "Appearance",
        ["الالتقاط و OCR"] = "Capture & OCR",
        ["تلقائي (كل اللغات المثبّتة)"] = "Automatic (all installed languages)",
        ["تبديل المفضلة"] = "Toggle favorite",
        ["صورة من الحافظة"] = "Image from clipboard",
        ["صورة من ملف"] = "Image from file",
        ["مفضلة"] = "Favorite",
        ["مثال: #3B82F6"] = "Example: #3B82F6",
        ["غير متاح في هذه النسخة (لم يُحدَّد مستودع الإصدارات)"] = "Not available in this build (no releases repository set)",
        ["إيقاف"] = "Stop",
        ["نطق / إيقاف"] = "Speak / stop",
        ["افتراضي"] = "Default",
        ["صوت النطق:"] = "Speech voice:",
        ["تلقائي (الأوضح: عبر الإنترنت ثم صوت ويندوز)"] = "Automatic (clearest: online, then the Windows voice)",
        ["عبر الإنترنت فقط (صوت Google)"] = "Online only (Google voice)",
        ["صوت ويندوز المثبّت فقط"] = "Installed Windows voice only",
        ["لا يوجد صوت لهذه اللغة. ثبّت صوتاً من إعدادات ويندوز (الوقت واللغة ← الكلام)."] = "No voice for this language. Install one in Windows settings (Time & language > Speech).",
        ["لا يوجد نص قابل للترجمة في الصورة"] = "No translatable text in the image",
        ["شاشة"] = "Screen",
        ["نص محدد"] = "Selection",
        ["حافظة"] = "Clipboard",
        ["صورة"] = "Image",
        ["مباشر"] = "Live",
        ["استبدال"] = "Replace",
        ["قاموس"] = "Dictionary",
        ["نص"] = "Text",        ["مرّر الماوس فوق أي إعداد أو اختصار لتظهر فائدته."] = "Hover over any setting or shortcut to see what it does.",
        ["البرامج"] = "Programs",
        ["لون الخلفية"] = "Background colour",
        ["إعدادات خاصة بكل برنامج أو لعبة: عندما يكون البرنامج في المقدمة وتضغط أي اختصار تُطبَّق هذه الإعدادات تلقائياً بدل العامة."] = "Settings for each program or game: when it is in the foreground and you press any shortcut these apply automatically instead of the general ones.",
        ["البرنامج"] = "Program",
        ["اللغة الهدف"] = "Target language",
        ["لغة المصدر"] = "Source language",
        ["إضافة / تحديث"] = "Add / update",
        ["اترك الحقل فارغاً (أو «افتراضي») ليبقى على الإعداد العام. لا تنسَ زر «حفظ» أسفل النافذة."] = "Leave a field empty (or “Default”) to keep the general setting. Don't forget the Save button below.",
        ["(افتراضي)"] = "(Default)",
        ["إشعاري عندما يتعطل محرك ويُستخدم غيره"] = "Notify me when an engine fails and another is used",
        ["فحص مفتاح DeepL والاستهلاك"] = "Check DeepL key and usage",
        ["اكتب المفتاح أولاً (من حسابك في DeepL ← API Keys)."] = "Enter the key first (from your DeepL account > API Keys).",
        ["جارٍ الفحص…"] = "Checking…",
        ["المفتاح يعمل"] = "Key works",
        ["المفتاح غير صحيح. تأكد أنه مفتاح API (ينتهي غالباً بـ :fx) وليس رقم الاشتراك."] = "The key is not valid. Make sure it is an API key (usually ending in :fx), not the subscription number.",
        ["تعذّر الفحص: "] = "Check failed: ",
        ["وصل حدّ الاستخدام"] = "reached its usage limit",
        ["غير صالح أو غير متاح"] = "invalid or unavailable",
        ["يُستخدم محرك آخر تلقائياً ({0} ث راحة)."] = "Another engine is used automatically ({0} s rest).",        ["واجهة التطبيق (تُطبَّق بالكامل بعد إعادة التشغيل):"] = "Interface language (fully applied after restart):",
    };
}













