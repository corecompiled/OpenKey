using System.Security.Cryptography;
using System.Text;

namespace OpenKey.Windows.OAuth;

public sealed record PkceCodes(string CodeVerifier, string CodeChallenge)
{
    public const string ChallengeMethod = "S256";

    public static PkceCodes Generate()
    {
        // RFC 7636 §4.1: 43–128 chars, [A-Za-z0-9-._~]
        // Base64url(no-pad) of 32 random bytes = 43 chars, satisfies the constraint.
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var verifier = Base64UrlEncode(bytes);
        var challenge = ChallengeFor(verifier);
        return new PkceCodes(verifier, challenge);
    }

    public static string ChallengeFor(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        var s = Convert.ToBase64String(bytes);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
