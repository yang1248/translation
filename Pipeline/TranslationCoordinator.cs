using RealtimeTranslator.Models;
using RealtimeTranslator.Services;
using RealtimeTranslator.Translation;

namespace RealtimeTranslator.Pipeline;

public sealed class TranslationCoordinator
{
    private readonly TranslationProviderFactory _factory = new();
    private readonly HistoryService _history;
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _lastSourceByMode = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private CancellationTokenSource? _activeRequest;
    private int _version;

    public TranslationCoordinator(AppSettings settings, HistoryService history)
    {
        _settings = settings;
        _history = history;
    }

    public event Action<CurrentTranslation>? CurrentTranslationChanged;
    public event Action<string>? StatusMessage;

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _history.Limit = settings.HistoryLimit;
    }

    public void SubmitSourceLine(string sourceText, string sourceMode)
    {
        var normalized = Normalize(sourceText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        lock (_lock)
        {
            if (_lastSourceByMode.TryGetValue(sourceMode, out var last) &&
                string.Equals(last, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _lastSourceByMode[sourceMode] = normalized;
            _activeRequest?.Cancel();
            _activeRequest?.Dispose();
            _activeRequest = new CancellationTokenSource();
            var version = ++_version;
            var cts = _activeRequest;

            CurrentTranslationChanged?.Invoke(new CurrentTranslation(
                sourceText,
                "翻译中...",
                isWorking: true,
                error: null));

            _ = RunTranslationAsync(sourceText, sourceMode, version, cts.Token);
        }
    }

    public void CancelPending()
    {
        lock (_lock)
        {
            _activeRequest?.Cancel();
            _activeRequest?.Dispose();
            _activeRequest = null;
            _version++;
        }
    }

    private async Task RunTranslationAsync(
        string sourceText,
        string sourceMode,
        int version,
        CancellationToken cancellationToken)
    {
        AppSettings snapshot;
        string targetLanguage;
        string providerName;
        lock (_lock)
        {
            snapshot = _settings;
            targetLanguage = _settings.TargetLanguageCode;
            providerName = _settings.TranslationProvider;
        }

        try
        {
            var service = _factory.Create(snapshot);
            var translated = await service.TranslateAsync(sourceText, targetLanguage, cancellationToken);

            lock (_lock)
            {
                if (version != _version)
                {
                    return;
                }
                _activeRequest = null;
            }

            var modeLabel = sourceMode switch
            {
                "Audio" or "声音" => "电脑声音",
                _ => "屏幕字幕"
            };
            _history.Add(sourceText, translated, modeLabel);
            CurrentTranslationChanged?.Invoke(new CurrentTranslation(
                sourceText,
                translated,
                isWorking: false,
                error: null));
        }
        catch (OperationCanceledException)
        {
            // A newer subtitle superseded this request.
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                if (version != _version)
                {
                    return;
                }
                _activeRequest = null;
            }

            var message = ex is TranslationException te
                ? te.Message
                : $"翻译失败：{ex.Message}";
            CurrentTranslationChanged?.Invoke(new CurrentTranslation(
                sourceText,
                "翻译失败",
                isWorking: false,
                error: message));
            StatusMessage?.Invoke($"翻译失败：{providerName}。{message}");
        }
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex
            .Replace(text, @"[\s\u3000]+", string.Empty)
            .ToLowerInvariant();
    }
}
