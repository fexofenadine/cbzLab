using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using cbzLab.Services;

namespace cbzLab.Dialogs;

public partial class AboutDialog : DialogBase
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = AppVersion.Display;
        LoadHeroImage();
    }

    private void LoadHeroImage()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
            using var stream = File.OpenRead(path);
            HeroImage.Source = Bitmap.DecodeToWidth(stream, 640);
        }
        catch
        {
            //no shipped logo asset - hero area just stays blank, not fatal
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Visual owner) => DialogHost.ShowAsync(owner, new AboutDialog());
}
