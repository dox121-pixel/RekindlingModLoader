using System;
using System.Text.RegularExpressions;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Lenient version parsing. Mod authors write versions like <c>1.2</c>, <c>1.2.0</c>, or
    /// <c>1.2.0-beta3</c>; all of those should work rather than throwing.
    /// </summary>
    public static class ModVersion
    {
        private static readonly Regex Numeric = new Regex(@"^\s*v?(\d+(?:\.\d+){0,3})", RegexOptions.Compiled);

        /// <summary>
        /// Parses a version string, ignoring any pre-release suffix. Returns 0.0.0 for null,
        /// empty or unrecognised input instead of throwing, so a typo in a manifest degrades
        /// to "oldest possible version" rather than killing the load.
        /// </summary>
        public static Version Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new Version(0, 0, 0);

            Match match = Numeric.Match(raw);
            if (!match.Success)
                return new Version(0, 0, 0);

            // Version requires at least major.minor.
            string text = match.Groups[1].Value;
            if (!text.Contains("."))
                text += ".0";

            return Version.TryParse(text, out Version parsed)
                ? parsed
                : new Version(0, 0, 0);
        }

        /// <summary>True when <paramref name="actual"/> is at least <paramref name="required"/>.</summary>
        public static bool Satisfies(string actual, string required)
            => Parse(actual) >= Parse(required);
    }
}
