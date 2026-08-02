namespace OpenKey.OAuth;

public sealed class OAuthPortInUseException : Exception
{
    public int Port { get; }

    public OAuthPortInUseException(int port, Exception inner)
        : base("Every port OpenKey can use for browser sign-in is already taken by another app on this machine.", inner)
    {
        Port = port;
    }

    public OAuthPortInUseException() : base("OAuth port already in use.") => Port = 0;
    public OAuthPortInUseException(string message) : base(message) => Port = 0;
    public OAuthPortInUseException(string message, Exception inner) : base(message, inner) => Port = 0;
}
