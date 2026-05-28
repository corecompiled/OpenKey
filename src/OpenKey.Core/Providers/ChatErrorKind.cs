namespace OpenKey.Core.Providers;

public enum ChatErrorKind
{
    TransientRateLimit,
    TransientServer,
    AuthFailure,
    QuotaExhausted,
    NetworkDown,
    MalformedResponse,
}
