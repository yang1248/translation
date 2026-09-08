using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Text.Json;
using RealtimeTranslator.Models;

namespace RealtimeTranslator.Configuration;

public sealed class SettingsService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RealtimeTranslator.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configDirectory;
    private readonly string _filePath;
    private readonly object _lock = new();

    public SettingsService()
    {
        _configDirectory = Environment.GetEnvironmentVariable("REALTIME_TRANSLATOR_CONFIG_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RealtimeTranslator");
        _filePath = Path.Combine(_configDirectory, "settings.json");
    }

    public string ConfigDirectory => _configDirectory;

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                var dto = JsonSerializer.Deserialize<SettingsDto>(json, JsonOptions) ?? new SettingsDto();
                var settings = dto.ToSettings();
                settings.OcrLanguage = string.IsNullOrWhiteSpace(settings.OcrLanguage) ? "auto" : settings.OcrLanguage;
                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(_configDirectory);
            var dto = SettingsDto.FromSettings(settings);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, JsonOptions), new UTF8Encoding(false));
        }
    }

    private static string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }
    }

    private static string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class SettingsDto
    {
        public int Mode { get; set; }
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
        public string? OpenAiApiKeyEncrypted { get; set; }
        public string AzureTranslatorBaseUrl { get; set; } = "https://api.cognitive.microsofttranslator.com";
        public string? AzureTranslationKeyEncrypted { get; set; }
        public string AzureTranslationRegion { get; set; } = "";
        public string? DeepLTranslationKeyEncrypted { get; set; }
        public string DeepLBaseUrl { get; set; } = "https://api-free.deepl.com/v2/translate";
        public string? GoogleTranslationKeyEncrypted { get; set; }
        public string GoogleBaseUrl { get; set; } = "https://translation.googleapis.com/language/translate/v2";
        public string DeepgramBaseUrl { get; set; } = "wss://api.deepgram.com/v1/listen";
        public string DeepgramModel { get; set; } = "nova-3";
        public string SpeechLanguage { get; set; } = "auto";
        public string? DeepgramApiKeyEncrypted { get; set; }

        public static SettingsDto FromSettings(AppSettings settings) => new()
        {
            Mode = (int)settings.Mode,
            TargetLanguageCode = settings.TargetLanguageCode,
            TargetLanguageName = settings.TargetLanguageName,
            OcrLanguage = settings.OcrLanguage,
            ScreenIntervalMs = settings.ScreenIntervalMs,
            TranslationTopmost = settings.TranslationTopmost,
            TranslationOpacity = settings.TranslationOpacity,
            HistoryLimit = settings.HistoryLimit,
            ToggleHotkey = settings.ToggleHotkey,
            ReselectHotkey = settings.ReselectHotkey,
            StartWithWindows = settings.StartWithWindows,
            TranslationProvider = settings.TranslationProvider,
            SpeechProvider = settings.SpeechProvider,
            OpenAiBaseUrl = settings.OpenAiBaseUrl,
            OpenAiModel = settings.OpenAiModel,
            OpenAiApiKeyEncrypted = Protect(settings.OpenAiApiKey),
            AzureTranslatorBaseUrl = settings.AzureTranslatorBaseUrl,
            AzureTranslationKeyEncrypted = Protect(settings.AzureTranslationKey),
            AzureTranslationRegion = settings.AzureTranslationRegion,
            DeepLTranslationKeyEncrypted = Protect(settings.DeepLTranslationKey),
            DeepLBaseUrl = settings.DeepLBaseUrl,
            GoogleTranslationKeyEncrypted = Protect(settings.GoogleTranslationKey),
            GoogleBaseUrl = settings.GoogleBaseUrl,
            DeepgramBaseUrl = settings.DeepgramBaseUrl,
            DeepgramModel = settings.DeepgramModel,
            SpeechLanguage = settings.SpeechLanguage,
            DeepgramApiKeyEncrypted = Protect(settings.DeepgramApiKey)
        };

        public AppSettings ToSettings() => new()
        {
            Mode = Enum.IsDefined(typeof(CaptureMode), Mode) ? (CaptureMode)Mode : CaptureMode.Screen,
            TargetLanguageCode = TargetLanguageCode,
            TargetLanguageName = TargetLanguageName,
            OcrLanguage = OcrLanguage,
            ScreenIntervalMs = ScreenIntervalMs is >= 150 and <= 3000 ? ScreenIntervalMs : 500,
            TranslationTopmost = TranslationTopmost,
            TranslationOpacity = TranslationOpacity is >= 0.2 and <= 1 ? TranslationOpacity : 0.92,
            HistoryLimit = HistoryLimit is >= 1 and <= 500 ? HistoryLimit : 10,
            ToggleHotkey = ToggleHotkey,
            ReselectHotkey = ReselectHotkey,
            StartWithWindows = StartWithWindows,
            TranslationProvider = TranslationProvider,
            SpeechProvider = SpeechProvider,
            OpenAiBaseUrl = OpenAiBaseUrl,
            OpenAiModel = OpenAiModel,
            OpenAiApiKey = Unprotect(OpenAiApiKeyEncrypted),
            AzureTranslatorBaseUrl = AzureTranslatorBaseUrl,
            AzureTranslationKey = Unprotect(AzureTranslationKeyEncrypted),
            AzureTranslationRegion = AzureTranslationRegion,
            DeepLTranslationKey = Unprotect(DeepLTranslationKeyEncrypted),
            DeepLBaseUrl = DeepLBaseUrl,
            GoogleTranslationKey = Unprotect(GoogleTranslationKeyEncrypted),
            GoogleBaseUrl = GoogleBaseUrl,
            DeepgramBaseUrl = DeepgramBaseUrl,
            DeepgramModel = DeepgramModel,
            SpeechLanguage = SpeechLanguage,
            DeepgramApiKey = Unprotect(DeepgramApiKeyEncrypted)
        };
    }
}
