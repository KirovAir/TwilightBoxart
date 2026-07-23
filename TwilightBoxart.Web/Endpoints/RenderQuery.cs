using System.Globalization;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// Parses render parameters out of a query string.
/// </summary>
/// <remarks>
/// Short names because these travel in a URL that a DS client stores in a config file and that a
/// browser caches per variant: <c>w</c>, <c>h</c>, <c>ar</c> (keep aspect ratio), <c>b</c> (border
/// style), <c>bt</c> (border thickness), <c>bc</c> (border colour).
///
/// Everything unparseable falls back to the default rather than 400ing. A box art request with a
/// typo'd border colour should still return box art. What is NOT tolerant is the range: the result
/// goes through <see cref="RenderOptions.Normalized"/> before it can size a buffer.
/// </remarks>
public static class RenderQuery
{
    /// <summary>
    /// Applies the query on top of <see cref="RenderOptions.Default"/>.
    /// </summary>
    /// <param name="query">The request's query string.</param>
    /// <param name="defaults">
    /// What to use for anything the caller did not specify. Always
    /// <see cref="RenderOptions.Default"/> in the web host; a parameter rather than a hardcoded
    /// reference only so the parsing rules can be tested against a known-different baseline.
    /// </param>
    public static RenderOptions From(IQueryCollection query, RenderOptions defaults)
    {
        return new RenderOptions
        {
            Width = Int(query, "w") ?? defaults.Width,
            Height = Int(query, "h") ?? defaults.Height,
            KeepAspectRatio = Bool(query, "ar") ?? defaults.KeepAspectRatio,
            BorderStyle = Border(query, "b") ?? defaults.BorderStyle,
            BorderThickness = Int(query, "bt") ?? defaults.BorderThickness,
            BorderColor = RenderOptions.ParseColor(query["bc"]) ?? defaults.BorderColor,
            // Not settable from the query: for DS-displayable sizes it is TWiLightMenu++'s hard
            // constraint, not a preference, and a client raising it produces art the DS silently
            // refuses to display. Oversize renders get their wider budget from Normalized() below.
            MaxPngBytes = defaults.MaxPngBytes,
        }.Normalized();
    }

    private static int? Int(IQueryCollection query, string name) =>
        int.TryParse(query[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool? Bool(IQueryCollection query, string name)
    {
        var raw = query[name].ToString();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        // "1"/"0" as well as "true"/"false": the DS client builds its query by hand in C.
        return raw switch
        {
            "1" => true,
            "0" => false,
            _ => bool.TryParse(raw, out var parsed) ? parsed : null,
        };
    }

    /// <summary>Accepts the enum name or its numeric value; the numeric form is what v0.7 clients sent.</summary>
    private static BoxartBorderStyle? Border(IQueryCollection query, string name)
    {
        var raw = query[name].ToString();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
            && Enum.IsDefined(typeof(BoxartBorderStyle), numeric))
        {
            return (BoxartBorderStyle)numeric;
        }

        return Enum.TryParse<BoxartBorderStyle>(raw, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }
}
