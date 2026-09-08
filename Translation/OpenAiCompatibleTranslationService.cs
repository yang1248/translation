using System.Net.Http.Headers;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using RealtimeTranslator.Models;

namespace RealtimeTranslator.Translation;

public sealed class OpenAiCompatibleTranslationService : ITranslationService
{
    private readonly AppSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public OpenAiCompatibleTranslationService(AppSettings settings)
    {
        _settings = settings;
    }

    public string Name => TranslationProviders.OpenAiCompatible;

    public async Task<string> TranslateAsync(string text, string targetLanguageCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.OpenAiApiKey))
        {
            throw new TranslationException("尚未配置 OpenAI 兼容服务的 API 密钥。");
        }

        var targetName = LanguageCatalog.Find(targetLanguageCode)?.Name ?? targetLanguageCode;
        var systemPrompt =
            $"你是实时字幕翻译器。请把用户提供的原文自动识别语言后翻译成{targetName}。" +
            "只输出一行译文，不要解释、不要加引号、不要输出原文。";

        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.OpenAiApiKey);
        request.Content = JsonContent.Create(new
        {
            model = _settings.OpenAiModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            },
            temperature = 0.1,
            stream = false
        }, options: _jsonOptions);

        try
        {
            using var response = await SharedHttp.Client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationException($"翻译服务返回 {((int)response.StatusCode)}：{TrimBody(body)}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                if (message.TryGetProperty("content", out var content))
                {
                    var result = content.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        return result;
                    }
                }
            }

            throw new TranslationException("翻译服务返回了空结果。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("翻译请求超时，请检查网络或服务状态。");
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException($"无法连接翻译服务：{ex.Message}");
        }
        catch (JsonException ex)
        {
            throw new TranslationException($"翻译服务返回内容无法解析：{ex.Message}");
        }
    }

    private string BuildEndpoint()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.OpenAiBaseUrl)
            ? "https://api.openai.com/v1"
            : _settings.OpenAiBaseUrl.TrimEnd('/');

        return baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + "/chat/completions";
    }

    private static string TrimBody(string body) =>
        body.Length <= 240 ? body : body[..240] + "...";
}
