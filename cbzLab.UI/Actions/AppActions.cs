using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using cbzLab.Dialogs;
using cbzLab.Models;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab;

/// <summary>
/// Everything the app does, shared by every layout: the desktop's MainView and the phone's
/// MobileShell both call these and differ only in how they show things. Owns the services and the
/// main view model. Dialogs open over <see cref="Owner"/>, the layout that created this.
/// What only a layout can do (rebuild a menu, select a book, show the editor) it is asked to do
/// through the events below.
/// </summary>
public sealed partial class AppActions
{
    public LogService Log { get; }
    public SettingsService Settings { get; }
    public SchemaService Schema { get; }
    public ThemeService Theme { get; }
    public ArchiveService Archive { get; }
    public ValidationService Validation { get; }
    public ComicVineCacheService ComicVineCache { get; }
    public ComicVineService ComicVine { get; }
    public MainViewModel ViewModel { get; }

    private readonly CombineService _combine;
    private readonly FooterTrimService _footerTrim;
    private readonly RecentValuesService _recentValues;
    private readonly AutosaveService _autosave;
    private readonly DispatcherTimer _autosaveTimer;

    //the layout dialogs open over; it must be inside a DialogHost
    public Visual Owner { get; }

    //the recent-files list changed, so a layout showing it rebuilds it
    public event EventHandler? RecentFilesChanged;

    //settings were saved (theme, fonts, density may have changed)
    public event EventHandler? SettingsApplied;

    //select this book in the list, staying where the user is
    public event Action<ComicFileViewModel>? SelectRequested;

    //select these books and show them in the editor (after Combine, Trim, or from grid view)
    public event Action<IReadOnlyList<ComicFileViewModel>>? EditRequested;

    public AppActions(Visual owner)
    {
        Owner = owner;

        //same construction order as App.xaml.cs in the winui project
        Log = new LogService();
        Settings = new SettingsService(Log);
        Schema = new SchemaService(Settings, Log);
        Theme = new ThemeService(Settings, Log);
        Archive = new ArchiveService(Settings, Schema, Log);
        _combine = new CombineService(Log, Schema.Constraints.ImageExtensions);
        _footerTrim = new FooterTrimService(Log, Schema.Constraints.ImageExtensions);
        Validation = new ValidationService(Schema);
        _recentValues = new RecentValuesService(Settings, Log);
        ComicVineCache = new ComicVineCacheService(Settings, Log);
        ComicVine = new ComicVineService(Settings, ComicVineCache, Log);
        _autosave = new AutosaveService(Settings, Log);
        ViewModel = new MainViewModel(Schema, Settings, Validation, _recentValues);

        //a handler that throws would otherwise take the whole app down, edits since the last autosave
        //with it: the error is logged and shown, unsaved edits are snapshotted, and the app carries on
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Log.Error("Unhandled error", e.Exception);
            try { SnapshotDirtyFiles(); } catch { }
            ViewModel.StatusText = $"Something went wrong: {e.Exception.Message}";
            if (!_showingCrash)
            {
                _showingCrash = true;
                _ = ShowCrashAsync(e.Exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task error", e.Exception);
            e.SetObserved();
        };

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autosaveTimer.Tick += (_, _) => SnapshotDirtyFiles();
        _autosaveTimer.Start();
    }

    //set by a host that can hand over files from other apps (android's "choose any app" picker);
    //returns the ids picked, empty if cancelled
    public Func<Task<IReadOnlyList<string>>>? ExternalOpenPicker { get; set; }

    //set by a host that picks folders its own way (android hands out folder grants, not paths): takes
    //whether to offer every app that can pick a folder, returns the archive ids inside, empty if cancelled
    public Func<bool, Task<IReadOnlyList<string>>>? FolderPicker { get; set; }

    private void RaiseRecentFilesChanged() => RecentFilesChanged?.Invoke(this, EventArgs.Empty);

    //---------------------------------------------------------------- crash reporting

    //one at a time: an error raised while the dialog is up only goes to the log
    private bool _showingCrash;

    private async Task ShowCrashAsync(Exception ex)
    {
        try
        {
            await MessageDialog.ShowAsync(Owner, "Something went wrong",
                $"{ex.Message}\n\nYour unsaved edits were kept. The details are in the log.");
        }
        catch
        {
            //no dialog host yet (very early in start-up); the log and status line still have it
        }
        finally
        {
            _showingCrash = false;
        }
    }

    //---------------------------------------------------------------- start-up and exit

    /// <summary>
    /// Called once the layout is on screen, so dialogs have somewhere to open: opens any files the
    /// app was started with, then offers back unsaved edits from a session that didn't end cleanly.
    /// </summary>
    public async Task OnShownAsync(IReadOnlyList<string> startupPaths)
    {
        if (startupPaths.Count > 0)
            await OpenPathsAsync(startupPaths.ToList());
        await OfferAutosaveRecoveryAsync();
    }

    /// <summary>
    /// Asked before the app closes: true when it's fine to close, after offering to save any unsaved
    /// edits. A failed save answers false, so nothing is silently lost.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        var dirty = ViewModel.DirtyFiles();
        if (dirty.Count == 0)
            return true;

        var choice = await UnsavedChangesDialog.ShowAsync(Owner, dirty.Select(f => f.FileName));
        if (choice == UnsavedChoice.Cancel)
            return false;
        if (choice == UnsavedChoice.Save)
            return await SaveFilesAsync(dirty, confirmFormats: true);
        return true;
    }

