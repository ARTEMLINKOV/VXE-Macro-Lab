using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => DrawMousePath();
        SizeChanged += (_, _) => DrawMousePath();
        _mouseInput.HotkeyPressed += ToggleMouseRecording;
        _mouseInput.MouseMoved += _ => UpdateMouseCount();
        UpdateKeyboardCount();
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
            _mouseEvents.Clear();
            _mouseInput.StartRecording();
            MouseRecordButton.Content = "■  Идёт запись";
            StatusText.Text = "Записываются движения мыши";
            UpdateMouseCount();
            MousePreviewCanvas.Children.Clear();
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

    protected override void OnClosed(EventArgs e)
    {
        if (_mouseInput.IsRecording) _mouseInput.StopRecording();
        StopKeyboardRecording();
        _mouseInput.Dispose();
        base.OnClosed(e);
    }
}
