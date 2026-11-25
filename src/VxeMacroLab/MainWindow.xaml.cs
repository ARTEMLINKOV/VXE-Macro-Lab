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
    private bool _snapLeftDown, _snapRightDown, _snapUpDown, _snapDownDown;
    private bool _snapReleasePulseLeft, _snapReleasePulseRight, _snapReleasePulseUp, _snapReleasePulseDown;
    private readonly DispatcherTimer _snapPulseTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UpdateGlobalSnapTapButton();
            DrawMousePath();
        };
        SizeChanged += (_, _) => DrawMousePath();
        _mouseInput.HotkeyPressed += ToggleMouseRecording;
        _mouseInput.MouseMoved += _ => UpdateMouseCount();
        UpdateKeyboardCount();
        _snapPulseTimer.Tick += (_, _) =>
        {
            _snapReleasePulseLeft = _snapReleasePulseRight = false;
            _snapReleasePulseUp = _snapReleasePulseDown = false;
            _snapPulseTimer.Stop();
            UpdateSnapTapPreview();
        };
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

    private void UpdateGlobalSnapTapButton()
    {
        var service = ((App)Application.Current).GlobalSnapTap;
        if (service == null)
        {
            GlobalSnapTapToggleButton.Content = "Snap-Tap: ОШИБКА";
            GlobalSnapTapToggleButton.IsEnabled = false;
            return;
        }
        GlobalSnapTapToggleButton.Content = service.IsEnabled ? "Snap-Tap: ВКЛ" : "Snap-Tap: ВЫКЛ";
        GlobalSnapTapToggleButton.Style = (Style)FindResource(service.IsEnabled ? "PrimaryButton" : "QuietButton");
    }

    private void GlobalSnapTapToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var service = ((App)Application.Current).GlobalSnapTap;
        if (service == null) return;
        service.ToggleEnabled();
        UpdateGlobalSnapTapButton();
        StatusText.Text = service.IsEnabled ? "Snap-Tap включён" : "Snap-Tap выключен";
    }

    private void MouseRecordButton_Click(object sender, RoutedEventArgs e) => ToggleMouseRecording();

    private void ToggleMouseRecording()
    {
        if (_mouseInput.IsRecording)
        {
            _mouseEvents.Clear();
            _mouseEvents.AddRange(_mouseInput.StopRecording());
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
                StatusText.Text = "Записываются движения мыши";
                UpdateMouseCount();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Не удалось включить захват мыши";
                MessageBox.Show(ex.Message, "VXE Macro Lab", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void UpdateMouseCount() { }
    private void DrawMousePath() { }

    private void UpdateKeyboardCount() { }

    private void SnapTapTestArea_MouseDown(object sender, MouseButtonEventArgs e) => SnapTapTestArea.Focus();

    private void SnapTapTestArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.A: case Key.Left: _snapLeftDown = true; break;
            case Key.D: case Key.Right: _snapRightDown = true; break;
            case Key.W: case Key.Up: _snapUpDown = true; break;
            case Key.S: case Key.Down: _snapDownDown = true; break;
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
                _snapLeftDown = false; _snapReleasePulseRight = true; _snapReleasePulseLeft = false; break;
            case Key.D: case Key.Right:
                _snapRightDown = false; _snapReleasePulseLeft = true; _snapReleasePulseRight = false; break;
            case Key.W: case Key.Up:
                _snapUpDown = false; _snapReleasePulseDown = true; _snapReleasePulseUp = false; break;
            case Key.S: case Key.Down:
                _snapDownDown = false; _snapReleasePulseUp = true; _snapReleasePulseDown = false; break;
            default: return;
        }
        _snapPulseTimer.Stop();
        _snapPulseTimer.Start();
        e.Handled = true;
        UpdateSnapTapPreview();
    }

    private void SnapTapTestArea_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ResetSnapTapPreview();

    private void SnapTapResetButton_Click(object sender, RoutedEventArgs e) => ResetSnapTapPreview();

    private void ResetSnapTapPreview()
    {
        _snapLeftDown = _snapRightDown = _snapUpDown = _snapDownDown = false;
        _snapReleasePulseLeft = _snapReleasePulseRight = false;
        _snapReleasePulseUp = _snapReleasePulseDown = false;
        _snapPulseTimer.Stop();
        UpdateSnapTapPreview();
        SnapTapStatusText.Text = "Сброшено · нажми на площадку для теста";
    }

    private void UpdateSnapTapPreview()
    {
        var leftActive = _snapReleasePulseLeft || (_snapLeftDown && !_snapRightDown);
        var rightActive = _snapReleasePulseRight || (_snapRightDown && !_snapLeftDown);
        var upActive = _snapReleasePulseUp || (_snapUpDown && !_snapDownDown);
        var downActive = _snapReleasePulseDown || (_snapDownDown && !_snapUpDown);
        SetSnapCardState(SnapLeftCard, SnapLeftState, leftActive);
        SetSnapCardState(SnapRightCard, SnapRightState, rightActive);
        SetSnapCardState(SnapUpCard, SnapUpState, upActive);
        SetSnapCardState(SnapDownCard, SnapDownState, downActive);
        SnapTapStatusText.Text = leftActive || rightActive || upActive || downActive ? "Направление активировано" : "Нейтраль";
    }

    private static void SetSnapCardState(Border card, TextBlock state, bool active)
    {
        card.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(81, 214, 154)) : new SolidColorBrush(Color.FromRgb(48, 57, 73));
        card.Background = active ? new SolidColorBrush(Color.FromRgb(24, 54, 47)) : new SolidColorBrush(Color.FromRgb(25, 31, 42));
        state.Text = active ? "АКТИВНО" : "НЕ АКТИВНО";
    }

    protected override void OnClosed(EventArgs e)
    {
        _snapPulseTimer.Stop();
        if (_mouseInput.IsRecording) _mouseInput.StopRecording();
        _mouseInput.Dispose();
        base.OnClosed(e);
    }
}
