using RealtimeTranslator.Models;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace RealtimeTranslator.Capture;

public static class OcrLanguageCatalog
{
    public static IReadOnlyList<LanguageOption> GetAvailable()
    {
        try
        {
            var list = new List<LanguageOption>
            {
                new("auto", "自动选择（推荐）")
            };

            foreach (var language in OcrEngine.AvailableRecognizerLanguages)
            {
                list.Add(new LanguageOption(language.LanguageTag, language.DisplayName));
            }

            return list.DistinctBy(x => x.Code).ToArray();
        }
        catch
        {
            return new[]
            {
                new LanguageOption("auto", "自动选择（推荐）")
            };
        }
    }

    public static OcrEngine? CreateEngine(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag) || languageTag == "auto")
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }

        try
        {
            var language = new Language(languageTag);
            return OcrEngine.TryCreateFromLanguage(language);
        }
        catch
        {
            return null;
        }
    }
}
