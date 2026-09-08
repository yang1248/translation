using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RealtimeTranslator.Models;

namespace RealtimeTranslator.Translation;

public sealed class AzureTranslatorService : ITranslationService
{
    private readonly AppSettings _settings;

    public AzureTranslatorService(AppSettings settings)
    {
        _settings = settings;
    }

    public string Name => TranslationProviders.MicrosoftAzure;

    public async Task<string> TranslateAsync(string text, string targetLanguageCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.AzureTranslationKey))
        {
            throw new TranslationException("尚未配置 Microsoft Azure 翻译密钥。");
        }

        var target = targetLanguageCode switch
        {
            "zh-CN" => "zh-Hans",
            "zh-TW" => "zh-Hant",
            _ => targetLanguageCode
        };
        var baseUrl = string.IsNullOrWhiteSpace(_settings.AzureTranslatorBaseUrl)
            ? "https://api.cognitive.microsofttranslator.com"
            : _settings.AzureTranslatorBaseUrl.TrimEnd('/');
        var uri = $"{baseUrl}/translate?api-version=3.0&to={Uri.EscapeDataString(target)}";

        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _settings.AzureTranslationKey);
        if (!string.IsNullOrWhiteSpace(_settings.AzureTranslationRegion))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Region", _settings.AzureTranslationRegion);
        }
        request.Content = JsonContent.Create(new[] { new { Text = text } });

        try
        {
            using var response = await SharedHttp.Client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationException($"Azure 翻译返回 {((int)response.StatusCode)}：{TrimBody(body)}");
            }

            using var doc = JsonDocument.Parse(body);
            var translations = doc.RootElement[0].GetProperty("translations");
            var translated = translations[0].GetProperty("text").GetString();
            return string.IsNullOrWhiteSpace(translated) ? throw new TranslationException("Azure 翻译返回了空结果。") : translated;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("Azure 翻译请求超时。");
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException($"无法连接 Azure 翻译服务：{ex.Message}");
        }
        catch (JsonException ex)
        {
            throw new TranslationException($"Azure 翻译返回内容无法解析：{ex.Message}");
        }
    }

    private static string TrimBody(string body) =>
        body.Length <= 240 ? body : body[..240] + "...";
}
