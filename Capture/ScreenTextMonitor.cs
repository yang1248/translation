using System.Drawing;
using CultureInfo = System.Globalization.CultureInfo;
using System.Text.RegularExpressions;
using System.IO;
using System.Windows.Threading;
using RealtimeTranslator.Models;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace RealtimeTranslator.Capture;

public sealed class ScreenTextMonitor
{
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;
    private OcrEngine? _engine;
    private RegionInfo? _region;
    private string _lastSignature = string.Empty;
    private bool _busy;
    private bool _running;
    private int _captureFailures;

    public ScreenTextMonitor(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
    }

    public bool IsRunning => _running;

    public event Action<string>? LineRecognized;
    public event Action<string>? Message;

    public void Start(RegionInfo region)
    {
        Stop();
        _region = region;
        _engine = OcrLanguageCatalog.CreateEngine(_settings.OcrLanguage);
        if (_engine is null)
        {
            Message?.Invoke("Windows 尚未安装可用的 OCR 语言包，请在系统设置中安装后重试。");
            return;
        }

        _running = true;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.ScreenIntervalMs, 150, 3000))
        };
        _timer.Tick += async (_, _) => await CaptureTickAsync();
        _timer.Start();
        Message?.Invoke("开始识别框选区域中的新字幕。");
    }

    public void Stop()
    {
        _running = false;
        _busy = false;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= async (_, _) => await CaptureTickAsync();
            _timer = null;
        }
        _engine = null;
        _region = null;
    }

    public void Pause()
    {
        _running = false;
        _timer?.Stop();
        Message?.Invoke("屏幕文字识别已暂停。");
    }

    public void Resume(RegionInfo region)
    {
        if (_timer is null || _region is null || _region != region)
        {
            Start(region);
            return;
        }

        _running = true;
        _timer.Start();
        Message?.Invoke("已继续识别屏幕字幕。");
    }

    public void UpdateSettings(AppSettings settings)
    {
        if (_running && _timer is not null)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.ScreenIntervalMs, 150, 3000));
        }
    }

    private async Task CaptureTickAsync()
    {
        if (!_running || _busy || _region is null || _engine is null)
        {
            return;
        }

        _busy = true;
        try
        {
            using var bitmap = await Task.Run(() => ScreenCapture.CaptureRegion(_region!));
            if (bitmap is null || !_running)
            {
                return;
            }

            var text = await RecognizeAsync(_engine, bitmap);
            if (!_running)
            {
                return;
            }

            var signature = Normalize(text);
            if (!string.IsNullOrWhiteSpace(signature) && !string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                _lastSignature = signature;
                LineRecognized?.Invoke(text);
            }
            _captureFailures = 0;
        }
        catch (Exception ex)
        {
            _captureFailures++;
            if (_captureFailures <= 1 || _captureFailures % 10 == 0)
            {
                Message?.Invoke($"屏幕捕获或识别失败：{ShortMessage(ex)}");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task<string> RecognizeAsync(OcrEngine engine, Bitmap bitmap)
    {
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        var bytes = pngStream.ToArray();

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(CryptographicBuffer.CreateFromByteArray(bytes));
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(softwareBitmap);
        var lines = result.Lines.Select(line => line.Text.Trim());
        return string.Join(" ", lines).Trim();
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(text, @"[\s\u3000]+", string.Empty);
        return normalized.ToLower(CultureInfo.InvariantCulture);
    }

    private static string ShortMessage(Exception ex)
    {
        var message = ex.Message;
        return message.Length > 160 ? message[..160] + "..." : message;
    }
}
