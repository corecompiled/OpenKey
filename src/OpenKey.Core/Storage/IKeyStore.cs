namespace OpenKey.Core.Storage;

public interface IKeyStore
{
    bool HasKey();
    string? Load();
    void Save(string apiKey);
    void Clear();
}
