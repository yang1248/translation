using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RealtimeTranslator.Models;

namespace RealtimeTranslator.Recognition;

public interface IStreamingSpeechClient
{
    event Action<string>? UtteranceRecognized;
    event Action<string>? Message;

    Task StartAsync(int sampleRate, CancellationToken cancellationToken);
    void EnqueueAudio(byte[] linear16Pcm);
    Task StopAsync();
}

public sealed class DeepgramSpeechClient : IStreamingSpeechClient
{
    private readonly AppSettings _settings;
    private readonly Channel<byte[]> _audioQueue;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _sendTask;
    private Task? _receiveTask;
    private string _lastUtterance = string.Empty;
    private bool _disposed;

    public DeepgramSpeechClient(AppSettings settings)
    {
        _settings = settings;
        _audioQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(240)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public event Action<string>? UtteranceRecognized;
    public event Action<string>? Message;

    public async Task StartAsync(int sampleRate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.DeepgramApiKey))
        {
            throw new InvalidOperationException("尚未配置 Deepgram API 密钥。");
        }

        await StopAsync();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _socket = new ClientWebSocket();
        _socket.Options.SetRequestHeader("Authorization", $"Token {_settings.DeepgramApiKey}");
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(25);

        var url = BuildUrl(sampleRate);
        await _socket.ConnectAsync(new Uri(url), cancellationToken);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_socket, token), token);
        _sendTask = Task.Run(() => SendLoopAsync(token), token);
        Message?.Invoke("语音识别连接已建立。");
    }

    public void EnqueueAudio(byte[] linear16Pcm)
    {
        if (linear16Pcm.Length > 0)
        {
            _audioQueue.Writer.TryWrite(linear16Pcm);
        }
    }

    public async Task StopAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_cts is not null)
        {
            _cts.Cancel();
        }

        var socket = _socket;
        if (socket is not null && socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client stop", timeout.Token);
            }
            catch
            {
                // Best effort close; the receiver below disposes the socket.
            }
        }

        try
        {
            if (_sendTask is not null)
            {
                await _sendTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            if (_receiveTask is not null)
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch
        {
            // The receive loop can throw when the connection is intentionally closed.
        }

        socket?.Dispose();
        _cts?.Dispose();
        _cts = null;
        _socket = null;
        _sendTask = null;
        _receiveTask = null;
        _lastUtterance = string.Empty;
    }

    private string BuildUrl(int sampleRate)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.DeepgramBaseUrl)
            ? "wss://api.deepgram.com/v1/listen"
            : _settings.DeepgramBaseUrl;
        var parts = new List<string>
        {
            $"model={Uri.EscapeDataString(string.IsNullOrWhiteSpace(_settings.DeepgramModel) ? "nova-3" : _settings.DeepgramModel)}",
            "encoding=linear16",
            $"sample_rate={sampleRate}",
            "channels=1",
            "interim_results=true",
            "vad_events=true",
            "speech_final=true",
            "endpointing=300",
            "punctuate=true"
        };

        if (!string.IsNullOrWhiteSpace(_settings.SpeechLanguage) &&
            !string.Equals(_settings.SpeechLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"language={Uri.EscapeDataString(_settings.SpeechLanguage)}");
        }

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return baseUrl + separator + string.Join("&", parts);
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket is null)
        {
            return;
        }

        await foreach (var chunk in _audioQueue.Reader.ReadAllAsync(cancellationToken))
        {
            await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, cancellationToken);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var text = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                Message?.Invoke("语音识别连接已断开。");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                continue;
            }

            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }

            var json = text.ToString();
            text.Clear();
            TryHandleMessage(json);
        }
    }

    private void TryHandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "Results")
            {
                return;
            }

            var isSpeechFinal = root.TryGetProperty("speech_final", out var speechFinal) && speechFinal.GetBoolean();
            if (!isSpeechFinal)
            {
                return;
            }

            var transcript = root
                .GetProperty("channel")
                .GetProperty("alternatives")[0]
                .GetProperty("transcript")
                .GetString()
                ?.Trim();

            if (string.IsNullOrWhiteSpace(transcript) ||
                string.Equals(transcript, _lastUtterance, StringComparison.Ordinal))
            {
                return;
            }

            _lastUtterance = transcript;
            UtteranceRecognized?.Invoke(transcript);
        }
        catch
        {
            // Ignore malformed or unknown service messages; the stream stays alive.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
    }
}
