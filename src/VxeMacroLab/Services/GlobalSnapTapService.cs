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
                // Release synthesized state, then restore physically held directions.
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
            // Let our own SendInput events continue through the Windows hook chain.
            if ((data.Flags & LlkhfInjected) == 0)
            {
                var message = wParam.ToInt32();
                var scanCode = (ushort)(data.ScanCode & 0xff);
                lock (_sync)
                {
                    if (_enabled && IsDirection(scanCode))
                    {
                        bool handled;
                        if (message is WmKeyDown or WmSysKeyDown)
                            handled = HandleKeyDown(scanCode);
                        else if (message is WmKeyUp or WmSysKeyUp)
                            handled = HandleKeyUp(scanCode);
                        else
                            return CallNextHookEx(_hook, code, wParam, lParam);

                        // If Windows refuses synthesized input (e.g. UIPI/integrity mismatch),
                        // pass the physical event through instead of swallowing the user's key.
                        if (handled) return new IntPtr(1);
                    }
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool HandleKeyDown(ushort scanCode)
    {
        if (!_physicalKeysDown.Add(scanCode)) return true; // Consume OS key-repeat events.

        var opposite = OppositeScanCode(scanCode);
        if (_syntheticKeysDown.Contains(opposite))
        {
            if (!SendKey(opposite, keyUp: true))
            {
                _physicalKeysDown.Remove(scanCode);
                return false;
            }
            _syntheticKeysDown.Remove(opposite);
        }

        if (!SendKey(scanCode, keyUp: false))
        {
            _physicalKeysDown.Remove(scanCode);
            return false;
        }
        _syntheticKeysDown.Add(scanCode);
        return true;
    }

    private bool HandleKeyUp(ushort scanCode)
    {
        if (!_physicalKeysDown.Contains(scanCode)) return false;

        if (_syntheticKeysDown.Contains(scanCode))
        {
            if (!SendKey(scanCode, keyUp: true))
            {
                _physicalKeysDown.Remove(scanCode);
                return false;
            }
            _syntheticKeysDown.Remove(scanCode);
        }

        _physicalKeysDown.Remove(scanCode);
        var fallback = OppositeScanCode(scanCode);
        if (_physicalKeysDown.Contains(fallback) && !_syntheticKeysDown.Contains(fallback))
        {
            if (SendKey(fallback, keyUp: false))
                _syntheticKeysDown.Add(fallback);
        }
        return true;
    }

    private static bool IsDirection(ushort scanCode) => scanCode is ScanA or ScanD or ScanW or ScanS;

    private static ushort OppositeScanCode(ushort scanCode) => scanCode switch
    {
        ScanA => ScanD,
        ScanD => ScanA,
        ScanW => ScanS,
        ScanS => ScanW,
        _ => 0
    };

    private static bool SendKey(ushort scanCode, bool keyUp)
    {
        var flags = KeyeventfScancode | (keyUp ? KeyeventfKeyup : 0);
        var input = new Input
        {
            Type = 1,
            Union = new InputUnion { Keyboard = new KeyboardInput { ScanCode = scanCode, Flags = flags } }
        };
        return SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) == 1;
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
