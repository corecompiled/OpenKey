namespace OpenKey.Core.Providers;

/// <summary>
/// Normative error taxonomy — see docs/01-architecture.md § "Error taxonomy".
/// Every provider maps its transport failures onto these, and rotation policy is driven
/// entirely by them. Adding a member is a contract change that propagates to every impl.
/// </summary>
public enum ChatErrorKind
{
    /// <summary>Rate limited. Retryable on a different model.</summary>
    TransientRateLimit,

    /// <summary>Upstream 5xx or timeout. Retryable on a different model.</summary>
    TransientServer,

    /// <summary>Key is missing, invalid, or revoked. Fatal — no model will accept it.</summary>
    AuthFailure,

    /// <summary>Credit or quota exhausted. Fatal for this key.</summary>
    QuotaExhausted,

    /// <summary>
    /// No route to the provider. Fatal for this turn and explicitly NOT rotated on: when the
    /// network is down every model fails identically, so rotating would burn the retry budget
    /// and leave all models cooling down for a fault unrelated to any of them.
    /// </summary>
    NetworkDown,

    /// <summary>Response could not be parsed. Retryable — the next model may answer cleanly.</summary>
    MalformedResponse,

    /// <summary>
    /// The request itself is unacceptable (context overflow, unknown model id, bad parameters).
    /// Fatal: retrying an identical request on another model cannot succeed, and doing so would
    /// cool down every model in turn.
    /// </summary>
    InvalidRequest,
}
