using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using VxeMacroLab.Models;
using VxeMacroLab.Services;

namespace VxeMacroLab;

public partial class MainWindow : Window
{
    private readonly RawMouseInputService _mouseInput = new();
    private readonly List<MouseEvent> _mouseEvents = new();
    private readonly List<KeyboardEvent> _keyboardEvents = new();
    private readonly Stopwatch _keyboardClock = new();
    private readonly HashSet<Key> _pressedKeys = new();
    private bool _keyboardRecording;
    private bool _snapLeftDown;
    private bool _snapRightDown;
    private bool _snapUpDown;
    private bool _snapDownDown;
    private bool _snapReleasePulseLeft;
    private bool _snapReleasePulseRight;
    private bool _snapReleasePulseUp;
    private bool _snapReleasePulseDown;
    private readonly DispatcherTimer _snapPulseTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly DispatcherTimer _snapTriggerDelayTimer = new() { Interval = TimeSpan.FromMilliseconds(0) };
    private int _snapPulseDurationMs = 90;
    private int _snapTriggerDelayMs = 0;
    private bool _snapPendingPulseLeft, _snapPendingPulseRight, _snapPendingPulseUp, _snapPendingPulseDown;
    private readonly DispatcherTimer _mousePlaybackTimer = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private Stopwatch _mousePlaybackClock = new();
    private List<Point> _mousePlaybackPoints = new();
    private List<long> _mousePlaybackTimes = new();
    private bool _mousePlaybackActive;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => DrawMousePath();
        SizeChanged += (_, _) => DrawMousePath();
        _mouseInput.HotkeyPressed += ToggleMouseRecording;
        _mouseInput.MouseMoved += _ => UpdateMouseCount();
        UpdateKeyboardCount();
        _snapPulseTimer.Tick += (_, _) => { _snapReleasePulseLeft = false; _snapReleasePulseRight = false; _snapReleasePulseUp = false; _snapReleasePulseDown = false; _snapPulseTimer.Stop(); UpdateSnapTapPreview(); };
        _snapTriggerDelayTimer.Tick += SnapTriggerDelayTimer_Tick;
        _mousePlaybackTimer.Tick += MousePlaybackTimer_Tick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            _mouseInput.Attach(new WindowInteropHelper(this).Handle);
            StatusText.Text = "Готово · F8 управляет записью мыши";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка инициализации мыши";
            MessageBox.Show(ex.Message, "VXE Macro Lab", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // MOUSE MODULE

    private void MouseRecordButton_Click(object sender, RoutedEventArgs e) => ToggleMouseRecording();

    private void ToggleMouseRecording()
    {
        if (_mouseInput.IsRecording)
        {
            _mouseEvents.Clear();
            _mouseEvents.AddRange(_mouseInput.StopRecording());
            MouseRecordButton.Content = "●  Начать запись";
            StatusText.Text = "Запись мыши остановлена";
            UpdateMouseCount();
            DrawMousePath();
        }
        else
        {
            try
            {
                _mouseEvents.Clear();
                _mouseInput.StartRecording();
                MouseRecordButton.Content = "■  Идёт запись";
                StatusText.Text = "Записываются движения мыши";
                UpdateMouseCount();
                MousePreviewCanvas.Children.Clear();
            }
            catch (Exception ex)
            {
                MouseRecordButton.Content = "●  Начать запись";
                StatusText.Text = "Не удалось включить захват мыши";
                MessageBox.Show(ex.Message, "VXE Macro Lab",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void MouseStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mouseInput.IsRecording) ToggleMouseRecording();
    }

    private void MouseClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mouseInput.IsRecording) _mouseInput.StopRecording();
        _mouseEvents.Clear();
        MousePreviewCanvas.Children.Clear();
        MouseRecordButton.Content = "●  Начать запись";
        StatusText.Text = "Данные мыши очищены";
        UpdateMouseCount();
    }

    private void MouseSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mouseInput.IsRecording) ToggleMouseRecording();
        if (_mouseEvents.Count == 0)
        {
            MessageBox.Show("Сначала запиши движение мыши.", "Нет данных",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить профиль мыши",
            Filter = "Защищённый профиль VXE (*.vxeprofile)|*.vxeprofile|Старый JSON (*.json)|*.json",
            FileName = "mouse-profile.vxeprofile",
            DefaultExt = ".vxeprofile"
        };
        if (dialog.ShowDialog() != true) return;

        var profile = new MacroProfile
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            Events = _mouseEvents.ToList()
        };
        ProfileProtectionService.Save(dialog.FileName, profile);
        StatusText.Text = "Профиль мыши сохранён";
    }

    private void MouseLoadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Загрузить профиль мыши",
            Filter = "Профиль VXE (*.vxeprofile)|*.vxeprofile|Старый JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var profile = ProfileProtectionService.Load<MacroProfile>(dialog.FileName);
            _mouseEvents.Clear();
            _mouseEvents.AddRange(profile.Events ?? new List<MouseEvent>());
            StatusText.Text = $"Загружен профиль мыши: {profile.Name}";
            UpdateMouseCount();
            DrawMousePath();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не удалось открыть профиль мыши",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateMouseCount()
    {
        var count = _mouseInput.IsRecording ? _mouseInput.Events.Count : _mouseEvents.Count;
        MouseEventCountText.Text = count.ToString("N0");
    }


    private void MousePlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mouseEvents.Count < 2)
        {
            MessageBox.Show("Запиши или загрузи траекторию минимум из двух событий.", "Нет траектории",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MousePlaybackStopButton_Click(sender, e);
        _mousePlaybackPoints.Clear();
        _mousePlaybackTimes.Clear();
        double x = 0, y = 0;
        _mousePlaybackPoints.Add(new Point(0, 0));
        _mousePlaybackTimes.Add(0);
        foreach (var ev in _mouseEvents)
        {
            x += ev.X; y += ev.Y;
            _mousePlaybackPoints.Add(new Point(x, y));
            _mousePlaybackTimes.Add(ev.TimeMs);
        }
        _mousePlaybackActive = true;
        _mousePlaybackClock.Restart();
        StatusText.Text = "Воспроизведение курсором в пределах площадки";
        _mousePlaybackTimer.Start();
    }

    private void MousePlaybackStopButton_Click(object sender, RoutedEventArgs e)
    {
        _mousePlaybackTimer.Stop();
        _mousePlaybackActive = false;
        _mousePlaybackClock.Stop();
        StatusText.Text = "Воспроизведение траектории остановлено";
    }

    private void MousePlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (!_mousePlaybackActive || _mousePlaybackPoints.Count < 2) return;
        var elapsed = _mousePlaybackClock.ElapsedMilliseconds;
        if (elapsed >= _mousePlaybackTimes[^1])
        {
            MousePlaybackStopButton_Click(this, new RoutedEventArgs());
            return;
        }
        var idx = 0;
        while (idx < _mousePlaybackTimes.Count - 2 && _mousePlaybackTimes[idx + 1] < elapsed) idx++;
        var t0 = _mousePlaybackTimes[idx];
        var t1 = Math.Max(t0 + 1, _mousePlaybackTimes[idx + 1]);
        var amount = Math.Clamp((double)(elapsed - t0) / (t1 - t0), 0, 1);
        var a = _mousePlaybackPoints[idx];
        var b = _mousePlaybackPoints[idx + 1];
        var current = new Point(a.X + (b.X - a.X) * amount, a.Y + (b.Y - a.Y) * amount);
        var minX = _mousePlaybackPoints.Min(p => p.X);
        var maxX = _mousePlaybackPoints.Max(p => p.X);
        var minY = _mousePlaybackPoints.Min(p => p.Y);
        var maxY = _mousePlaybackPoints.Max(p => p.Y);
        var w = Math.Max(1, maxX - minX);
        var h = Math.Max(1, maxY - minY);
        var cw = Math.Max(30, MousePreviewCanvas.ActualWidth - 40);
        var ch = Math.Max(30, MousePreviewCanvas.ActualHeight - 40);
        var scale = Math.Min(cw / w, ch / h);
        var origin = MousePreviewCanvas.PointToScreen(new Point(0, 0));
        var edge = MousePreviewCanvas.PointToScreen(new Point(MousePreviewCanvas.ActualWidth, MousePreviewCanvas.ActualHeight));
        var px = origin.X + 20 + (cw - w * scale) / 2 + (current.X - minX) * scale;
        var py = origin.Y + 20 + (ch - h * scale) / 2 + (current.Y - minY) * scale;
        px = Math.Clamp(px, origin.X + 8, edge.X - 8);
        py = Math.Clamp(py, origin.Y + 8, edge.Y - 8);
        SetCursorPos((int)px, (int)py);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private void DrawMousePath()
    {
        if (!IsLoaded || MousePreviewCanvas.ActualWidth < 10 || MousePreviewCanvas.ActualHeight < 10)
            return;

        MousePreviewCanvas.Children.Clear();
        if (_mouseEvents.Count < 2) return;

        double x = 0, y = 0;
        var points = new List<Point> { new(0, 0) };
        foreach (var item in _mouseEvents)
        {
            x += item.X;
            y += item.Y;
            points.Add(new Point(x, y));
        }

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        const double margin = 24;
        var scale = Math.Min((MousePreviewCanvas.ActualWidth - margin * 2) / width,
                             (MousePreviewCanvas.ActualHeight - margin * 2) / height);
        var offsetX = (MousePreviewCanvas.ActualWidth - width * scale) / 2;
        var offsetY = (MousePreviewCanvas.ActualHeight - height * scale) / 2;

        var line = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(139, 124, 255)),
            StrokeThickness = 2.2
        };
        foreach (var point in points)
            line.Points.Add(new Point(offsetX + (point.X - minX) * scale,
                                      offsetY + (point.Y - minY) * scale));
        MousePreviewCanvas.Children.Add(line);
        AddMouseMarker(line.Points.First(), "#FF57C785", 7);
        AddMouseMarker(line.Points.Last(), "#FFE3A64B", 7);
    }

    private void AddMouseMarker(Point point, string color, double size)
    {
        var marker = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!
        };
        Canvas.SetLeft(marker, point.X - size / 2);
        Canvas.SetTop(marker, point.Y - size / 2);
        MousePreviewCanvas.Children.Add(marker);
    }

    // KEYBOARD MODULE
    // Keyboard events are captured only by this focused application surface.
    // No system-wide key logging and no synthetic input is sent to other apps.

    private void KeyboardRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_keyboardRecording)
        {
            StopKeyboardRecording();
            return;
        }

        _keyboardEvents.Clear();
        _pressedKeys.Clear();
        KeyboardEventsList.Items.Clear();
        _keyboardClock.Restart();
        _keyboardRecording = true;
        KeyboardRecordButton.Content = "■  Идёт запись";
        StatusText.Text = "Запись клавиатуры · окно приложения в фокусе";
        KeyboardCaptureArea.Focus();
        UpdateKeyboardCount();
    }

    private void KeyboardStopButton_Click(object sender, RoutedEventArgs e) => StopKeyboardRecording();

    private void StopKeyboardRecording()
    {
        if (!_keyboardRecording) return;
        _keyboardRecording = false;
        _keyboardClock.Stop();
        _pressedKeys.Clear();
        KeyboardRecordButton.Content = "●  Начать запись";
        StatusText.Text = "Запись клавиатуры остановлена";
        UpdateKeyboardCount();
    }

    private void KeyboardClearButton_Click(object sender, RoutedEventArgs e)
    {
        StopKeyboardRecording();
        _keyboardEvents.Clear();
        KeyboardEventsList.Items.Clear();
        StatusText.Text = "Данные клавиатуры очищены";
        UpdateKeyboardCount();
    }

    private void KeyboardCaptureArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        KeyboardCaptureArea.Focus();
    }

    private void KeyboardCaptureArea_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_keyboardRecording)
            StopKeyboardRecording();
    }

    private void KeyboardCaptureArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_keyboardRecording) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.None || !_pressedKeys.Add(key))
        {
            e.Handled = true;
            return;
        }

        AddKeyboardEvent(key, isKeyDown: true);
        e.Handled = true;
    }

    private void KeyboardCaptureArea_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_keyboardRecording) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_pressedKeys.Remove(key))
            AddKeyboardEvent(key, isKeyDown: false);

        e.Handled = true;
    }

    private void AddKeyboardEvent(Key key, bool isKeyDown)
    {
        var item = new KeyboardEvent
        {
            KeyName = key.ToString(),
            IsKeyDown = isKeyDown,
            TimeMs = _keyboardClock.ElapsedMilliseconds
        };
        _keyboardEvents.Add(item);

        var marker = isKeyDown ? "↓ DOWN " : "↑ UP   ";
        var time = TimeSpan.FromMilliseconds(item.TimeMs).ToString(@"mm\:ss\.fff");
        KeyboardEventsList.Items.Add($"{time}   {marker}{item.KeyName}");
        KeyboardEventsList.ScrollIntoView(KeyboardEventsList.Items[^1]);
        UpdateKeyboardCount();
    }

    private void UpdateKeyboardCount()
    {
        KeyboardEventCountText.Text = _keyboardEvents.Count.ToString("N0");
    }

    private void KeyboardSaveButton_Click(object sender, RoutedEventArgs e)
    {
        StopKeyboardRecording();
        if (_keyboardEvents.Count == 0)
        {
            MessageBox.Show("Сначала запиши нажатия клавиш в области захвата.",
                "Нет данных", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить профиль клавиатуры",
            Filter = "Защищённый профиль VXE (*.vxeprofile)|*.vxeprofile|Старый JSON (*.json)|*.json",
            FileName = "keyboard-profile.vxeprofile",
            DefaultExt = ".vxeprofile"
        };
        if (dialog.ShowDialog() != true) return;

        var profile = new KeyboardMacroProfile
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            Events = _keyboardEvents.ToList()
        };
        ProfileProtectionService.Save(dialog.FileName, profile);
        StatusText.Text = "Профиль клавиатуры сохранён";
    }

    private void KeyboardLoadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Загрузить профиль клавиатуры",
            Filter = "Профиль VXE (*.vxeprofile)|*.vxeprofile|Старый JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var profile = ProfileProtectionService.Load<KeyboardMacroProfile>(dialog.FileName);
            StopKeyboardRecording();
            _keyboardEvents.Clear();
            _keyboardEvents.AddRange(profile.Events ?? new List<KeyboardEvent>());
            RefreshKeyboardList();
            StatusText.Text = $"Загружен профиль клавиатуры: {profile.Name}";
            UpdateKeyboardCount();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не удалось открыть профиль клавиатуры",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshKeyboardList()
    {
        KeyboardEventsList.Items.Clear();
        foreach (var item in _keyboardEvents)
        {
            var marker = item.IsKeyDown ? "↓ DOWN " : "↑ UP   ";
            var time = TimeSpan.FromMilliseconds(item.TimeMs).ToString(@"mm\:ss\.fff");
            KeyboardEventsList.Items.Add($"{time}   {marker}{item.KeyName}");
        }
        if (KeyboardEventsList.Items.Count > 0)
            KeyboardEventsList.ScrollIntoView(KeyboardEventsList.Items[^1]);
    }


    // SNAP-TAP LAB: isolated four-direction visual demonstration.
    // Input is handled only while this in-app test surface owns keyboard focus.
    private void SnapTapTestArea_MouseDown(object sender, MouseButtonEventArgs e) =>
        SnapTapTestArea.Focus();

    private void SnapTapTestArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.A: case Key.Left:
                _snapLeftDown = true; break;
            case Key.D: case Key.Right:
                _snapRightDown = true; break;
            case Key.W: case Key.Up:
                _snapUpDown = true; break;
            case Key.S: case Key.Down:
                _snapDownDown = true; break;
            default: return;
        }
        e.Handled = true;
        UpdateSnapTapPreview();
    }

    private void SnapTapTestArea_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.A: case Key.Left:
                _snapLeftDown = false;
                _snapReleasePulseRight = true;
                _snapReleasePulseLeft = false;
                break;
            case Key.D: case Key.Right:
                _snapRightDown = false;
                _snapReleasePulseLeft = true;
                _snapReleasePulseRight = false;
                break;
            case Key.W: case Key.Up:
                _snapUpDown = false;
                _snapReleasePulseDown = true;
                _snapReleasePulseUp = false;
                break;
            case Key.S: case Key.Down:
                _snapDownDown = false;
                _snapReleasePulseUp = true;
                _snapReleasePulseDown = false;
                break;
            default: return;
        }
        QueueSnapPulse();
        e.Handled = true;
        UpdateSnapTapPreview();
    }

    private void QueueSnapPulse()
    {
        _snapPulseTimer.Stop();
        _snapTriggerDelayTimer.Stop();
        if (_snapTriggerDelayMs == 0)
        {
            StartSnapPulse();
            return;
        }
        _snapPendingPulseLeft = _snapReleasePulseLeft;
        _snapPendingPulseRight = _snapReleasePulseRight;
        _snapPendingPulseUp = _snapReleasePulseUp;
        _snapPendingPulseDown = _snapReleasePulseDown;
        _snapReleasePulseLeft = _snapReleasePulseRight = false;
        _snapReleasePulseUp = _snapReleasePulseDown = false;
        UpdateSnapTapPreview();
        _snapTriggerDelayTimer.Interval = TimeSpan.FromMilliseconds(_snapTriggerDelayMs);
        _snapTriggerDelayTimer.Start();
    }

    private void SnapTriggerDelayTimer_Tick(object? sender, EventArgs e)
    {
        _snapTriggerDelayTimer.Stop();
        _snapReleasePulseLeft = _snapPendingPulseLeft;
        _snapReleasePulseRight = _snapPendingPulseRight;
        _snapReleasePulseUp = _snapPendingPulseUp;
        _snapReleasePulseDown = _snapPendingPulseDown;
        _snapPendingPulseLeft = _snapPendingPulseRight = false;
        _snapPendingPulseUp = _snapPendingPulseDown = false;
        StartSnapPulse();
    }

    private void StartSnapPulse()
    {
        _snapPulseTimer.Stop();
        _snapPulseTimer.Interval = TimeSpan.FromMilliseconds(_snapPulseDurationMs);
        // Visual pulse state is already set on key release.
        _snapPulseTimer.Start();
        UpdateSnapTapPreview();
    }

    private void SnapTapPulseDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _snapPulseDurationMs = (int)e.NewValue;
        if (SnapTapPulseDurationText != null)
            SnapTapPulseDurationText.Text = $"{_snapPulseDurationMs} мс";
    }

    private void SnapTapTriggerDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _snapTriggerDelayMs = (int)e.NewValue;
        if (SnapTapTriggerDelayText != null)
            SnapTapTriggerDelayText.Text = $"{_snapTriggerDelayMs} мс";
    }

    private void SnapTapTestArea_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ResetSnapTapPreview();

    private void SnapTapResetButton_Click(object sender, RoutedEventArgs e) =>
        ResetSnapTapPreview();

    private void ResetSnapTapPreview()
    {
        _snapLeftDown = _snapRightDown = _snapUpDown = _snapDownDown = false;
        _snapReleasePulseLeft = _snapReleasePulseRight = false;
        _snapReleasePulseUp = _snapReleasePulseDown = false;
        _snapPulseTimer.Stop();
        _snapTriggerDelayTimer.Stop();
        _snapPendingPulseLeft = _snapPendingPulseRight = _snapPendingPulseUp = _snapPendingPulseDown = false;
        UpdateSnapTapPreview();
        SnapTapStatusText.Text = "Сброшено · нажми на площадку для теста";
    }

    private void UpdateSnapTapPreview()
    {
        var leftActive = _snapReleasePulseLeft || (_snapLeftDown && !_snapRightDown);
        var rightActive = _snapReleasePulseRight || (_snapRightDown && !_snapLeftDown);
        var upActive = _snapReleasePulseUp || (_snapUpDown && !_snapDownDown);
        var downActive = _snapReleasePulseDown || (_snapDownDown && !_snapUpDown);

        SetSnapCardState(SnapLeftCard, SnapLeftState, leftActive, _snapLeftDown, "A");
        SetSnapCardState(SnapRightCard, SnapRightState, rightActive, _snapRightDown, "D");
        SetSnapCardState(SnapUpCard, SnapUpState, upActive, _snapUpDown, "W");
        SetSnapCardState(SnapDownCard, SnapDownState, downActive, _snapDownDown, "S");

        SnapTapStatusText.Text =
            _snapReleasePulseLeft ? "KeyUp D → краткий импульс A (демо)" :
            _snapReleasePulseRight ? "KeyUp A → краткий импульс D (демо)" :
            _snapReleasePulseUp ? "KeyUp S → краткий импульс W (демо)" :
            _snapReleasePulseDown ? "KeyUp W → краткий импульс S (демо)" :
            AnyDirectionHeld() ? $"Удерживаются: {HeldDirectionNames()}" : "Нейтраль";
    }

    private static void SetSnapCardState(Border card, TextBlock state, bool active, bool held, string key)
    {
        card.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(81, 214, 154)) : new SolidColorBrush(Color.FromRgb(48, 57, 73));
        card.Background = active ? new SolidColorBrush(Color.FromRgb(24, 54, 47)) : new SolidColorBrush(Color.FromRgb(25, 31, 42));
        state.Text = active ? "АКТИВНО · ПРИОРИТЕТ" : held ? $"УДЕРЖИВАЕТСЯ · {key}" : "НЕ АКТИВНО";
    }

    private bool AnyDirectionHeld() => _snapLeftDown || _snapRightDown || _snapUpDown || _snapDownDown;

    private string HeldDirectionNames()
    {
        var held = new List<string>();
        if (_snapUpDown) held.Add("W");
        if (_snapLeftDown) held.Add("A");
        if (_snapDownDown) held.Add("S");
        if (_snapRightDown) held.Add("D");
        return string.Join(" + ", held);
    }

    protected override void OnClosed(EventArgs e)
    {
        MousePlaybackStopButton_Click(this, new RoutedEventArgs());
        _snapPulseTimer.Stop();
        _snapTriggerDelayTimer.Stop();
        if (_mouseInput.IsRecording) _mouseInput.StopRecording();
        StopKeyboardRecording();
        _mouseInput.Dispose();
        base.OnClosed(e);
    }
}
