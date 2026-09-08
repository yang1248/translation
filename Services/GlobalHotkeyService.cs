using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RealtimeTranslator.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;

    private readonly Dictionary<int, Action> _actions = new();
    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _disposed;

    public event EventHandler<string>? RegistrationFailed;

    public void Attach(Window window)
    {
        Detach();
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    public bool Register(string combination, Action action)
    {
        Unregister(combination);

        if (_hwnd == IntPtr.Zero || !TryParse(combination, out var modifiers, out var key))
        {
            RegistrationFailed?.Invoke(this, $"快捷键格式无效：{combination}");
            return false;
        }

        var id = GetIdFor(combination);
        if (!RegisterHotKey(_hwnd, id, modifiers | ModNoRepeat, key))
        {
            RegistrationFailed?.Invoke(this, $"快捷键注册失败，可能已被其他程序占用：{combination}");
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void Unregister(string combination)
    {
        var id = GetIdFor(combination);
        if (_actions.Remove(id) && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys.ToArray())
        {
            if (_hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, id);
            }
        }
        _actions.Clear();
    }

    public void Detach()
    {
        UnregisterAll();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
        _hwnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Detach();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static int GetIdFor(string combination) =>
        string.GetHashCode(combination, StringComparison.OrdinalIgnoreCase) & 0x7fffffff;

    private static bool TryParse(string combination, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        var parts = combination.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                default:
                    return false;
            }
        }

        if (modifiers == 0 || parts[^1].Length is < 1 or > 2)
        {
            return false;
        }

        var name = parts[^1].ToLowerInvariant();
        uint code = name switch
        {
            "space" => 0x20,
            "tab" => 0x09,
            "enter" => 0x0d,
            "esc" => 0x1b,
            "f1" or "f2" or "f3" or "f4" or "f5" or "f6" or "f7" or "f8" or "f9"
                or "f10" or "f11" or "f12" => (uint)(0x70 + Array.IndexOf(
                    new[] { "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12" }, name)),
            _ => name.Length == 1 && char.IsAsciiLetter(name[0])
                ? (uint)(0x41 + name[0] - 'a')
                : 0u
        };

        key = code;
        return code != 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
