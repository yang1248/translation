using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using RealtimeTranslator.Models;

namespace RealtimeTranslator.Services;

public sealed class HistoryService
{
    private readonly ObservableCollection<HistoryEntry> _items = new();
    private int _limit;
    private int _sequence;

    public HistoryService(int limit)
    {
        _limit = Math.Clamp(limit, 1, 500);
    }

    public ObservableCollection<HistoryEntry> Items => _items;

    public int Limit
    {
        get => _limit;
        set
        {
            _limit = Math.Clamp(value, 1, 500);
            while (_items.Count > _limit)
            {
                _items.RemoveAt(0);
            }
        }
    }

    public void Add(string sourceText, string translatedText, string sourceMode)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(translatedText))
        {
            return;
        }

        _sequence++;
        _items.Add(new HistoryEntry
        {
            Index = _sequence,
            Time = DateTime.Now,
            SourceText = sourceText,
            TranslatedText = translatedText,
            SourceMode = sourceMode
        });

        while (_items.Count > _limit)
        {
            _items.RemoveAt(0);
        }
    }

    public void Clear()
    {
        _items.Clear();
    }

    public async Task ExportAsync(string filePath)
    {
        var entries = _items.ToArray();
        var builder = new StringBuilder();
        foreach (var item in entries)
        {
            var source = item.SourceText.Replace("\r", " ").Replace("\n", " ");
            var translated = item.TranslatedText.Replace("\r", " ").Replace("\n", " ");
            builder.Append(CultureInfo.InvariantCulture, $"{item.Index}\t");
            builder.Append(item.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\t');
            builder.Append(source).Append('\t');
            builder.Append(translated).AppendLine();
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), new UTF8Encoding(false));
    }
}
