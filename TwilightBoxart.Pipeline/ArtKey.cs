namespace TwilightBoxart.Pipeline;

/// <summary>
/// Validation for the art key - the title-space identifier in <c>/v2/art/{platform}/{key}.png</c>.
/// </summary>
/// <remarks>
/// The derivation itself lives in Core (<c>IdentificationLadder.DeriveKey</c>) and deliberately does
/// not exist here too - two copies of a hash that must agree is a bug waiting for its first rename.
/// What lives here is validation, plus the query-string parsing that shares its concerns.
///
/// The key goes straight into a filesystem path, so the allowlist here is the boundary that keeps a
/// request from addressing anything outside the cache. It is an allowlist rather than a sanitiser on
/// purpose: the 2020 backend built cache paths from <c>Uri.LocalPath</c>, which unescapes <c>%2f</c>
/// AFTER dot-segment normalisation and before Path.Combine - a traversal primitive that no amount of
/// stripping "../" would have caught. Real keys are a 4-character title id or a
/// 16-hex name digest; nothing legitimate needs a dot, a slash or a percent sign.
/// </remarks>
public static class ArtKey
{
    public static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > PipelineLimits.MaxKeyLength)
        {
            return false;
        }

        foreach (var c in key)
        {
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Folds an already-validated key onto the exact casing <c>DeriveKey</c> emits: digest keys are
    /// lowercase hex, everything else (title ids included) is uppercase. The database collates keys
    /// NOCASE, so without this a case-variant URL addresses the same row but a different cache path.
    /// </summary>
    public static string Normalize(string key)
    {
        if (key.Length != 16)
        {
            return key.ToUpperInvariant();
        }

        foreach (var c in key)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            {
                return key.ToUpperInvariant();
            }
        }

        return key.ToLowerInvariant();
    }

    /// <summary>
    /// True when the key is the 16-hex digest form rather than a title id. Digest keys carry no
    /// information on their own, so the art endpoint has to resolve them through the record store.
    /// </summary>
    public static bool IsNameDigest(string key)
    {
        if (key.Length != 16)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

}
