using OpenKey.Windows.OAuth;
using Xunit;

namespace OpenKey.Tests;

public sealed class PkceCodesTests
{
    [Fact]
    public void ChallengeForMatchesRfc7636AppendixB()
    {
        // RFC 7636 Appendix B test vector
        const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string Expected = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var actual = PkceCodes.ChallengeFor(Verifier);

        Assert.Equal(Expected, actual);
    }

    [Fact]
    public void GenerateProducesValidLengthVerifierAndDeterministicChallenge()
    {
        var pkce = PkceCodes.Generate();

        // 32 random bytes → base64url(no-pad) = 43 chars
        Assert.Equal(43, pkce.CodeVerifier.Length);

        // The verifier must only contain URL-safe chars
        foreach (var ch in pkce.CodeVerifier)
            Assert.True(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '~',
                $"invalid char in verifier: {ch}");

        // Re-deriving the challenge from the verifier must match the recorded one
        Assert.Equal(PkceCodes.ChallengeFor(pkce.CodeVerifier), pkce.CodeChallenge);
    }

    [Fact]
    public void GenerateProducesDifferentVerifiersAcrossCalls()
    {
        var a = PkceCodes.Generate();
        var b = PkceCodes.Generate();
        Assert.NotEqual(a.CodeVerifier, b.CodeVerifier);
    }
}
