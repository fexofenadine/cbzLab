using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using cbzLab.Dialogs;
using cbzLab.Services;

namespace cbzLab;

//the desktop's host for the shared MainView: remembers its own size and position, closes (after the
//view's unsaved-changes check), and updates the app - the parts no other cbzLab app shares
public partial class MainWindow : Window
{
    private readonly UpdateService _updateService;
    private bool _forceClose;

    //set by HandleUpdateCheckResultAsync once an update has been downloaded and is
    //ready to install; launched from PersistUiState right before the app actually
    //exits, never before - see UpdateService's own doc comment for why
    private string? _pendingUpdateSwapScript;

    //command-line paths, opened once the window is actually shown so dialogs have a valid owner
    private List<string> _pendingStartupPaths = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = $"cbzLab {AppVersion.Display}";
        LoadWindowIcon();
        RestoreWindowGeometry();

        _updateService = new UpdateService(View.Log, AppVersion.Display);
        View.UpdatesSupported = true;
        View.CheckForUpdatesRequested += OnCheckForUpdates;
        View.QuitRequested += (_, _) => Close();

        Closing += OnMainWindowClosing;
        Opened += OnMainWindowOpened;
    }

    public void QueueStartupPaths(List<string> paths) => _pendingStartupPaths = paths;

    private async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnMainWindowOpened;
        await View.OnShownAsync(_pendingStartupPaths);
        _pendingStartupPaths = new();

        if (View.Settings.Settings.CheckForUpdatesOnStartup)
        {
            var result = await _updateService.CheckAsync();
            await HandleUpdateCheckResultAsync(result, silentIfUpToDate: true);
        }
    }

    //loose sanity bounds, not real multi-monitor awareness - same thresholds as the winui original
    private void RestoreWindowGeometry()
    {
        var s = View.Settings.Settings;
        var validSize = s.WindowWidth is >= 400 and <= 8000 && s.WindowHeight is >= 300 and <= 8000;
        if (validSize)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        var validPos = s.WindowX != int.MinValue && s.WindowY != int.MinValue
            && s.WindowX is >= -2000 and <= 8000 && s.WindowY is >= -2000 and <= 8000;
        if (validPos)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(s.WindowX, s.WindowY);
        }
    }

    private void LoadWindowIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            using var stream = File.OpenRead(path);
            Icon = new WindowIcon(stream);
        }
        catch
        {
            //no shipped icon asset - falls back to Avalonia's default window icon, not fatal
        }
    }

    //Closing can't be awaited directly: cancel synchronously, then resolve async and force-close if appropriate
    private async void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose)
        {
            PersistUiState();
            return;
        }

        if (View.ViewModel.DirtyFiles().Count == 0)
        {
            PersistUiState();
            return;
        }

        e.Cancel = true;
        if (!await View.ConfirmCloseAsync())
            return;

        _forceClose = true;
        PersistUiState();
        Close();
    }

    //only called right before an actual close, not a cancelled one
    private void PersistUiState()
    {
        View.PrepareForExit();
        var s = View.Settings.Settings;
        s.WindowWidth = Width;
        s.WindowHeight = Height;
        s.WindowX = Position.X;
        s.WindowY = Position.Y;
        View.Settings.Save();

        //only launched here, right before an actual close - never earlier, so a
        //cancelled close (unsaved changes, user hits Cancel) never triggers the swap
        if (_pendingUpdateSwapScript is not null)
            UpdateService.LaunchSwapScript(_pendingUpdateSwapScript);
    }

    private async void OnCheckForUpdates(object? sender, EventArgs e)
    {
        View.ViewModel.StatusText = "Checking for updates…";
        var result = await _updateService.CheckAsync();
        await HandleUpdateCheckResultAsync(result, silentIfUpToDate: false);
    }

    //shared by the manual Help > Check for Updates action and the optional startup
    //check - silentIfUpToDate suppresses the "you're up to date"/error dialogs for the
    //startup path, since popping a dialog on every single launch would be obnoxious;
    //an actual available update is never silent either way, since installing one
    //needs explicit confirmation regardless of how the check was triggered
    private async Task HandleUpdateCheckResultAsync(UpdateCheckResult result, bool silentIfUpToDate)
    {
        var version = AppVersion.Display;
        if (result.ErrorMessage is not null)
        {
            if (!silentIfUpToDate)
                await MessageDialog.ShowAsync(this, "Check for Updates", result.ErrorMessage);
            return;
        }

        if (!result.UpdateAvailable)
        {
            if (!silentIfUpToDate)
                await MessageDialog.ShowAsync(this, "Check for Updates", $"You're up to date ({version}).");
            return;
        }

        if (!View.Settings.Settings.AutoUpdateEnabled || result.AssetDownloadUrl is null)
        {
            await MessageDialog.ShowAsync(this, "Check for Updates",
                $"A newer version is available: {result.LatestVersionTag} (you have {version}).\n\n{result.ReleaseUrl}");
            return;
        }

        var install = await ConfirmDialog.ShowAsync(this, "Update available",
            $"Version {result.LatestVersionTag} is available (you have {version}). Download and install it now? "
            + "cbzLab will close and reopen once the update is ready.", "Download and Install");
        if (!install)
            return;

        View.ViewModel.StatusText = "Downloading update…";
        var scriptPath = await _updateService.PrepareUpdateAsync(result);
        if (scriptPath is null)
        {
            await MessageDialog.ShowAsync(this, "Update failed",
                "Couldn't download or prepare the update. Check the logs for details, or download it manually from "
                + result.ReleaseUrl);
            return;
        }

        _pendingUpdateSwapScript = scriptPath;
        View.ViewModel.StatusText = "Update ready - closing to install…";
        Close();
    }
}
