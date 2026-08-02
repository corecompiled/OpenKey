using Microsoft.ML.Tokenizers;
using OpenKey.Core.Engine;

namespace OpenKey.Windows;

/// <summary>
/// Real token counting, backed by the cl100k_base vocabulary.
/// <para>
/// The vocabulary is embedded via <c>Microsoft.ML.Tokenizers.Data.Cl100kBase</c> rather than
/// downloaded on demand. OpenKey must work on first run behind a captive portal and makes no
/// network call other than to OpenRouter, so a tokenizer that fetches its own vocabulary would
/// break both properties.
/// </para>
/// <para>
/// cl100k is a GPT-family encoding, and OpenRouter's free tier is mostly Llama, Qwen, DeepSeek and
/// Mistral, whose tokenizers differ. It is still substantially closer than four-characters-per-token
/// for ordinary prose, and this only drives how much history to drop — so being close and cheap
/// beats being exact and slow.
/// </para>
/// </summary>
public sealed class TiktokenCounter : ITokenCounter
{
    private readonly TiktokenTokenizer? _tokenizer;
    private readonly HeuristicTokenCounter _fallback = new();

    public TiktokenCounter()
    {
        try
        {
            _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        }
        catch (Exception)
        {
            // Never let tokenizer setup stop the app starting; the heuristic is a fine substitute.
            _tokenizer = null;
        }
    }

    public int Count(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (_tokenizer is null) return _fallback.Count(text);

        try
        {
            return _tokenizer.CountTokens(text);
        }
        catch (Exception)
        {
            return _fallback.Count(text);
        }
    }
}
