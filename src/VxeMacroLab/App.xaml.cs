using System.Windows;
using VxeMacroLab.Services;

namespace VxeMacroLab;

public partial class App : Application
{
    private GlobalSnapTapService? _globalSnapTap;

    public GlobalSnapTapService? GlobalSnapTap => _globalSnapTap;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _globalSnapTap = new GlobalSnapTapService();
            _globalSnapTap.Start();
        }
        catch (Exception ex)
        {
            _globalSnapTap?.Dispose();
            _globalSnapTap = null;
            MessageBox.Show(ex.Message, "VXE Macro Lab · Snap-Tap",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _globalSnapTap?.Dispose();
        base.OnExit(e);
    }
}
