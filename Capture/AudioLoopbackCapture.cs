using System.Buffers.Binary;
using NAudio.Wave;

namespace RealtimeTranslator.Capture;

public sealed class AudioLoopbackCapture : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private Pcm16Encoder? _encoder;
    private bool _running;

    public event Action<byte[]>? Pcm16Data;
    public event Action<string>? Failed;
    public event Action? Stopped;

    public int SampleRate { get; private set; }

    public void Prepare()
    {
        Stop();
        _capture = new WasapiLoopbackCapture();
        _encoder = new Pcm16Encoder(_capture.WaveFormat);
        SampleRate = _capture.WaveFormat.SampleRate;
    }

    public void Start()
    {
        if (_capture is null)
        {
            Prepare();
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _running = true;
        _capture.StartRecording();
    }

    public void Stop()
    {
        _running = false;
        var capture = _capture;
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.StopRecording();
        }
        catch
        {
            // Dispose below is still needed.
        }

        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        capture.Dispose();
        _capture = null;
        _encoder = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running || _encoder is null)
        {
            return;
        }

        try
        {
            var pcm = _encoder.Encode(e.Buffer, e.BytesRecorded);
            if (pcm.Length > 0)
            {
                Pcm16Data?.Invoke(pcm);
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke($"电脑声音捕获失败：{ex.Message}");
            Stop();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _running = false;
        if (e.Exception is not null)
        {
            Failed?.Invoke($"电脑声音捕获停止：{e.Exception.Message}");
        }
        Stopped?.Invoke();
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class Pcm16Encoder
    {
        private readonly WaveFormat _format;
        private readonly int _sampleBytes;
        private readonly float[] _channelSamples;

        public Pcm16Encoder(WaveFormat format)
        {
            _format = format;
            _sampleBytes = format.BitsPerSample / 8;
            _channelSamples = new float[format.Channels];
        }

        public byte[] Encode(byte[] buffer, int bytesRecorded)
        {
            var bytesPerFrame = _format.BlockAlign;
            var frames = bytesRecorded / bytesPerFrame;
            var output = new byte[frames * 2];
            var outOffset = 0;

            for (var frame = 0; frame < frames; frame++)
            {
                var frameOffset = frame * bytesPerFrame;
                var sum = 0f;

                for (var channel = 0; channel < _format.Channels; channel++)
                {
                    var offset = frameOffset + channel * _sampleBytes;
                    float sample = _format.Encoding switch
                    {
                        WaveFormatEncoding.IeeeFloat => BitConverter.ToSingle(buffer, offset),
                        WaveFormatEncoding.Pcm when _format.BitsPerSample == 16 => BitConverter.ToInt16(buffer, offset) / 32768f,
                        WaveFormatEncoding.Pcm when _format.BitsPerSample == 32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                        WaveFormatEncoding.Pcm when _format.BitsPerSample == 24 =>
                            ReadInt24(buffer, offset) / 8388608f,
                        _ => throw new InvalidOperationException($"不支持的音频格式：{_format.Encoding}")
                    };
                    _channelSamples[channel] = sample;
                    sum += sample;
                }

                var mixed = sum / _format.Channels;
                var clamped = Math.Clamp(mixed, -1f, 1f);
                var pcm16 = (short)Math.Round(clamped * short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(outOffset), pcm16);
                outOffset += 2;
            }

            return output;
        }

        private static int ReadInt24(byte[] buffer, int offset)
        {
            var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            return value << 8 >> 8;
        }
    }
}