    /// <summary>
    /// Called right before an actual close (never a cancelled one): records the open tab for next
    /// time, then clears crash-recovery drafts, since everything left is now either saved or
    /// deliberately discarded. The host saves the settings afterwards.
    /// </summary>
    public void PrepareForExit(int activeTab)
    {
        Settings.Settings.ActiveTab = activeTab;

        //a draft still being written would land after the clear and be offered back anyway, so it's let finish first
        _autosaveTimer.Stop();
        try { _autosaveWrite?.Wait(TimeSpan.FromSeconds(5)); } catch { /* its failure is already logged */ }
        _autosave.ClearAll();
    }

    //---------------------------------------------------------------- autosave

    //EditVersion each file's current draft was written at
    private readonly Dictionary<ComicFileViewModel, int> _autosavedVersion = new();
    private bool _autosaveRunning;
    //the background write itself, so exit can wait for it before clearing drafts
    private Task? _autosaveWrite;

    //writes any unsaved edits to their crash-recovery drafts now, rather than at the next timer tick
    //- for a phone about to move the app to the background, where it can be killed without warning
    public void FlushAutosave() => SnapshotDirtyFiles();

    /// <summary>
    /// Writes a crash-recovery draft for each unsaved file changed since its last draft. Used to
    /// rewrite every unsaved file every tick, on the ui thread: after a batch edit of a thousand
    /// books that was a thousand file writes every 30 seconds, whether anything had changed or not.
    /// Values are copied here on the ui thread; only the writing happens in the background.
    /// </summary>
    private async void SnapshotDirtyFiles()
    {
        if (_autosaveRunning)
            return;

        var batch = ViewModel.OpenFiles
            .Where(f => f.IsDirty && (!_autosavedVersion.TryGetValue(f, out var v) || v != f.EditVersion))
            .Select(f => (File: f, Path: f.Path, Version: f.EditVersion,
                Values: new Dictionary<string, string>(f.CurrentValues, StringComparer.Ordinal)))
            .ToList();
        if (batch.Count == 0)
            return;

        _autosaveRunning = true;
        try
        {
            _autosaveWrite = Task.Run(() =>
            {
                foreach (var item in batch)
                    _autosave.Save(item.Path, item.Values);
            });
            await _autosaveWrite;

            //a file saved, reverted or closed while its draft was being written must not keep one
            var open = ViewModel.OpenFiles.ToHashSet();
            foreach (var item in batch)
            {
                if (item.File.IsDirty && open.Contains(item.File) && PathComparison.Same(item.File.Path, item.Path))
                    _autosavedVersion[item.File] = item.Version;
                else
                    _autosave.Clear(item.Path);
            }
            foreach (var gone in _autosavedVersion.Keys.Where(f => !open.Contains(f)).ToList())
                _autosavedVersion.Remove(gone);
        }
        catch (Exception ex)
        {
            Log.Warning($"Autosave failed: {ex.Message}");
        }
        finally
        {
            _autosaveRunning = false;
        }
    }

