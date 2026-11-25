using System.Runtime.InteropServices;

namespace VxeMacroLab.Services;

/// <summary>
/// Applies a narrow global Snap-Tap rule. It observes only A/D/W/S key-up events;
/// it does not store or expose keyboard input.
/// </summary>
public sealed class GlobalSnapTapService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const uint LlkhfInjected = 0x10;
    private const uint KeyeventfKeyup = 0x0002;
    private const int PulseDurationMs = 90;
    private const ushort VkA = 0x41;
    private const ushort VkD = 0x44;
    private const ushort VkW = 0x57;
    private const ushort VkS = 0x53;
    private const ushort VkF9 = 0x78;

    private readonly HookProc _hookProc;
    private readonly object _sync = new();
    private readonly Dictionary<ushort, Timer> _releaseTimers = new();
    private readonly HashSet<ushort> _syntheticKeysDown = new();
    private IntPtr _hook;
    private bool _enabled = true;
    private bool _toggleKeyDown;
    private bool _disposed;

    public GlobalSnapTapService() => _hookProc = KeyboardHookCallback;

    public bool IsEnabled
    {
        get { lock (_sync) return _enabled; }
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hook != IntPtr.Zero) return;

            var module = GetModuleHandle(null);
            _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, module, 0);
            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException($"Не удалось включить глобальное правило клавиатуры. Код Windows: {Marshal.GetLastWin32Error()}");
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
                    if (data.VirtualKey == VkF9)
                    {
                        if (message is WmKeyDown or WmSysKeyDown && !_toggleKeyDown)
                        {
                            _toggleKeyDown = true;
                            _enabled = !_enabled;
                        }
                        else if (message is WmKeyUp or WmSysKeyUp)
                        {
                            _toggleKeyDown = false;
                        }
                    }
                    else if (_enabled && message is WmKeyUp or WmSysKeyUp)
                    {
                        var target = OppositeKey(data.VirtualKey);
                        if (target != 0)
                            SendPulse(target);
                    }
                }
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void SendPulse(ushort virtualKey)
    {
        if ((GetAsyncKeyState(virtualKey) & 0x8000) != 0 && !_syntheticKeysDown.Contains(virtualKey))
            return;

        if (!_syntheticKeysDown.Contains(virtualKey))
        {
            var down = CreateKeyboardInput(virtualKey, 0);
            if (SendInput(1, new[] { down }, Marshal.SizeOf<Input>()) != 1)
                return;
            _syntheticKeysDown.Add(virtualKey);
        }

        if (_releaseTimers.Remove(virtualKey, out var previous))
            previous.Dispose();

        _releaseTimers[virtualKey] = new Timer(_ => ReleaseSyntheticKey(virtualKey), null,
            PulseDurationMs, Timeout.Infinite);
    }

    private void ReleaseSyntheticKey(ushort virtualKey)
    {
        lock (_sync)
        {
            if (!_syntheticKeysDown.Remove(virtualKey)) return;
            if (_releaseTimers.Remove(virtualKey, out var timer))
                timer.Dispose();
            var up = CreateKeyboardInput(virtualKey, KeyeventfKeyup);
            SendInput(1, new[] { up }, Marshal.SizeOf<Input>());
        }
    }

    private static ushort OppositeKey(uint key) => key switch
    {
        VkA => VkD,
        VkD => VkA,
        VkW => VkS,
        VkS => VkW,
        _ => 0
    };

    private static Input CreateKeyboardInput(ushort virtualKey, uint flags) => new()
    {
        Type = 1,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = flags
            }
        }
    };

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

            foreach (var timer in _releaseTimers.Values)
                timer.Dispose();
            _releaseTimers.Clear();
            foreach (var key in _syntheticKeysDown)
            {
                var up = CreateKeyboardInput(key, KeyeventfKeyup);
                SendInput(1, new[] { up }, Marshal.SizeOf<Input>());
            }
            _syntheticKeysDown.Clear();
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
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(ushort virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int inputSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
