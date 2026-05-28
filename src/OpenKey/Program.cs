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

services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

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
await sp.GetRequiredService<ConsoleHost>().RunAsync();
