using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Remnant2Overlay.Icons;

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

    // Phone sync: upload a few seconds after a change settles, and check for the phone's changes every minute.
    private readonly PhoneSync _phoneSync = new();
    private readonly DispatcherTimer _syncDebounce = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _syncPoll = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _syncing, _syncAgain;

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

        _syncDebounce.Tick += (_, _) =>
        {
            _syncDebounce.Stop();
            _ = SyncPhoneAsync();
        };
        _syncPoll.Tick += (_, _) => _ = SyncPhoneAsync();
        _vm.SyncNeeded += () =>
        {
            if (!_phoneSync.SignedIn) return;
            _syncDebounce.Stop();
            _syncDebounce.Start();
        };

        _vm.LoadSave(null);
        WatchSave();
        Loaded += (_, _) =>
        {
            // Fill in any icons missing from the local cache; runs in the background.
            _ = _vm.SyncIconsAsync();
            // Automatic once set up: the first start opens the browser to sign in, later starts reuse the saved token.
            if (PhoneSync.HasClientFile && !_settings.PhoneSyncOff) _ = StartPhoneSyncAsync();
        };
    }

    private async Task StartPhoneSyncAsync()
    {
        if (!PhoneSync.HasClientFile)
        {
            _vm.SyncStatus = $"Phone sync: Google client file missing ({PhoneSync.ClientFile}). See README.";
            PhoneSyncToggle.IsChecked = false;
            return;
        }

        try
        {
            _vm.SyncStatus = "Phone sync: signing in with Google (check your browser)…";
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await _phoneSync.SignInAsync(timeout.Token);
            _settings.PhoneSyncOff = false;
            PhoneSyncToggle.IsChecked = true;
            _syncPoll.Start();
            await SyncPhoneAsync();
        }
        catch (Exception e)
        {
            _vm.SyncStatus = $"Phone sync: sign-in failed ({e.Message})";
            PhoneSyncToggle.IsChecked = false;
        }
    }

    private async Task SyncPhoneAsync()
    {
        if (!_phoneSync.SignedIn) return;
        // One round trip at a time; a change during a sync gets its own pass afterwards.
        if (_syncing)
        {
            _syncAgain = true;
            return;
        }

        _syncing = true;
        try
        {
            do
            {
                _syncAgain = false;
                var merged = await _phoneSync.SyncAsync(_vm.BuildSyncDocument());
                _vm.ApplySynced(merged);
                _vm.SyncStatus = $"Phone sync: up to date at {DateTime.Now:t}";
            } while (_syncAgain);
        }
        catch (Remnant2Overlay.Sync.DriveSyncException e) when (e.NeedsSignIn)
        {
            _vm.SyncStatus = "Phone sync: Google sign-in expired or was revoked. Turn Phone sync off and on to sign in again.";
        }
        catch (Exception e)
        {
            // Offline or Drive hiccup: the next change or the minute timer tries again.
            _vm.SyncStatus = $"Phone sync failed at {DateTime.Now:t}: {e.Message}";
        }
        finally
        {
            _syncing = false;
        }
    }

    private async void PhoneSync_Click(object sender, RoutedEventArgs e)
    {
        if (PhoneSyncToggle.IsChecked == true)
        {
            if (!PhoneSync.HasClientFile)
            {
                PhoneSyncToggle.IsChecked = false;
                MessageBox.Show(this,
                    "Phone sync needs a Google OAuth client for this PC.\n\n" +
                    $"Download the Desktop app client JSON from Google Cloud Console and save it as:\n{PhoneSync.ClientFile}\n\n" +
                    "The README's \"Phone sync\" section has the steps.",
                    "Remnant 2 Overlay", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await StartPhoneSyncAsync();
        }
        else
        {
            _syncPoll.Stop();
            _syncDebounce.Stop();
            _settings.PhoneSyncOff = true;
            await _phoneSync.SignOutAsync();
            _vm.SyncStatus = "Phone sync off. Your data stays in Google Drive for the phone app.";
        }
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
        _vm.CancelIconSync();
        _watcher?.Dispose();
        _syncPoll.Stop();
        _syncDebounce.Stop();
        base.OnClosing(e);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ItemTileViewModel tile }) return;
        // Manual mode: click marks owned/missing. Otherwise click opens the item's details.
        if (_vm.ManualMode) _vm.Toggle(tile);
        else _vm.ShowDetails(tile);
    }

    private void Tile_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemTileViewModel tile }) _vm.ShowDetails(tile);
    }

    private void RunTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is RunNode { ItemId: { } id }) _vm.ShowDetails(id);
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e) => _vm.CloseDetails();

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

    private void GameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select your Remnant 2 install folder" };
        if (dialog.ShowDialog(this) != true) return;
        if (GameLocator.NormalizePaksFolder(dialog.FolderName) is null)
        {
            MessageBox.Show(this, "That folder doesn't contain Remnant 2's .pak files. Pick the Remnant2 folder in steamapps\\common.",
                "Remnant 2 Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _vm.SetGameFolder(dialog.FolderName);
    }

    private void ReextractIcons_Click(object sender, RoutedEventArgs e) => _ = _vm.SyncIconsAsync(force: true);
    private void Reload_Click(object sender, RoutedEventArgs e) => _vm.LoadSave(_settings.ProfilePath);
    private void ClearManual_Click(object sender, RoutedEventArgs e) => _vm.ClearManual();
    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
