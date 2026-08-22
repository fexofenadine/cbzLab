using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace cbzLab.Avalonia.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = MainWindow.DisplayVersion;
        KeyDown += OnKeyDown;
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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape) Close();
    }

    private void OnOk(object? sender, PointerPressedEventArgs e) => Close();

    public static Task ShowAsync(Window owner) => new AboutDialog().ShowDialog(owner);
}
