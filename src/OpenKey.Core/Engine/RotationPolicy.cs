using System.Globalization;
using System.Text.Json;
using OpenKey.Core.AppPaths;
using OpenKey.Core.Providers;

namespace OpenKey.Core.Engine;

public sealed class RotationPolicy : IRotationPolicy
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);

    private readonly IAppPaths _paths;
    private readonly Dictionary<string, ModelState> _states;

    public RotationPolicy(IAppPaths paths)
    {
        _paths = paths;
        _states = LoadFromDisk() ?? new Dictionary<string, ModelState>(StringComparer.Ordinal);
    }

    public async Task<ModelInfo> PickAsync(IReadOnlyList<ModelInfo> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
            throw new InvalidOperationException("No candidate models available.");

        var now = DateTimeOffset.UtcNow;

        foreach (var model in candidates)
        {
            var state = GetOrCreate(model.Id);
            if (state.CooldownUntil is null || state.CooldownUntil <= now)
                return model;
        }

        // All on cooldown — find soonest
        var soonestModel = candidates[0];
        var soonest = GetOrCreate(soonestModel.Id).CooldownUntil ?? DateTimeOffset.MaxValue;
        for (int i = 1; i < candidates.Count; i++)
        {
            var model = candidates[i];
            var until = GetOrCreate(model.Id).CooldownUntil ?? DateTimeOffset.MaxValue;
            if (until < soonest)
            {
                soonest = until;
                soonestModel = model;
            }
        }

        var wait = soonest - now;
        if (wait > TimeSpan.Zero)
        {
            if (wait > TimeSpan.FromSeconds(30))
            {
                throw new ChatException(
                    ChatErrorKind.TransientRateLimit,
                    $"All free models on cooldown. Try again in {wait.TotalSeconds:F0}s.");
            }
            await Task.Delay(wait, ct);
        }

        return soonestModel;
    }

    public void MarkFailure(string modelId, ChatErrorKind kind, string? retryAfterHint)
    {
        var state = GetOrCreate(modelId);
        state.FailureCount++;
        state.LastErrorKind = kind;
        state.LastUsedAt = DateTimeOffset.UtcNow;

        var baseSeconds = kind switch
        {
            ChatErrorKind.TransientRateLimit => 60,
            ChatErrorKind.TransientServer    => 30,
            ChatErrorKind.NetworkDown        => 15,
            ChatErrorKind.QuotaExhausted     => 3600,
            ChatErrorKind.MalformedResponse  => 10,
            ChatErrorKind.AuthFailure        => -1,
            _                                => 30,
        };

        if (baseSeconds < 0)
        {
            // Never retry auth failure
            state.CooldownUntil = DateTimeOffset.MaxValue;
            SaveToDisk();
            return;
        }

        // Apply Retry-After hint if larger
        if (kind == ChatErrorKind.TransientRateLimit
            && !string.IsNullOrEmpty(retryAfterHint)
            && int.TryParse(retryAfterHint, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hint))
        {
            baseSeconds = Math.Max(baseSeconds, hint);
        }

        var clampedExp = Math.Min(state.FailureCount - 1, 6);
        var multiplier = 1 << Math.Max(0, clampedExp);
        var cooldown = TimeSpan.FromSeconds((double)baseSeconds * multiplier);
        if (cooldown > MaxCooldown) cooldown = MaxCooldown;

        state.CooldownUntil = DateTimeOffset.UtcNow + cooldown;
        SaveToDisk();
    }

    public void MarkSuccess(string modelId)
    {
        var state = GetOrCreate(modelId);
        state.FailureCount = 0;
        state.CooldownUntil = null;
        state.LastErrorKind = null;
        state.LastUsedAt = DateTimeOffset.UtcNow;
        SaveToDisk();
    }

    public void Clear()
    {
        _states.Clear();
        var path = _paths.RotationStateFile;
        if (File.Exists(path)) File.Delete(path);
    }

    private ModelState GetOrCreate(string id)
    {
        if (!_states.TryGetValue(id, out var s))
        {
            s = new ModelState { ModelId = id };
            _states[id] = s;
        }
        return s;
    }

    private Dictionary<string, ModelState>? LoadFromDisk()
    {
        var path = _paths.RotationStateFile;
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var env = JsonSerializer.Deserialize<StateEnvelope>(stream, JsonOpts);
            return env?.States is null
                ? null
                : new Dictionary<string, ModelState>(env.States, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private void SaveToDisk()
    {
        _paths.EnsureRoot();
        var path = _paths.RotationStateFile;
        var tmp = path + ".tmp";
        var env = new StateEnvelope(_states);
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, env, JsonOpts);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public sealed class ModelState
    {
        public string ModelId { get; set; } = "";
        public int FailureCount { get; set; }
        public DateTimeOffset? CooldownUntil { get; set; }
        public ChatErrorKind? LastErrorKind { get; set; }
        public DateTimeOffset LastUsedAt { get; set; }
    }

    private sealed record StateEnvelope(IReadOnlyDictionary<string, ModelState> States);
}
