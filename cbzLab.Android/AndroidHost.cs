using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Avalonia.Controls;
using cbzLab.Services;

namespace cbzLab.Android;

//what only an android app does around the shared MainView: unpacking the bundled files, choosing the
//archive storage, handing over the books another app opened us with, and the lifecycle hooks
public static class AndroidHost
{
    private static bool _prepared;
    private static bool _shown;

    public static MainActivity? Activity { get; set; }
    public static MainView? View { get; private set; }

    //the books the app was started with, or handed later, as document uris
    public static List<string> PendingBooks(Intent? intent)
    {
        var books = new List<string>();
        if (intent?.Action == Intent.ActionView && intent.Data is { } data)
            books.Add(data.ToString()!);
        return books;
    }

    public static MainView CreateView()
    {
        Prepare(global::Android.App.Application.Context);
        var view = new MainView { TouchMode = true, ExternalOpenPicker = PickFromAnyAppAsync, FolderPicker = PickFolderAsync };
        View = view;
        view.QuitRequested += async (_, _) => await LeaveAsync(view);
        view.Loaded += async (_, _) =>
        {
            if (_shown)
                return;
            _shown = true;

            //the keyboard: edge-to-edge windows ignore adjustResize, so the view is lifted by the
            //height the keyboard covers, which keeps the field being typed into in sight
            if (TopLevel.GetTopLevel(view) is { InputPane: { } pane } window)
            {
                //read from android's own insets on every layout pass: the input pane's rect went stale
                //across a rotation, lifting the view too far and leaving a black band (or most of a
                //landscape screen black)
                void Lift()
                {
                    //the input pane's own figure is only a fallback, so it is capped at half the screen
                    var covered = KeyboardHeight(window.RenderScaling)
                                  ?? Math.Min(pane.State == Avalonia.Controls.Platform.InputPaneState.Open ? pane.OccludedRect.Height : 0,
                                              window.Bounds.Height / 2);
                    view.SetRoomAboveKeyboard(covered > 0 ? window.Bounds.Height - covered : double.PositiveInfinity);
                    if (view.Margin.Bottom == covered)
                        return;
                    view.Margin = new Avalonia.Thickness(0, 0, 0, covered);
                    //what's left can be a thin strip (a phone on its side), so the field being typed in is
                    //scrolled back into it once the layout has caught up
                    if (covered > 0)
                        Avalonia.Threading.Dispatcher.UIThread.Post(
                            () => (window.FocusManager?.GetFocusedElement() as Avalonia.Controls.Control)?.BringIntoView(),
                            Avalonia.Threading.DispatcherPriority.Background);
                }
                pane.StateChanged += (_, _) => Lift();
                window.SizeChanged += (_, _) => Lift();
                if (Activity?.Window?.DecorView.ViewTreeObserver is { } observer)
                    observer.GlobalLayout += (_, _) => Lift();
            }

            //the back button: an open dialog, then the editor back to the list, then leaving
            if (TopLevel.GetTopLevel(view) is { } top)
                top.BackRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (!view.HandleBack())
                        _ = LeaveAsync(view);
                };

            await view.OnShownAsync(PendingBooks(Activity?.Intent));
        };
        return view;
    }

    //how far the keyboard reaches above the navigation bar, in view units; null before android 11,
    //where the insets don't name the keyboard and the input pane's own figure is used instead
    private static double? KeyboardHeight(double scaling)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || Activity?.Window?.DecorView.RootWindowInsets is not { } insets)
            return null;
        var ime = insets.GetInsets(global::Android.Views.WindowInsets.Type.Ime()).Bottom;
        var bars = insets.GetInsets(global::Android.Views.WindowInsets.Type.SystemBars()).Bottom;
        return Math.Max(0, ime - bars) / scaling;
    }

    //---------------------------------------------------------------- picking from any app

    private const int PickRequest = 4201;
    private static TaskCompletionSource<IReadOnlyList<string>>? _pick;

    //android's "get content" chooser lists every app that can hand over a file - Solid Explorer, Material
    //Files and so on, including their network drives - not just the document providers the system picker
    //shows. what comes back is a document uri like any other; a manager may make it read-only or a pipe
    //(both handled: a failed save is reported with the original untouched, and a pipe is copied to read)
    private static Task<IReadOnlyList<string>> PickFromAnyAppAsync()
    {
        var activity = Activity ?? throw new InvalidOperationException("no activity to ask from");
        _pick?.TrySetResult(Array.Empty<string>());
        _pick = new TaskCompletionSource<IReadOnlyList<string>>();

        var intent = new Intent(Intent.ActionGetContent);
        intent.SetType("*/*");
        intent.AddCategory(Intent.CategoryOpenable);
        intent.PutExtra(Intent.ExtraAllowMultiple, true);
        //a hint, not a filter: many managers don't know the comic types, so none is excluded
        intent.PutExtra(Intent.ExtraMimeTypes, new[]
        {
            "application/vnd.comicbook+zip", "application/vnd.comicbook-rar", "application/x-cbz", "application/x-cbr",
            "application/zip", "application/x-rar-compressed", "application/vnd.rar", "application/octet-stream",
        });
        activity.StartActivityForResult(Intent.CreateChooser(intent, "Open comic archives with"), PickRequest);
        return _pick.Task;
    }

    //---------------------------------------------------------------- picking a folder

    private const int FolderRequest = 4202;
    private static TaskCompletionSource<IReadOnlyList<string>>? _folderPick;

    //a folder is a tree grant, not a path: the user picks one (the system's picker, or any app that
    //offers the same, such as a file manager with network drives) and every archive under it is listed
    //as a document uri. the grant is kept, so the books can be reopened and saved after a restart
    private static Task<IReadOnlyList<string>> PickFolderAsync(bool anyApp)
    {
        var activity = Activity ?? throw new InvalidOperationException("no activity to ask from");
        _folderPick?.TrySetResult(Array.Empty<string>());
        _folderPick = new TaskCompletionSource<IReadOnlyList<string>>();

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission
                        | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        activity.StartActivityForResult(anyApp ? Intent.CreateChooser(intent, "Open a folder with") : intent, FolderRequest);
        return _folderPick.Task;
    }

    private static async void FinishFolderPick(global::Android.Net.Uri? tree)
    {
        var source = _folderPick;
        _folderPick = null;
        if (source is null)
            return;
        if (tree is null)
        {
            source.TrySetResult(Array.Empty<string>());
            return;
        }
        try
        {
            var resolver = global::Android.App.Application.Context.ContentResolver!;
            try
            {
                resolver.TakePersistableUriPermission(tree,
                    ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            }
            catch
            {
                //read-only grant, or a provider that doesn't persist: still good for this session
                try { resolver.TakePersistableUriPermission(tree, ActivityFlags.GrantReadUriPermission); } catch { }
            }
            source.TrySetResult(await Task.Run(() => ArchivesUnder(resolver, tree)));
        }
        catch (System.Exception ex)
        {
            source.TrySetException(ex);
        }
    }

    //walks the tree's folders, collecting every cbz/cbr as a document uri, in name order
    private static List<string> ArchivesUnder(ContentResolver resolver, global::Android.Net.Uri tree)
    {
        var found = new List<(string Name, string Id)>();
        var pending = new Stack<string>();
        pending.Push(global::Android.Provider.DocumentsContract.GetTreeDocumentId(tree)!);
        while (pending.Count > 0)
        {
            var parent = pending.Pop();
            var children = global::Android.Provider.DocumentsContract.BuildChildDocumentsUriUsingTree(tree, parent);
            using var cursor = resolver.Query(children, new[]
            {
                global::Android.Provider.DocumentsContract.Document.ColumnDocumentId,
                global::Android.Provider.DocumentsContract.Document.ColumnDisplayName,
                global::Android.Provider.DocumentsContract.Document.ColumnMimeType,
            }, null, null, null);
            if (cursor is null)
                continue;
            while (cursor.MoveToNext())
            {
                var docId = cursor.GetString(0);
                var name = cursor.GetString(1) ?? "";
                var mime = cursor.GetString(2);
                if (docId is null)
                    continue;
                if (mime == global::Android.Provider.DocumentsContract.Document.MimeTypeDir)
                    pending.Push(docId);
                else if (ArchiveService.IsSupportedArchive(name))
                    found.Add((name, global::Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(tree, docId)!.ToString()!));
            }
        }
        found.Sort((a, b) => NaturalCompare(a.Name, b.Name));
        return found.Select(f => f.Id).ToList();
    }

    //digit runs compare as numbers, so "issue 2" comes before "issue 10"
    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length)
                    return na.Length - nb.Length;
                var c = string.CompareOrdinal(na, nb);
                if (c != 0)
                    return c;
            }
            else
            {
                var c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (c != 0)
                    return c;
                i++;
                j++;
            }
        }
        return (a.Length - i) - (b.Length - j);
    }

    //called by the activity for every result; true if it was ours
    public static bool OnActivityResult(int requestCode, global::Android.App.Result resultCode, Intent? data)
    {
        if (requestCode == FolderRequest)
        {
            FinishFolderPick(resultCode == global::Android.App.Result.Ok ? data?.Data : null);
            return true;
        }
        if (requestCode != PickRequest)
            return false;

        var ids = new List<string>();
        if (resultCode == global::Android.App.Result.Ok && data is not null)
        {
            if (data.ClipData is { } clip)
            {
                for (var i = 0; i < clip.ItemCount; i++)
                    if (clip.GetItemAt(i)?.Uri?.ToString() is { } id)
                        ids.Add(id);
            }
            else if (data.Data?.ToString() is { } single)
            {
                ids.Add(single);
            }
        }

        //kept where the provider allows it, so Open Recent works after a restart
        foreach (var id in ids)
            ArchiveStorage.Current.Keep(id);

        _pick?.TrySetResult(ids);
        _pick = null;
        return true;
    }

    //leaving asks about unsaved edits first; unsaved edits that survive a kill are the autosave's job,
    //but a deliberate exit resolves them (saved or discarded) like closing the desktop window does
    private static async System.Threading.Tasks.Task LeaveAsync(MainView view)
    {
        if (!await view.ConfirmCloseAsync())
            return;
        view.PrepareForExit();
        view.Settings.Save();
        Activity?.FinishAndRemoveTask();
    }

    private static void Prepare(Context context)
    {
        if (_prepared)
            return;
        _prepared = true;

        AppVersion.UseAssembly(typeof(AndroidHost).Assembly);
        ArchiveStorage.Current = new AndroidArchiveStorage(context);
        //fingertip-sized targets, app-wide so menus and dialogs get them too
        Avalonia.Application.Current!.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://cbzLab.UI/"))
        {
            Source = new Uri("avares://cbzLab.UI/TouchStyles.axaml"),
        });
        AppPaths.BundledAssetsDir = UnpackBundledAssets(context);
    }

    //the shared code reads the schema, themes and logo as ordinary files, which inside an apk they
    //aren't. copied out once per version, so an update brings its new ones
    private static string UnpackBundledAssets(Context context)
    {
        var target = Path.Combine(context.FilesDir!.AbsolutePath, "bundled");
        var marker = Path.Combine(target, ".version");
        var version = AppVersion.Display;
        if (File.Exists(marker) && File.ReadAllText(marker) == version)
            return target;

        Directory.CreateDirectory(target);
        CopyAssetDir(context, "bundled", target);
        File.WriteAllText(marker, version);
        return target;
    }

    private static void CopyAssetDir(Context context, string assetPath, string targetDir)
    {
        var assets = context.Assets!;
        foreach (var name in assets.List(assetPath) ?? Array.Empty<string>())
        {
            var childAsset = assetPath + "/" + name;
            var childTarget = Path.Combine(targetDir, name);
            if ((assets.List(childAsset) ?? Array.Empty<string>()).Length > 0)
            {
                Directory.CreateDirectory(childTarget);
                CopyAssetDir(context, childAsset, childTarget);
            }
            else
            {
                using var input = assets.Open(childAsset);
                using var output = File.Create(childTarget);
                input.CopyTo(output);
            }
        }
    }
}
