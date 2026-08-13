using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PlaylistBridge.Core;

public static partial class TrackNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var withoutMarks = new string(decomposed.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Spaces().Replace(NonAlphaNumeric().Replace(withoutMarks.ToLowerInvariant(), " "), " ").Trim();
    }

    public static bool HasVersionMarker(string? value, string marker) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(marker);

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumeric();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
