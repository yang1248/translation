namespace RealtimeTranslator.Models;

public sealed class HistoryEntry
{
    public required int Index { get; set; }
    public required DateTime Time { get; set; }
    public required string SourceText { get; set; }
    public required string TranslatedText { get; set; }
    public required string SourceMode { get; set; }
}

public sealed class CurrentTranslation
{
    public CurrentTranslation(string sourceText, string translatedText, bool isWorking, string? error)
    {
        SourceText = sourceText;
        TranslatedText = translatedText;
        IsWorking = isWorking;
        Error = error;
    }

    public string SourceText { get; }
    public string TranslatedText { get; }
    public bool IsWorking { get; }
    public string? Error { get; }
}

public static class DisplayState
{
    public const string Ready = "就绪";
    public const string RunningScreen = "正在识别屏幕字幕";
    public const string RunningAudio = "正在翻译电脑声音";
    public const string Paused = "已暂停";
    public const string Starting = "正在连接";
    public const string Error = "运行异常";
}
