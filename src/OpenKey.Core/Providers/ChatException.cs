namespace OpenKey.Core.Providers;

public sealed class ChatException : Exception
{
    public ChatErrorKind Kind { get; }
    public string? RetryAfterHint { get; }

    public ChatException(ChatErrorKind kind, string message, string? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        RetryAfterHint = retryAfter;
    }

    public ChatException() : this(ChatErrorKind.MalformedResponse, "Chat failed.") { }
    public ChatException(string message) : this(ChatErrorKind.MalformedResponse, message) { }
    public ChatException(string message, Exception inner) : this(ChatErrorKind.MalformedResponse, message, null, inner) { }
}
