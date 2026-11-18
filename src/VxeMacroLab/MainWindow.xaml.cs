using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using VxeMacroLab.Models;
using VxeMacroLab.Services;

namespace VxeMacroLab;

public partial class MainWindow : Window
{
    private readonly RawMouseInputService _input = new();
    private List<MouseEvent> _currentEvents = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => DrawPath();
        SizeChanged += (_, _) => DrawPath();
        _input.HotkeyPressed += ToggleRecording;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            _input.Attach(new WindowInteropHelper(this).Handle);
            StatusText.Text = "Готово · F8 для записи";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка инициализации";
            MessageBox.Show(ex.Message, "VXE Macro Lab", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e) => ToggleRecording();

    private void ToggleRecording()
    {
        if (_input.IsRecording)
        {
            _currentEvents = _input.StopRecording();
            RecordButton.Content = "● Запись";
            StatusText.Text = "Запись остановлена";
            UpdateCount();
            DrawPath();
        }
        else
        {
            _input.StartRecording();
            RecordButton.Content = "■ Идёт запись";
            StatusText.Text = "Запись движений мыши…";
            _currentEvents.Clear();
            UpdateCount();
            PreviewCanvas.Children.Clear();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_input.IsRecording) ToggleRecording();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_input.IsRecording) ToggleRecording();
        if (_currentEvents.Count == 0)
        {
            MessageBox.Show("Сначала запиши траекторию.", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Профиль VXE (*.json)|*.json",
            FileName = "mouse-profile.json",
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog() != true) return;

        var profile = new MacroProfile { Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), Events = _currentEvents };
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
        StatusText.Text = "Профиль сохранён";
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Профиль VXE (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var profile = JsonSerializer.Deserialize<MacroProfile>(File.ReadAllText(dialog.FileName));
            if (profile is null) throw new InvalidDataException("Файл профиля пуст или имеет неверный формат.");
            _currentEvents = profile.Events ?? new List<MouseEvent>();
            StatusText.Text = $"Загружен профиль: {profile.Name}";
            UpdateCount();
            DrawPath();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не удалось открыть профиль", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_input.IsRecording) _input.StopRecording();
        _currentEvents.Clear();
        PreviewCanvas.Children.Clear();
        RecordButton.Content = "● Запись";
        StatusText.Text = "Данные очищены";
        UpdateCount();
    }

    private void UpdateCount()
    {
        var count = _input.IsRecording ? _input.Events.Count : _currentEvents.Count;
        EventCountText.Text = $"{count:N0} событий";
    }

    private void DrawPath()
    {
        if (!IsLoaded || PreviewCanvas.ActualWidth < 10 || PreviewCanvas.ActualHeight < 10) return;
        PreviewCanvas.Children.Clear();
        if (_currentEvents.Count < 2) return;

        double x = 0, y = 0;
        var points = new List<Point> { new(0, 0) };
        foreach (var item in _currentEvents)
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
        var scale = Math.Min((PreviewCanvas.ActualWidth - margin * 2) / width,
                             (PreviewCanvas.ActualHeight - margin * 2) / height);
        var offsetX = (PreviewCanvas.ActualWidth - width * scale) / 2;
        var offsetY = (PreviewCanvas.ActualHeight - height * scale) / 2;

        var line = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(88, 166, 255)), StrokeThickness = 2.2 };
        foreach (var point in points)
            line.Points.Add(new Point(offsetX + (point.X - minX) * scale,
                                      offsetY + (point.Y - minY) * scale));
        PreviewCanvas.Children.Add(line);

        AddMarker(line.Points.First(), "#FF57C785", 7);
        AddMarker(line.Points.Last(), "#FFE3A64B", 7);
    }

    private void AddMarker(Point point, string color, double size)
    {
        var marker = new Ellipse
        {
            Width = size, Height = size,
            Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!
        };
        Canvas.SetLeft(marker, point.X - size / 2);
        Canvas.SetTop(marker, point.Y - size / 2);
        PreviewCanvas.Children.Add(marker);
    }

    protected override void OnClosed(EventArgs e)
    {
        _input.Dispose();
        base.OnClosed(e);
    }
}
