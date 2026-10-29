using System.Windows;
using System.Windows.Controls;
using QuickTranslate.Core.Settings;
using Control = System.Windows.Controls.Control;
using Orientation = System.Windows.Controls.Orientation;

namespace QuickTranslate.App;

/// <summary>Short explanations (Arabic / English) shown as tooltips on every setting and every shortcut.</summary>
internal static class SettingHelp
{
    // x:Name of the control -> (Arabic, English)
    private static readonly Dictionary<string, (string Ar, string En)> ByControl = new()
    {
        // ---- General
        ["UiLangBox"] = ("لغة واجهة البرنامج (عربي أو English). عند تغييرها يُعاد تشغيل التطبيق ليُطبَّق التغيير بالكامل.", "The interface language (Arabic or English). Changing it restarts the app to apply fully."),
        ["TrayClickBox"] = ("الفعل الذي يُنفَّذ عند النقر مرة واحدة على أيقونة التطبيق بجانب الساعة (أو داخل الأيقونات المخفية).", "What happens when you click the app icon once (next to the clock, or inside the hidden icons)."),
        ["TrayDoubleBox"] = ("الفعل عند النقر مرتين على الأيقونة. اتركه «لا شيء» لتُنفَّذ النقرة الواحدة فوراً دون انتظار.", "What happens on a double click. Leave it as “Nothing” so a single click acts immediately without waiting."),
        ["StartWithWindows"] = ("يشغّل التطبيق تلقائياً عند الدخول إلى ويندوز ليكون الاختصار جاهزاً دائماً دون فتحه يدوياً.", "Starts the app when you sign in to Windows so the shortcuts are always ready."),
        ["StartupDelay"] = ("ينتظر هذا العدد من الثواني بعد دخول ويندوز قبل التشغيل، ليخفف الضغط على الجهاز أثناء الإقلاع.", "Waits this many seconds after sign-in before starting, to ease the load while Windows boots."),
        ["StartMinimized"] = ("يبدأ التطبيق مخفياً بجانب الساعة دون فتح النافذة (عند التشغيل مع ويندوز).", "Starts hidden next to the clock without opening the window (when started with Windows)."),
        ["ShowTray"] = ("إظهار أيقونة التطبيق بجانب الساعة. إخفاؤها يعني أن إغلاق النافذة يُنهي التطبيق.", "Shows the app icon next to the clock. If hidden, closing the window quits the app."),
        ["CloseToTray"] = ("زر الإغلاق (×) يُخفي النافذة ويُبقي التطبيق يعمل في الخلفية لتبقى الاختصارات فعّالة.", "The close button (×) hides the window and keeps the app running so the shortcuts keep working."),
        ["RunAdmin"] = ("التشغيل بصلاحية المدير يجعل الاختصارات والالتقاط تعمل أيضاً فوق البرامج والألعاب التي تعمل كمدير. دونه لا تعمل فوقها.", "Running as administrator lets the shortcuts and capture work over apps and games that run as admin. Without it they do not."),
        ["CheckUpdates"] = ("يفحص وجود إصدار جديد من التطبيق عند التشغيل ويُعلمك به.", "Checks for a new version when the app starts and tells you about it."),
        ["AutoClipboard"] = ("يترجم تلقائياً أي نص تنسخه (Ctrl+C) ويعرض الترجمة في نافذة صغيرة. مناسب للقراءة السريعة.", "Automatically translates anything you copy (Ctrl+C) in a small popup. Handy for quick reading."),
        ["SaveHistory"] = ("يحفظ ترجماتك في سجل على جهازك فقط لتعود إليها لاحقاً. أوقفه إن أردت أقصى خصوصية.", "Keeps your translations in a history on this PC only so you can return to them. Turn it off for maximum privacy."),
        ["IgnoredProcs"] = ("أسماء برامج (مثل game) لا تريد أن تعمل الاختصارات داخلها، مفصولة بفواصل.", "Names of programs (like game) in which the shortcuts should not work, separated by commas."),
        ["OpenDataBtn"] = ("يفتح المجلد الذي يخزن الإعدادات والسجل والكاش ونماذج الترجمة المحلية.", "Opens the folder that stores settings, history, cache and offline models."),
        ["ClearCacheBtn"] = ("يمسح الترجمات المحفوظة مؤقتاً؛ ستُطلب الجمل من جديد من المحركات.", "Clears the cached translations; sentences will be requested from the engines again."),

        // ---- Translation
        ["TargetLang"] = ("اللغة التي تُترجم إليها النصوص (العربية افتراضياً).", "The language texts are translated into (Arabic by default)."),
        ["SourceLang"] = ("لغة النص الأصلي. «كشف تلقائي» يعرفها بنفسه وهو الأنسب غالباً.", "The language of the original text. “Auto-detect” works it out itself and is usually best."),
        ["RotateEngines"] = ("يوزّع الطلبات بالتناوب على المحركات المتاحة حتى لا يصل أحدها لحد الاستخدام، فيرتفع الحد العملي كثيراً.", "Spreads requests across the available engines so none hits its limit, greatly raising the practical limit."),
        ["UseCache"] = ("يحفظ كل جملة مترجمة ولا يعيد طلبها من الإنترنت، فتصبح أسرع وتستهلك الحد أقل.", "Remembers every translated sentence and never asks the internet again, so it is faster and uses less of your limit."),
        ["SpeechBox"] = ("مصدر صوت النطق: صوت الإنترنت أوضح غالباً، وصوت ويندوز يعمل دون إنترنت لكن جودته أقل.", "Where the speech voice comes from: the online voice is usually clearer; the Windows voice works offline but sounds worse."),
        ["DictionaryMode"] = ("عند ترجمة كلمة واحدة يعرض معانيها حسب نوع الكلمة ونطقها وأمثلة، بدل ترجمة واحدة فقط.", "For a single word, shows its meanings by part of speech, pronunciation and examples instead of one translation."),
        ["ReplaceLang"] = ("اللغة التي يكتب بها اختصار «استبدال النص المحدد». مثال: تكتب بالعربية فيحوّلها إلى الإنجليزية في مكانها.", "The language the “replace selected text” shortcut writes in. Example: you type Arabic and it turns it into English in place."),
        ["EngineList"] = ("المحركات تُجرَّب من الأعلى إلى الأسفل. أزل العلامة لتعطيل محرك، ورتّبها بالأسهم.", "Engines are tried from top to bottom. Untick one to disable it, and reorder with the arrows."),
        ["EngineUp"] = ("ينقل المحرك المحدد إلى أعلى (أولوية أعلى).", "Moves the selected engine up (higher priority)."),
        ["EngineDown"] = ("ينقل المحرك المحدد إلى أسفل (أولوية أقل).", "Moves the selected engine down (lower priority)."),
        ["RefreshStatus"] = ("يحدّث عدد الطلبات والأخطاء وحالة راحة كل محرك.", "Refreshes each engine's request count, failures and rest state."),
        ["DeepLKey"] = ("مفتاح DeepL (المجاني 500 ألف إلى مليون حرف شهرياً) لترجمة أدق وحد أعلى. يُخزَّن مشفّراً على جهازك فقط.", "A DeepL key (the free plan gives 500k to 1M characters a month) for better quality and a higher limit. Stored encrypted on this PC only."),
        ["AzureKey"] = ("مفتاح Azure Translator المجاني (مليونا حرف شهرياً). يُخزَّن مشفّراً على جهازك فقط.", "A free Azure Translator key (2M characters a month). Stored encrypted on this PC only."),
        ["AzureRegion"] = ("منطقة خدمة Azure (مثل westeurope) كما تظهر في بوابة Azure.", "Your Azure service region (such as westeurope) as shown in the Azure portal."),
        ["LibreUrl"] = ("عنوان خادم LibreTranslate (شخصي أو عام) لاستخدامه كمحرك إضافي.", "The address of a LibreTranslate server (your own or public) to use as an extra engine."),
        ["LibreKey"] = ("مفتاح خادم LibreTranslate إن كان يتطلب مفتاحاً.", "The LibreTranslate server key, if it requires one."),
        ["DeepLTest"] = ("يتحقق أن مفتاح DeepL صحيح ويعرض كم حرفاً استهلكت من حصتك الشهرية. لاحظ: المفتاح غير رقم الاشتراك.", "Checks that the DeepL key is valid and shows how many characters of your monthly quota you used. Note: the key is not the subscription number."),
        ["NotifyFallback"] = ("يُظهر إشعاراً قصيراً عندما يرفض محرك الطلبات (حد استخدام) ويُستبدل بغيره تلقائياً، فتعرف سبب تغيّر الجودة.", "Shows a short notice when an engine refuses requests (usage limit) and another one takes over, so you know why quality changed."),
        ["ProfileList"] = ("البرامج التي لها إعدادات خاصة. اختر واحداً لتعديله.", "Programs that have their own settings. Pick one to edit it."),
        ["ProfProcess"] = ("اسم عملية البرنامج (مثل chrome أو game). اختر من البرامج المفتوحة حالياً أو اكتبه.", "The program's process name (like chrome or game). Pick from the open programs or type it."),
        ["ProfTarget"] = ("لغة الترجمة لهذا البرنامج فقط، مثلاً الإنجليزية في برنامج المحادثة والعربية في اللعبة.", "The translation language for this program only, e.g. English in a chat app and Arabic in a game."),
        ["ProfSource"] = ("لغة النص الأصلي في هذا البرنامج إن كانت ثابتة (يسرّع الكشف ويزيد الدقة).", "The source language in this program if it never changes (faster and more accurate)."),
        ["ProfFontSize"] = ("حجم خط الترجمة لهذا البرنامج، مثلاً 24 لعرض أكبر في الألعاب.", "The translation font size for this program, e.g. 24 for larger text in games."),
        ["ProfTextColor"] = ("لون نص الترجمة لهذا البرنامج، مثل #FFFFFF.", "The translation text colour for this program, like #FFFFFF."),
        ["ProfBg"] = ("لون خلفية لوحة الترجمة لهذا البرنامج (ARGB).", "The translation panel background for this program (ARGB)."),
        ["ProfSave"] = ("يضيف البرنامج أو يحدّث إعداداته. يُطبَّق بعد ضغط «حفظ».", "Adds the program or updates its settings. Applied after you press Save."),
        ["ProfDelete"] = ("يحذف إعدادات البرنامج المحدد فيعود للإعداد العام.", "Deletes the selected program's settings so it uses the general ones again."),
        ["GlossaryBox"] = ("قاموسك الشخصي: اكتب في كل سطر كلمة=ترجمتها لفرض ترجمة معينة لمصطلحاتك.", "Your personal glossary: write word=translation on each line to force a specific translation for your terms."),

        // ---- Offline
        ["PreferOffline"] = ("يجعل الترجمة المحلية أولى دائماً: بلا إنترنت وبلا حدود وبخصوصية كاملة، لكنها أبطأ وأقل دقة.", "Makes local translation first: no internet, no limits and full privacy, but slower and less accurate."),
        ["PackList"] = ("نماذج اللغات. نزّل «الأساس» (إنجليزي ⇄ عربي) أولاً، ثم لغات المصدر التي تحتاجها.", "Language models. Download “Base” (English ⇄ Arabic) first, then the source languages you need."),
        ["PackInstall"] = ("ينزّل النموذج المحدد (نحو 105 ميغابايت لكل لغة) ويثبّته للاستخدام دون إنترنت.", "Downloads the selected model (about 105 MB per language) for offline use."),
        ["PackRemove"] = ("يحذف النموذج المحدد من جهازك لتوفير المساحة.", "Deletes the selected model from your PC to free space."),
        ["PackCancel"] = ("يوقف التنزيل الجاري.", "Stops the current download."),

        // ---- Appearance
        ["ThemeBox"] = ("مظهر التطبيق: فاتح أو داكن أو حسب إعداد ويندوز.", "App look: light, dark or follow Windows."),
        ["AccentBox"] = ("اللون المميز للإطارات والأزرار، بصيغة مثل #3B82F6.", "The accent colour for frames and highlights, like #3B82F6."),
        ["FontBox"] = ("خط النص المترجم الظاهر فوق الشاشة وفي النوافذ العائمة.", "The font of the translated text shown over the screen and in popups."),
        ["FontSizeSlider"] = ("الحجم الأساسي للنص المترجم. قد يصغر تلقائياً ليناسب السطر الأصلي.", "The base size of translated text. It may shrink automatically to fit the original line."),
        ["TextColorBox"] = ("لون النص المترجم، مثل #FFFFFF.", "The colour of translated text, like #FFFFFF."),
        ["BgColorBox"] = ("لون خلفية لوحة الترجمة بصيغة ARGB، مثل #E6101827 (أول رقمين للشفافية).", "The translation panel background in ARGB, like #E6101827 (the first two digits are transparency)."),
        ["OpacitySlider"] = ("شفافية لوحة الترجمة: الأقل يُظهر ما تحتها أكثر.", "Translation panel opacity: lower shows more of what is underneath."),
        ["DimSlider"] = ("مقدار تعتيم الشاشة أثناء التحديد ليظهر النص بوضوح.", "How much the screen is dimmed during selection so the text stands out."),
        ["RadiusSlider"] = ("استدارة زوايا لوحات الترجمة.", "How rounded the translation panels' corners are."),
        ["ShowOriginal"] = ("يعرض بطاقة صغيرة بالنص الأصلي مع زر لنسخ الترجمة.", "Shows a small card with the original text and a button to copy the translation."),
        ["Animations"] = ("حركات ظهور ناعمة للنتائج. أوقفها لأداء أخف.", "Soft fade-in animations for results. Turn off for lighter performance."),
        ["ImageMatchBackground"] = ("في ترجمة الصور: يأخذ لون الخلفية حول كل سطر ليبدو النص المترجم جزءاً من الصورة.", "In image translation: picks the background colour around each line so the translation blends into the picture."),
        ["AutoCopy"] = ("ينسخ الترجمة إلى الحافظة تلقائياً بعد كل ترجمة.", "Copies the translation to the clipboard automatically after each translation."),
        ["AutoClose"] = ("يغلق نافذة النتيجة تلقائياً بعد هذا العدد من الثواني. 0 تعني أن تبقى حتى تنقر.", "Closes the result window automatically after this many seconds. 0 keeps it until you click."),

        // ---- Capture & OCR
        ["ModeBox"] = ("طريقة اختصار «ترجمة منطقة»: ترجمة النص القريب من المؤشر فوراً، أو سحب مستطيل لتحديد منطقة بنفسك.", "How the “translate area” shortcut works: instantly translate the text near the cursor, or drag a rectangle yourself."),
        ["OcrLangBox"] = ("لغة قراءة النص من الصور. «تلقائي» يجرّب كل اللغات المثبّتة في ويندوز ويختار الأفضل.", "The language used to read text from images. “Automatic” tries every language installed in Windows and picks the best."),
        ["Upscale"] = ("يكبّر المناطق والصور الصغيرة قبل قراءتها، فتزداد دقة قراءة الخط الصغير.", "Enlarges small areas and images before reading them, which makes small text far more accurate."),
        ["MergeSlider"] = ("كم يُدمج من أسطر متقاربة في فقرة واحدة: أعلى = دمج أكثر.", "How many neighbouring lines are merged into one paragraph: higher = more merging."),
        ["LiveInterval"] = ("كل كم مللي ثانية تُعاد قراءة المنطقة في الترجمة المباشرة. الأقل أسرع لكنه أثقل على الجهاز.", "How often (in milliseconds) the area is re-read in live translation. Lower is faster but heavier."),
        ["LiveReuse"] = ("عند بدء الترجمة المباشرة داخل برنامج سبق أن حددت له منطقة، تُستخدم تلك المنطقة فوراً دون رسمها من جديد.", "Starting live translation inside an app you already chose an area for reuses it instantly, without redrawing."),
        ["RegionList"] = ("المناطق التي حفظتها للترجمة المباشرة، منطقة لكل برنامج.", "The areas you saved for live translation, one per application."),
        ["RegionDelete"] = ("يحذف المنطقة المحددة.", "Deletes the selected area."),
        ["RegionClear"] = ("يحذف كل المناطق المحفوظة.", "Deletes all saved areas."),
    };