    //leftover drafts mean the last session ended uncleanly (crash, force-kill, power loss) -
    //PrepareForExit clears the whole autosave folder on every clean exit, so anything still
    //here was never cleanly resolved one way or the other
    private async Task OfferAutosaveRecoveryAsync()
    {
        var drafts = _autosave.LoadAll().Where(d => ArchiveStorage.Current.Exists(d.OriginalPath)).ToList();
        if (drafts.Count == 0)
            return;

        var names = drafts.Select(d => ArchiveStorage.Current.Name(d.OriginalPath));
        var restore = await ConfirmDialog.ShowAsync(Owner, "Restore unsaved changes",
            "cbzLab didn't close cleanly last time. Unsaved changes were found for:\n\n"
            + string.Join("\n", names) + "\n\nRestore them?", "Restore");

        if (restore)
        {
            await OpenPathsAsync(drafts.Select(d => d.OriginalPath).ToList());
            using (ViewModel.History.Begin("Restore Unsaved Changes"))
            using (ViewModel.DeferRefresh())
            {
                foreach (var draft in drafts)
                {
                    var file = ViewModel.OpenFiles.FirstOrDefault(
                        f => PathComparison.Same(f.Path, draft.OriginalPath));
                    file?.ReplaceCurrentValues(draft.Values);
                }
            }
            ViewModel.RefreshEditor();
            ViewModel.StatusText = $"Restored unsaved changes for {drafts.Count} file(s)";
        }

        foreach (var draft in drafts)
            _autosave.Clear(draft.OriginalPath);
    }

    //---------------------------------------------------------------- covers

    //a fast scroll through a big list attaches dozens of rows at once; unthrottled, each started its own
    //read of a whole archive, which thrashes a spinning disk or a NAS
    private static readonly System.Threading.SemaphoreSlim CoverLoadGate = new(4);

    public async Task EnsureCoverAsync(ComicFileViewModel file)
    {
        if (file.HasCover)
            return;

        var token = file.BeginCoverLoad();
        await CoverLoadGate.WaitAsync();
        try
        {
            //scrolled past while waiting its turn - skip the archive read entirely
            if (!file.IsCoverLoadCurrent(token))
                return;

            //read and decode both off the ui thread - decoding a full-size page there caused scroll jank
            var path = file.Path;
            var cover = await Task.Run(() => ComicFileViewModel.DecodeCover(Archive.ReadCoverBytes(path)));
            //discarded (and disposed) if the row was released meanwhile
            file.ApplyCover(token, cover);
        }
        catch (Exception ex)
        {
            Log.Warning($"Could not load cover for '{file.Path}': {ex.Message}");
        }
        finally
        {
            CoverLoadGate.Release();
        }
    }

    //the editor binds the current file's cover too, so a selected file keeps its thumbnail even once
    //its row has scrolled out of the list
    public void ReleaseCoverUnlessSelected(ComicFileViewModel file)
    {
        if (!ViewModel.SelectedFiles.Contains(file))
            file.ReleaseCover();
    }

    //---------------------------------------------------------------- leaving a field

    //a field's box lost focus. its value joins the recent ones here rather than on each keystroke,
    //so half-typed text never does, and "on leaving the field" validation runs.
    //remember is false for the date box, whose text isn't one tag's value
    public void FieldLeft(FieldViewModel field, bool remember = true)
    {
        //typing in the field was one undo step; whatever is typed next is another
        ViewModel.History.Seal();
        if (remember)
        {
            _recentValues.Record(field.Tag, field.Value);
            if (!ViewModel.IsBatchMode)
                field.DistinctValues = ViewModel.RecentPickerFor(field);
        }
        if (Settings.Settings.LiveValidationMode == "blur")
            ViewModel.ValidateFieldNow(field);
    }

    //---------------------------------------------------------------- theme

    public void ApplyTheme(string name)
    {
        Theme.Apply(name);
        Settings.Settings.Theme = name;
        Settings.Save();
    }
}
