using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Remnant2Overlay.App;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x5232;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x2, ModShift = 0x4, VkO = 0x4F;

    private readonly Settings _settings = Settings.Load();
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _reloadDebounce = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private FileSystemWatcher? _watcher;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(_settings);
        DataContext = _vm;

        Width = _settings.Width;
        Height = _settings.Height;
        if (_settings.Left is { } left && _settings.Top is { } top)
        {
            Left = left;
            Top = top;
        }
        OpacitySlider.Value = _settings.Opacity;

        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            _vm.LoadSave(_settings.ProfilePath);
        };

        _vm.LoadSave(null);
        WatchSave();
    }

    // The game rewrites the saves on checkpoints; reload shortly after they settle.
    private void WatchSave()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_settings.ProfilePath is not { } path || !File.Exists(path)) return;

        // Watch profile.sav and save_N.sav: the world save changes when you roll or progress a run.
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, "*.sav")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)!.AddHook(WndProc);
        RegisterHotKey(handle, HotkeyId, ModControl | ModShift, VkO);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            if (IsVisible) Hide();
            else
            {
                Show();
                Activate();
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _settings.Opacity = OpacitySlider.Value;
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.Save();
        UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
        _watcher?.Dispose();
        base.OnClosing(e);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemTileViewModel tile }) _vm.Toggle(tile);
    }

    private void OpenSave_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Remnant 2 profile.sav",
            Filter = "Remnant 2 profile|profile.sav|Save files|*.sav",
            InitialDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Remnant2", "Steam"),
        };
        if (dialog.ShowDialog(this) != true) return;
        _vm.LoadSave(dialog.FileName);
        WatchSave();
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => _vm.LoadSave(_settings.ProfilePath);
    private void ClearManual_Click(object sender, RoutedEventArgs e) => _vm.ClearManual();
    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
