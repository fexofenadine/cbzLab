using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Avalonia.Android;

//ComicVine lookups are the only thing that goes online; saving and opening are all local documents
[assembly: UsesPermission(global::Android.Manifest.Permission.Internet)]

namespace cbzLab.Android;

[Application(Label = "cbzLab", Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon")]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }
}

//handles rotation itself, so Android doesn't destroy and recreate the activity, and with it the
//open books and their unsaved edits
[Activity(Label = "cbzLab", Theme = "@style/Theme.AppCompat.NoActionBar", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTask, WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout
                           | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode | ConfigChanges.KeyboardHidden)]
//"open with" for comic archives from a file manager or a download
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeTypes = new[]
    {
        "application/vnd.comicbook+zip", "application/vnd.comicbook-rar", "application/x-cbz", "application/x-cbr",
        "application/x-cbz-compressed", "application/x-cbr-compressed",
    })]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "content", DataMimeType = "*/*", DataPathPattern = ".*\\.cbz")]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "content", DataMimeType = "*/*", DataPathPattern = ".*\\.cbr")]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidHost.Activity = this;
        base.OnCreate(savedInstanceState);
    }

    //a book opened from another app while this one is already running
    protected override async void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        var books = AndroidHost.PendingBooks(intent);
        if (books.Count > 0 && AndroidHost.View is { } view)
            await view.OpenAsync(books);
    }

    //going to the background is where the system may kill the app without a word, so unsaved edits
    //are written to their recovery drafts, and the settings saved, before it can
    protected override void OnPause()
    {
        AndroidHost.View?.FlushAutosave();
        AndroidHost.View?.Settings.Save();
        base.OnPause();
    }
}
