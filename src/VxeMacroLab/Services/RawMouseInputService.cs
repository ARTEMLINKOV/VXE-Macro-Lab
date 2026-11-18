using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using VxeMacroLab.Models;

namespace VxeMacroLab.Services;

public sealed class RawMouseInputService : IDisposable
{
    private const int WmInput = 0x00FF;
    private const int WmHotkey = 0x0312;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const int HotkeyId = 0x4D31;
    private const uint VkF8 = 0x77;
    private const uint ModNoRepeat = 0x4000;

    private HwndSource? _source;
    private readonly Stopwatch _clock = new();
    private readonly List<MouseEvent> _events = new();
    private bool _recording;
    private bool _hotkeyRegistered;

    public event Action<MouseEvent>? MouseMoved;
    public event Action? HotkeyPressed;
    public IReadOnlyList<MouseEvent> Events => _events;
    public bool IsRecording => _recording;

    public void Attach(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(WndProc);

        var device = new RawInputDevice
        {
            UsagePage = 0x01,
            Usage = 0x02,
            Flags = RidevInputSink,
            Target = hwnd
        };

        if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new InvalidOperationException("Не удалось зарегистрировать Raw Input. Код: " + Marshal.GetLastWin32Error());

        _hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, ModNoRepeat, VkF8);
    }

    public void StartRecording()
    {
        _events.Clear();
        _clock.Restart();
        _recording = true;
    }

    public List<MouseEvent> StopRecording()
    {
        _recording = false;
        _clock.Stop();
        return _events.ToList();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        else if (msg == WmInput)
        {
            ReadRawInput(lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ReadRawInput(IntPtr rawInputHandle)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize) == uint.MaxValue)
                return;

            var input = Marshal.PtrToStructure<RawInput>(buffer);
            var x = input.Mouse.LastX;
            var y = input.Mouse.LastY;
            if (x == 0 && y == 0) return;

            var item = new MouseEvent { X = x, Y = y, TimeMs = _recording ? _clock.ElapsedMilliseconds : 0 };
            if (_recording) _events.Add(item);
            MouseMoved?.Invoke(item);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            if (_hotkeyRegistered)
                UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        public ushort Flags;
        public ushort ButtonFlags;
        public ushort ButtonData;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawMouse Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint numDevices, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