    private static readonly Dictionary<HotkeyAction, (string Ar, string En)> ByHotkey = new()
    {
        [HotkeyAction.TranslateRegion] = ("اسحب مستطيلاً على الشاشة فيُقرأ النص بداخله ويُعرض مترجماً فوقه.", "Drag a rectangle on the screen; the text inside is read and shown translated on top of it."),
        [HotkeyAction.TranslateUnderCursor] = ("يترجم فوراً النص الأقرب لمؤشر الماوس (فقرة كاملة) دون تحديد.", "Instantly translates the text nearest the mouse pointer (a whole paragraph) with no selecting."),
        [HotkeyAction.RepeatLastRegion] = ("يعيد قراءة وترجمة آخر منطقة ترجمتها. مفيد للحوارات المتغيرة في الألعاب.", "Re-reads and re-translates the last area you translated. Handy for changing dialogue in games."),
        [HotkeyAction.TranslateSelection] = ("يترجم النص المحدد في أي برنامج ويعرضه في نافذة صغيرة عند المؤشر.", "Translates the text selected in any program and shows it in a small popup at the cursor."),
        [HotkeyAction.TranslateClipboard] = ("يترجم النص المنسوخ حالياً في الحافظة.", "Translates the text currently on the clipboard."),
        [HotkeyAction.ToggleMainWindow] = ("يُظهر نافذة التطبيق الرئيسية أو يُخفيها.", "Shows or hides the main window."),
        [HotkeyAction.LiveTranslate] = ("يترجم منطقة ثابتة من الشاشة باستمرار (ترجمات الأفلام والألعاب) ويحفظ المنطقة لكل برنامج. اضغطه مرة أخرى للإيقاف.", "Continuously translates a fixed screen area (movie subtitles, games) and remembers the area per program. Press again to stop."),
        [HotkeyAction.LiveTranslatePickNew] = ("يبدأ الترجمة المباشرة مع رسم منطقة جديدة بدل المحفوظة.", "Starts live translation and lets you draw a new area instead of the saved one."),
        [HotkeyAction.TranslateClipboardImage] = ("يترجم صورة منسوخة (مثل لقطة Win+Shift+S) ويعرض الترجمة فوق الصورة.", "Translates a copied image (such as a Win+Shift+S screenshot) and shows the translation over it."),
        [HotkeyAction.ReplaceSelection] = ("يستبدل النص المحدد بترجمته في مكانه. مفيد لكتابة رسائل بلغة أخرى.", "Replaces the selected text with its translation right where it is. Great for writing messages in another language."),
    };

