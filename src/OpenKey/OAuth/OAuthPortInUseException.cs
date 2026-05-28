namespace OpenKey.OAuth;

public sealed class OAuthPortInUseException : Exception
{
    public int Port { get; }

    public OAuthPortInUseException(int port, Exception inner)
        : base($"Port {port} is in use by another app on this machine, so OpenKey can't receive the OAuth callback. Close the other app or use the paste flow.", inner)
    {
        Port = port;
    }

    public OAuthPortInUseException() : base("OAuth port already in use.") => Port = 0;
    public OAuthPortInUseException(string message) : base(message) => Port = 0;
    public OAuthPortInUseException(string message, Exception inner) : base(message, inner) => Port = 0;
}
