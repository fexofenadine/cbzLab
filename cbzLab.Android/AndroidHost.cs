using System;
using System.Collections.Generic;
using System.IO;
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
        var view = new MainView { TouchMode = true, ExternalOpenPicker = PickFromAnyAppAsync };
        View = view;
        view.QuitRequested += async (_, _) => await LeaveAsync(view);
        view.Loaded += async (_, _) =>
        {
            if (_shown)
                return;
            _shown = true;

            //the keyboard: edge-to-edge windows ignore adjustResize, so the view is lifted by the
            //height the keyboard covers, which keeps the field being typed into in sight
            if (TopLevel.GetTopLevel(view)?.InputPane is { } pane)
                pane.StateChanged += (_, e) =>
                    view.Margin = new Avalonia.Thickness(0, 0, 0,
                        e.NewState == Avalonia.Controls.Platform.InputPaneState.Open ? pane.OccludedRect.Height : 0);

            //the back button: an open dialog
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

    //called by the activity for every result; true if it was ours
    public static bool OnActivityResult(int requestCode, global::Android.App.Result resultCode, Intent? data)
    {
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
