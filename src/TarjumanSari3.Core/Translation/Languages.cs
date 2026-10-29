namespace TarjumanSari3.Core.Translation;

public sealed record Language(string Code, string Arabic, string English)
{
    public override string ToString() => Languages.UseEnglish ? English : $"{Arabic} ({English})";
}

public static class Languages
{
    /// <summary>Set when the UI language is English so pickers show English names only.</summary>
    public static bool UseEnglish { get; set; }

    public static readonly Language Auto = new("auto", "كشف تلقائي", "Auto-detect");

    public static readonly IReadOnlyList<Language> All = new List<Language>
    {
        new("ar", "العربية", "Arabic"), new("en", "الإنجليزية", "English"), new("fr", "الفرنسية", "French"),
        new("es", "الإسبانية", "Spanish"), new("de", "الألمانية", "German"), new("it", "الإيطالية", "Italian"),
        new("pt", "البرتغالية", "Portuguese"), new("ru", "الروسية", "Russian"), new("tr", "التركية", "Turkish"),
        new("fa", "الفارسية", "Persian"), new("ur", "الأردية", "Urdu"), new("hi", "الهندية", "Hindi"),
        new("bn", "البنغالية", "Bengali"), new("id", "الإندونيسية", "Indonesian"), new("ms", "الملايو", "Malay"),
        new("ja", "اليابانية", "Japanese"), new("ko", "الكورية", "Korean"), new("zh-CN", "الصينية (مبسطة)", "Chinese (Simplified)"),
        new("zh-TW", "الصينية (تقليدية)", "Chinese (Traditional)"), new("th", "التايلاندية", "Thai"),
        new("vi", "الفيتنامية", "Vietnamese"), new("nl", "الهولندية", "Dutch"), new("pl", "البولندية", "Polish"),
        new("uk", "الأوكرانية", "Ukrainian"), new("cs", "التشيكية", "Czech"), new("sv", "السويدية", "Swedish"),
        new("no", "النرويجية", "Norwegian"), new("da", "الدنماركية", "Danish"), new("fi", "الفنلندية", "Finnish"),
        new("el", "اليونانية", "Greek"), new("he", "العبرية", "Hebrew"), new("ro", "الرومانية", "Romanian"),
        new("hu", "المجرية", "Hungarian"), new("bg", "البلغارية", "Bulgarian"), new("sr", "الصربية", "Serbian"),
        new("hr", "الكرواتية", "Croatian"), new("sk", "السلوفاكية", "Slovak"), new("ca", "الكتالونية", "Catalan"),
        new("sw", "السواحيلية", "Swahili"), new("tl", "الفلبينية", "Filipino"),
    };

    public static IReadOnlyList<Language> WithAuto { get; } = new[] { Auto }.Concat(All).ToList();

    public static Language Find(string code) => WithAuto.FirstOrDefault(l => l.Code == code) ?? new(code, code, code);
}