    public static string ForHotkey(HotkeyAction action) =>
        ByHotkey.TryGetValue(action, out var h) ? (Loc.English ? h.En : h.Ar) : "";

    /// <summary>Attaches a wrapped tooltip to each named control and to the label(s) beside it.</summary>
    public static void Apply(FrameworkElement root)
    {
        ToolTipService.SetShowDuration(root, 30000);
        ToolTipService.SetInitialShowDelay(root, 250);
        foreach (var (name, help) in ByControl)
        {
            if (root.FindName(name) is not FrameworkElement control) continue;
            var text = Loc.English ? help.En : help.Ar;
            Attach(control, text);
            foreach (var label in LabelsBeside(control)) Attach(label, text);
        }
    }

    public static System.Windows.Controls.ToolTip Make(string text) => new()
    {
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
        Padding = new Thickness(10, 8, 10, 8),
    };

    private static void Attach(FrameworkElement element, string text)
    {
        element.ToolTip = Make(text);
        ToolTipService.SetShowDuration(element, 30000);
        ToolTipService.SetInitialShowDelay(element, 250);
        if (element is Control { IsEnabled: false }) ToolTipService.SetShowOnDisabled(element, true);
    }

    /// <summary>TextBlocks that act as the label of a control (same Grid row, or siblings in a horizontal stack).</summary>
    private static IEnumerable<FrameworkElement> LabelsBeside(FrameworkElement control)
    {
        switch (LogicalTreeHelper.GetParent(control))
        {
            case Grid grid:
                var row = Grid.GetRow(control);
                foreach (var child in grid.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == row)) yield return child;
                break;
            case StackPanel { Orientation: Orientation.Horizontal } stack:
                foreach (var child in stack.Children.OfType<TextBlock>()) yield return child;
                break;
        }
    }
}



