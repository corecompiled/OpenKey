using System.Collections.ObjectModel;
using OpenKey.Core.Providers;

namespace OpenKey.Gui.ViewModels;

public enum Speaker
{
    You,
    Assistant,
}

/// <summary>
/// One turn in the conversation.
/// <para>
/// Text and rendered blocks are kept side by side on purpose: the raw text is what streams in and
/// what <c>/copy</c> and export need, while the blocks are what the view draws. Re-parsing markdown
/// on every delta would be wasteful, so blocks are rebuilt only when a block boundary is reached —
/// the same rule the console's streaming renderer uses.
/// </para>
/// </summary>
public sealed class MessageViewModel : ObservableObject
{
    private string _text = string.Empty;
    private bool _isStreaming;
    private string? _modelId;
    private TimeSpan _elapsed;
    private string _userName;
    private bool _isAwaitingReply;

    public MessageViewModel(Speaker speaker, string userName, string text = "")
    {
        Speaker = speaker;
        _userName = userName;
        _text = text;
        if (text.Length > 0) RebuildBlocks();
    }

    public Speaker Speaker { get; }

    public bool IsFromUser => Speaker == Speaker.You;

    /// <summary>
    /// True only on the trailing user turn when nothing answered it, which is exactly the state a
    /// resend applies to. A failed or stopped turn deletes its empty assistant reply, so "the last
    /// message is still yours" is a reliable signal rather than a guess.
    /// </summary>
    public bool IsAwaitingReply
    {
        get => _isAwaitingReply;
        set => Set(ref _isAwaitingReply, value);
    }

    public string Header => Speaker == Speaker.You ? _userName : "OpenKey AI";

    /// <summary>
    /// Renaming yourself relabels the whole transcript, not just messages sent afterwards — a
    /// transcript addressing you by two different names would look like two different people.
    /// </summary>
    public void SetUserName(string name)
    {
        if (_userName == name) return;
        _userName = name;
        if (Speaker == Speaker.You) Raise(nameof(Header));
    }

    /// <summary>
    /// Waiting on the model with nothing to show yet — the gap between sending and the first token,
    /// which on a busy free model can run to tens of seconds. Distinct from <see cref="IsTyping"/>
    /// so the two states can look different: an empty caret blinking at nothing reads as a stall,
    /// not as progress.
    /// </summary>
    public bool IsThinking => IsStreaming && !HasText;

    /// <summary>Tokens are arriving; the caret trails the text.</summary>
    public bool IsTyping => IsStreaming && HasText;

    public ObservableCollection<MarkdownBlock> Blocks { get; } = new();

    public string Text
    {
        get => _text;
        private set
        {
            if (!Set(ref _text, value)) return;
            Raise(nameof(HasText));
            Raise(nameof(IsThinking));
            Raise(nameof(IsTyping));
        }
    }

    public bool HasText => _text.Length > 0;

    /// <summary>True while deltas are still arriving; drives the caret in the view.</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (!Set(ref _isStreaming, value)) return;
            Raise(nameof(IsThinking));
            Raise(nameof(IsTyping));
        }
    }

    public string? ModelId
    {
        get => _modelId;
        set { if (Set(ref _modelId, value)) Raise(nameof(Subtitle)); }
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        set { if (Set(ref _elapsed, value)) Raise(nameof(Subtitle)); }
    }

    public string Subtitle =>
        _modelId is null
            ? string.Empty
            : $"{_modelId}  ·  {(_elapsed.TotalSeconds < 60 ? $"{_elapsed.TotalSeconds:0.0}s" : $"{(int)_elapsed.TotalMinutes}m {_elapsed.Seconds}s")}";

    public void Append(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;
        Text += delta;
        RebuildBlocks();
    }

    /// <summary>Discards everything from an abandoned attempt — see <see cref="ChatChunk.IsAttemptRestart"/>.</summary>
    public void Reset()
    {
        Text = string.Empty;
        Blocks.Clear();
    }

    public void Complete()
    {
        IsStreaming = false;
        RebuildBlocks();
    }

    private void RebuildBlocks()
    {
        var parsed = MarkdownBlock.Parse(_text);

        // Replace in place rather than clearing: clearing makes the list view flash and lose scroll
        // position on every delta, which is very visible while a reply streams.
        for (var i = 0; i < parsed.Count; i++)
        {
            if (i < Blocks.Count)
            {
                if (!Blocks[i].Equals(parsed[i])) Blocks[i] = parsed[i];
            }
            else
            {
                Blocks.Add(parsed[i]);
            }
        }

        while (Blocks.Count > parsed.Count) Blocks.RemoveAt(Blocks.Count - 1);
    }
}
