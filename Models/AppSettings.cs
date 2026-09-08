namespace RealtimeTranslator.Models;

public enum CaptureMode
{
    Screen = 0,
    Audio = 1
}

public sealed class AppSettings
{
    public CaptureMode Mode { get; set; } = CaptureMode.Screen;
    public string RegionText { get; set; } = string.Empty;

    public string TargetLanguageCode { get; set; } = "zh-CN";
    public string TargetLanguageName { get; set; } = "简体中文";

    public string OcrLanguage { get; set; } = "auto";
    public int ScreenIntervalMs { get; set; } = 500;

    public bool TranslationTopmost { get; set; } = true;
    public double TranslationOpacity { get; set; } = 0.92;

    public int HistoryLimit { get; set; } = 10;

    public string ToggleHotkey { get; set; } = "Ctrl+Alt+T";
    public string ReselectHotkey { get; set; } = "Ctrl+Alt+R";

    public bool StartWithWindows { get; set; }

    public string TranslationProvider { get; set; } = "OpenAI 兼容";
    public string SpeechProvider { get; set; } = "Deepgram";

    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string OpenAiModel { get; set; } = "gpt-4o-mini";
    public string? OpenAiApiKey { get; set; }

    public string AzureTranslatorBaseUrl { get; set; } = "https://api.cognitive.microsofttranslator.com";
    public string? AzureTranslationKey { get; set; }
    public string AzureTranslationRegion { get; set; } = "";

    public string? DeepLTranslationKey { get; set; }
    public string DeepLBaseUrl { get; set; } = "https://api-free.deepl.com/v2/translate";

    public string? GoogleTranslationKey { get; set; }
    public string GoogleBaseUrl { get; set; } = "https://translation.googleapis.com/language/translate/v2";

    public string DeepgramBaseUrl { get; set; } = "wss://api.deepgram.com/v1/listen";
    public string DeepgramModel { get; set; } = "nova-3";
    public string SpeechLanguage { get; set; } = "auto";
    public string? DeepgramApiKey { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public sealed class LanguageOption
{
    public LanguageOption(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; }
    public string Name { get; }

    public override string ToString() => Name;
}

public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageOption> Targets { get; } = new[]
    {
        new LanguageOption("zh-CN", "简体中文"),
        new LanguageOption("zh-TW", "繁體中文"),
        new LanguageOption("en", "English"),
        new LanguageOption("ja", "日本語"),
        new LanguageOption("ko", "한국어"),
        new LanguageOption("fr", "Français"),
        new LanguageOption("de", "Deutsch"),
        new LanguageOption("es", "Español"),
        new LanguageOption("ru", "Русский"),
        new LanguageOption("pt", "Português"),
        new LanguageOption("it", "Italiano"),
        new LanguageOption("ar", "العربية"),
        new LanguageOption("vi", "Tiếng Việt"),
        new LanguageOption("th", "ไทย"),
        new LanguageOption("id", "Bahasa Indonesia")
    };

    public static LanguageOption? Find(string? code) =>
        Targets.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

    public static string ProviderTargetCode(string provider, string targetCode)
    {
        if (string.Equals(provider, "DeepL", StringComparison.OrdinalIgnoreCase))
        {
            return targetCode switch
            {
                "zh-CN" => "ZH-HANS",
                "zh-TW" => "ZH-HANT",
                "en" => "EN-US",
                "pt" => "PT-BR",
                _ => targetCode.ToUpperInvariant()
            };
        }

        return targetCode;
    }
}

public sealed class RegionInfo
{
    public RegionInfo(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public bool IsValid => Width >= 20 && Height >= 20;

    public override string ToString() => $"{Width} x {Height}";
}
