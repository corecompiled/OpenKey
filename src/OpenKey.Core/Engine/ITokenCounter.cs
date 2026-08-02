using OpenKey.Core.Providers;

namespace OpenKey.Core.Engine;

/// <summary>
/// Counts tokens for context trimming.
/// <para>
/// An interface rather than a direct dependency because <c>OpenKey.Core</c> deliberately has no
/// package references — that absence is what stops a host or provider concern leaking into the
/// shared layer. A real tokenizer needs a vocabulary package, so it is supplied from outside.
/// </para>
/// </summary>
public interface ITokenCounter
{
    int Count(string text);

    /// <summary>
    /// Tokens for a whole conversation, including the small per-message overhead every chat API
    /// adds for role framing.
    /// </summary>
    int Count(IEnumerable<ChatMessage> messages)
    {
        var total = 0;
        foreach (var m in messages) total += Count(m.Role) + Count(m.Content) + 4;
        return total;
    }
}

/// <summary>
/// Fallback used when no real tokenizer is supplied: roughly four characters per token.
/// <para>
/// Crude, and deliberately so — it is only ever used to decide how much history to drop, where
/// over-estimating costs a little context and under-estimating costs a rejected request. It errs
/// high for that reason.
/// </para>
/// </summary>
public sealed class HeuristicTokenCounter : ITokenCounter
{
    public int Count(string text) => string.IsNullOrEmpty(text) ? 0 : (text.Length / 4) + 1;
}
