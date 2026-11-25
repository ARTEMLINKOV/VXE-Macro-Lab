using System.Runtime.InteropServices;

namespace VxeMacroLab.Services;

/// <summary>Global physical-key Snap-Tap for the WASD cluster.</summary>
public sealed class GlobalSnapTapService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;
    private const int PulseDurationMs = 90;

    private const ushort ScanA = 0x1E;
    private const ushort ScanD = 0x20;
    private const ushort ScanW = 0x11;
    private const ushort ScanS = 0x1F;

    private readonly HookProc _hookProc;
    private readonly object _sync = new();
    private readonly Dictionary<ushort, Timer> _releaseTimers = new();
    private readonly HashSet<ushort> _syntheticKeysDown = new();
    private IntPtr _hook;
    private bool _enabled = true;
    private bool _disposed;

    public GlobalSnapTapService() => _hookProc = KeyboardHookCallback;

    public bool IsEnabled { get { lock (_sync) return _enabled; } }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            _enabled = enabled;
            if (!enabled) ReleaseAllSyntheticKeys();
        }
    }

    public void ToggleEnabled() => SetEnabled(!IsEnabled);

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hook != IntPtr.Zero) return;
            _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException($"Не удалось установить глобальный перехват клавиатуры. Код Windows: {Marshal.GetLastWin32Error()}");
        }
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((data.Flags & LlkhfInjected) == 0)
            {
                lock (_sync)
                {
                    if (_enabled && message is WmKeyUp or WmSysKeyUp)
                    {
                        var target = OppositeScanCode((ushort)(data.ScanCode & 0xff));
                        if (target != 0) SendPulse(target);
                    }
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void SendPulse(ushort scanCode)
    {
        if (!_syntheticKeysDown.Contains(scanCode))
        {
            var down = CreateKeyboardInput(scanCode, KeyeventfScancode);
            if (SendInput(1, new[] { down }, Marshal.SizeOf<Input>()) != 1) return;
            _syntheticKeysDown.Add(scanCode);
        }
        if (_releaseTimers.Remove(scanCode, out var previous)) previous.Dispose();
        _releaseTimers[scanCode] = new Timer(_ => ReleaseSyntheticKey(scanCode), null, PulseDurationMs, Timeout.Infinite);
    }

    private void ReleaseSyntheticKey(ushort scanCode)
    {
        lock (_sync)
        {
            if (!_syntheticKeysDown.Remove(scanCode)) return;
            if (_releaseTimers.Remove(scanCode, out var timer)) timer.Dispose();
            SendInput(1, new[] { CreateKeyboardInput(scanCode, KeyeventfScancode | KeyeventfKeyup) }, Marshal.SizeOf<Input>());
        }
    }

    private void ReleaseAllSyntheticKeys()
    {
        foreach (var scanCode in _syntheticKeysDown.ToArray())
        {
            if (_releaseTimers.Remove(scanCode, out var timer)) timer.Dispose();
            SendInput(1, new[] { CreateKeyboardInput(scanCode, KeyeventfScancode | KeyeventfKeyup) }, Marshal.SizeOf<Input>());
        }
        _syntheticKeysDown.Clear();
    }

    private static ushort OppositeScanCode(ushort scanCode) => scanCode switch
    {
        ScanA => ScanD, ScanD => ScanA,
        ScanW => ScanS, ScanS => ScanW,
        _ => 0
    };

    private static Input CreateKeyboardInput(ushort scanCode, uint flags) => new()
    {
        Type = 1,
        Union = new InputUnion { Keyboard = new KeyboardInput { ScanCode = scanCode, Flags = flags } }
    };

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            foreach (var timer in _releaseTimers.Values) timer.Dispose();
            _releaseTimers.Clear();
            ReleaseAllSyntheticKeys();
        }
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct KbdLlHookStruct { public uint VirtualKey; public uint ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int inputSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? moduleName);
}
