using System.Runtime.InteropServices;

namespace VxeMacroLab.Services;

/// <summary>Global last-pressed priority for opposite WASD directions.</summary>
public sealed class GlobalSnapTapService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;

    private const ushort ScanA = 0x1E;
    private const ushort ScanD = 0x20;
    private const ushort ScanW = 0x11;
    private const ushort ScanS = 0x1F;

    private readonly HookProc _hookProc;
    private readonly object _sync = new();
    private readonly HashSet<ushort> _physicalKeysDown = new();
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
            if (_enabled == enabled) return;
            if (!enabled)
            {
                // Restore held physical directions before returning to pass-through mode.
                foreach (var scanCode in _syntheticKeysDown.ToArray()) SendKey(scanCode, keyUp: true);
                _syntheticKeysDown.Clear();
                foreach (var scanCode in _physicalKeysDown) SendKey(scanCode, keyUp: false);
                _physicalKeysDown.Clear();
            }
            _enabled = enabled;
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
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((data.Flags & LlkhfInjected) == 0)
            {
                var message = wParam.ToInt32();
                var scanCode = (ushort)(data.ScanCode & 0xff);
                lock (_sync)
                {
                    if (_enabled && IsDirection(scanCode))
                    {
                        if (message is WmKeyDown or WmSysKeyDown) HandleKeyDown(scanCode);
                        else if (message is WmKeyUp or WmSysKeyUp) HandleKeyUp(scanCode);
                        else return CallNextHookEx(_hook, code, wParam, lParam);

                        // Consume physical WASD event; send only the resolved priority key.
                        return new IntPtr(1);
                    }
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void HandleKeyDown(ushort scanCode)
    {
        if (!_physicalKeysDown.Add(scanCode)) return; // Ignore OS key-repeat events.

        var opposite = OppositeScanCode(scanCode);
        if (_syntheticKeysDown.Remove(opposite)) SendKey(opposite, keyUp: true);
        if (_syntheticKeysDown.Add(scanCode)) SendKey(scanCode, keyUp: false);
    }

    private void HandleKeyUp(ushort scanCode)
    {
        _physicalKeysDown.Remove(scanCode);
        if (!_syntheticKeysDown.Remove(scanCode)) return;

        SendKey(scanCode, keyUp: true);
        var fallback = OppositeScanCode(scanCode);
        if (_physicalKeysDown.Contains(fallback) && _syntheticKeysDown.Add(fallback))
            SendKey(fallback, keyUp: false);
    }

    private static bool IsDirection(ushort scanCode) =>
        scanCode is ScanA or ScanD or ScanW or ScanS;

    private static ushort OppositeScanCode(ushort scanCode) => scanCode switch
    {
        ScanA => ScanD,
        ScanD => ScanA,
        ScanW => ScanS,
        ScanS => ScanW,
        _ => 0
    };

    private static void SendKey(ushort scanCode, bool keyUp)
    {
        var flags = KeyeventfScancode | (keyUp ? KeyeventfKeyup : 0);
        var input = new Input
        {
            Type = 1,
            Union = new InputUnion { Keyboard = new KeyboardInput { ScanCode = scanCode, Flags = flags } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            foreach (var scanCode in _syntheticKeysDown) SendKey(scanCode, keyUp: true);
            _syntheticKeysDown.Clear();
            _physicalKeysDown.Clear();
        }
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Union; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int inputSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
