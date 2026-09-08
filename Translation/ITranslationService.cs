using System.Net.Http;

namespace RealtimeTranslator.Translation;

public interface ITranslationService
{
    string Name { get; }

    Task<string> TranslateAsync(string text, string targetLanguageCode, CancellationToken cancellationToken);
}

public static class TranslationProviders
{
    public const string OpenAiCompatible = "OpenAI 兼容";
    public const string MicrosoftAzure = "Microsoft Azure";
    public const string DeepL = "DeepL";
    public const string GoogleCloud = "Google Cloud";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        OpenAiCompatible,
        MicrosoftAzure,
        DeepL,
        GoogleCloud
    };
}

public sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message)
    {
    }

    public TranslationException(string message, Exception inner) : base(message, inner)
    {
    }
}

internal static class SharedHttp
{
    internal static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };
}
