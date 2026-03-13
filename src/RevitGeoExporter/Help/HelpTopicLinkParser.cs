using System;

namespace RevitGeoExporter.Help;

internal static class HelpTopicLinkParser
{
    public static bool TryParse(Uri? uri, out HelpTopic topic)
    {
        topic = default;
        if (uri == null || !string.Equals(uri.Scheme, "help", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candidate = ExtractCandidate(uri);
        return Enum.TryParse(candidate, ignoreCase: true, out topic);
    }

    private static string ExtractCandidate(Uri uri)
    {
        string candidate = (uri.AbsolutePath ?? string.Empty).Trim('/');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = (uri.Host ?? string.Empty).Trim('/');
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            string original = uri.OriginalString ?? string.Empty;
            if (original.StartsWith("help:", StringComparison.OrdinalIgnoreCase))
            {
                candidate = original.Substring("help:".Length).Trim('/');
            }
        }

        int separatorIndex = candidate.IndexOfAny(new[] { '?', '#' });
        if (separatorIndex >= 0)
        {
            candidate = candidate.Substring(0, separatorIndex);
        }

        return Uri.UnescapeDataString(candidate ?? string.Empty);
    }
}
