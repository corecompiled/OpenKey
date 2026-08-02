using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OpenKey;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Engine;
using OpenKey.Core.Providers;
using OpenKey.Core.Storage;
using OpenKey.Providers.OpenRouter;

// LLM replies contain non-ASCII (em-dash, smart quotes, emoji). Without UTF-8 the
// Windows console renders them as "?" / "??". Guarded: stdout may be redirected.
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* no console / redirected */ }

var services = new ServiceCollection();

services.AddSingleton<IAppPaths, WindowsAppPaths>();
services.AddSingleton<IKeyStore, DpapiKeyStore>();
services.AddSingleton<ISessionStore, JsonSessionStore>();
services.AddSingleton<IRotationPolicy, RotationPolicy>();

// Infinite on purpose. HttpClient.Timeout bounds the *entire* response including reading the body,
// even with ResponseHeadersRead, so any finite value here silently aborts long-but-healthy streamed
// replies and gets misread as a network fault. The provider applies per-read deadlines instead.
services.AddSingleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

services.AddSingleton<IChatProvider>(sp =>
{
    var http = sp.GetRequiredService<HttpClient>();
    var keys = sp.GetRequiredService<IKeyStore>();
    return new OpenRouterProvider(http, keys.Load);
});

services.AddSingleton<IModelCatalog, JsonModelCatalog>();
services.AddSingleton<ChatEngine>();
services.AddSingleton<ConsoleHost>();

await using var sp = services.BuildServiceProvider();

try
{
    await sp.GetRequiredService<ConsoleHost>().RunAsync();
}
catch (Exception ex)
{
    // Without this the window closes on the same frame as the stack trace, so a double-clicked
    // OpenKey.exe just vanishes and the user has nothing to report.
    ConsoleHost.ReportFatal(ex);
    return 1;
}

return 0;
