using System.Net;
using System.Text;
using System.Text.Json;
using RealtimeTranslator.Models;

namespace RealtimeTranslator.Translation;

public sealed class DeepLTranslationService : ITranslationService
{
    private readonly AppSettings _settings;

    public DeepLTranslationService(AppSettings settings)
    {
        _settings = settings;
    }

    public string Name => TranslationProviders.DeepL;

    public async Task<string> TranslateAsync(string text, string targetLanguageCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.DeepLTranslationKey))
        {
            throw new TranslationException("尚未配置 DeepL API 密钥。");
        }

        var target = LanguageCatalog.ProviderTargetCode(TranslationProviders.DeepL, targetLanguageCode);
        var endpoint = string.IsNullOrWhiteSpace(_settings.DeepLBaseUrl)
            ? "https://api-free.deepl.com/v2/translate"
            : _settings.DeepLBaseUrl;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["auth_key"] = _settings.DeepLTranslationKey,
            ["text"] = text,
            ["target_lang"] = target
        });

        try
        {
            using var response = await SharedHttp.Client.PostAsync(endpoint, form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationException($"DeepL 返回 {((int)response.StatusCode)}：{TrimBody(body)}");
            }

            using var doc = JsonDocument.Parse(body);
            var translated = doc.RootElement.GetProperty("translations")[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(translated))
            {
                throw new TranslationException("DeepL 返回了空结果。");
            }

            return WebUtility.HtmlDecode(translated);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("DeepL 请求超时。");
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException($"无法连接 DeepL：{ex.Message}");
        }
        catch (JsonException ex)
        {
            throw new TranslationException($"DeepL 返回内容无法解析：{ex.Message}");
        }
    }

    private static string TrimBody(string body) =>
        body.Length <= 240 ? body : body[..240] + "...";
}
