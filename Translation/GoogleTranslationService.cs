using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RealtimeTranslator.Models;

namespace RealtimeTranslator.Translation;

public sealed class GoogleTranslationService : ITranslationService
{
    private readonly AppSettings _settings;

    public GoogleTranslationService(AppSettings settings)
    {
        _settings = settings;
    }

    public string Name => TranslationProviders.GoogleCloud;

    public async Task<string> TranslateAsync(string text, string targetLanguageCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.GoogleTranslationKey))
        {
            throw new TranslationException("尚未配置 Google Cloud API 密钥。");
        }

        var baseUrl = string.IsNullOrWhiteSpace(_settings.GoogleBaseUrl)
            ? "https://translation.googleapis.com/language/translate/v2"
            : _settings.GoogleBaseUrl;
        var separator = baseUrl.Contains('?') ? "&" : "?";
        var uri = $"{baseUrl}{separator}key={Uri.EscapeDataString(_settings.GoogleTranslationKey)}";

        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Content = JsonContent.Create(new
        {
            q = text,
            target = targetLanguageCode,
            format = "text"
        });

        try
        {
            using var response = await SharedHttp.Client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationException($"Google 翻译返回 {((int)response.StatusCode)}：{TrimBody(body)}");
            }

            using var doc = JsonDocument.Parse(body);
            var translated = doc.RootElement
                .GetProperty("data")
                .GetProperty("translations")[0]
                .GetProperty("translatedText")
                .GetString();
            if (string.IsNullOrWhiteSpace(translated))
            {
                throw new TranslationException("Google 翻译返回了空结果。");
            }

            return WebUtility.HtmlDecode(translated);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("Google 翻译请求超时。");
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException($"无法连接 Google 翻译：{ex.Message}");
        }
        catch (JsonException ex)
        {
            throw new TranslationException($"Google 翻译返回内容无法解析：{ex.Message}");
        }
    }

    private static string TrimBody(string body) =>
        body.Length <= 240 ? body : body[..240] + "...";
}
