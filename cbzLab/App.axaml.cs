using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace cbzLab;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();

            //queued rather than opened immediately so dialogs have a valid owner window
            var cliPaths = (desktop.Args ?? Array.Empty<string>())
                .Where(File.Exists)
                .ToList();
            if (cliPaths.Count > 0)
                window.QueueStartupPaths(cliPaths);

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
