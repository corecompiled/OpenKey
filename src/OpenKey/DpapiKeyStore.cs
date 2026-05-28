using System.Security.Cryptography;
using System.Text;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Storage;

namespace OpenKey;

public sealed class DpapiKeyStore : IKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OpenKey/v1/key-entropy");

    private readonly IAppPaths _paths;

    public DpapiKeyStore(IAppPaths paths) => _paths = paths;

    public bool HasKey() => File.Exists(_paths.KeyFile);

    public string? Load()
    {
        var path = _paths.KeyFile;
        if (!File.Exists(path)) return null;

        try
        {
            var cipher = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            return null;
        }
    }

    public void Save(string apiKey)
    {
        _paths.EnsureRoot();
        var plain = Encoding.UTF8.GetBytes(apiKey);
        var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_paths.KeyFile, cipher);
    }

    public void Clear()
    {
        var path = _paths.KeyFile;
        if (File.Exists(path)) File.Delete(path);
    }
}
