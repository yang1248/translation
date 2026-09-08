using RealtimeTranslator.Models;

namespace RealtimeTranslator.Translation;

public sealed class TranslationProviderFactory
{
    public ITranslationService Create(AppSettings settings)
    {
        return settings.TranslationProvider switch
        {
            TranslationProviders.MicrosoftAzure => new AzureTranslatorService(settings),
            TranslationProviders.DeepL => new DeepLTranslationService(settings),
            TranslationProviders.GoogleCloud => new GoogleTranslationService(settings),
            _ => new OpenAiCompatibleTranslationService(settings)
        };
    }
}
