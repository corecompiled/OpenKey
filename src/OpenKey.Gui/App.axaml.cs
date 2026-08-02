using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OpenKey.Gui.ViewModels;
using OpenKey.Gui.Views;

namespace OpenKey.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Program.Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            // Start-up work (loading the key, restoring the conversation, fetching models) happens
            // after the window is up, so the user sees the app immediately rather than a delay
            // followed by a window.
            desktop.MainWindow.Opened += async (_, _) => await vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
