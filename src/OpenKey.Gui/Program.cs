using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using OpenKey.Core.Updates;
using OpenKey.Gui.ViewModels;
using OpenKey.Providers.OpenRouter;
using OpenKey.Windows;

namespace OpenKey.Gui;

internal static class Program
{
    /// <summary>
    /// Composition is identical to the console host's, and deliberately so: the GUI is a second
    /// host over the same engine, not a second implementation. Everything below the view models
    /// is shared, which is what makes this project small.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = default!;

    [STAThread]
    public static int Main(string[] args)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAppPaths, WindowsAppPaths>();
        services.AddSingleton<IKeyStore, DpapiKeyStore>();
        services.AddSingleton<IChatStore, JsonChatStore>();
        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();
services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();
        services.AddSingleton<IRotationPolicy, RotationPolicy>();
        services.AddSingleton<ITokenCounter, TiktokenCounter>();

        // Infinite on purpose — see the console host's Program.cs. HttpClient.Timeout bounds the
        // whole response including the body, which silently aborts long streamed replies; the
        // provider applies per-read deadlines instead.
        services.AddSingleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

        services.AddSingleton<IChatProvider>(sp =>
            new OpenRouterProvider(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<IKeyStore>().Load));

        services.AddSingleton<IModelCatalog, JsonModelCatalog>();
        services.AddSingleton<ChatEngine>();
        services.AddSingleton<MainWindowViewModel>();

        var provider = services.BuildServiceProvider();
        Services = provider;

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            provider.Dispose();
        }
    }

    // Referenced by name by the Avalonia designer tooling; keep the signature.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
