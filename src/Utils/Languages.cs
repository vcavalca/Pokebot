using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Pokebot.Utils
{
    /// <summary>
    /// The languages Pokebot can be displayed in: English (Messages.resx) plus every translation
    /// Messages.&lt;code&gt;.resx, which Pokebot.csproj embeds in the DLL as "Pokebot.Messages.&lt;code&gt;.resources"
    /// (read by <see cref="EmbeddedCultureResourceManager"/>). Adding a translation file is enough for it
    /// to be listed in the Settings tab.
    /// </summary>
    public static class Languages
    {
        public const string DefaultCode = "en";

        private const string ResourcePrefix = "Pokebot.Messages.";
        private const string ResourceSuffix = ".resources";

        /// <summary>English first, then the embedded translations sorted by their own name.</summary>
        public static IReadOnlyList<LanguageOption> GetAvailable()
        {
            var translations = new List<LanguageOption>();
            foreach (var resourceName in typeof(Messages).Assembly.GetManifestResourceNames())
            {
                var code = GetCultureCode(resourceName);
                if (code == null)
                {
                    continue;
                }

                try
                {
                    var culture = CultureInfo.GetCultureInfo(code);
                    translations.Add(new LanguageOption(culture.Name, GetDisplayName(culture)));
                }
                catch (CultureNotFoundException)
                {
                    Log.Warn($"Translation '{resourceName}' ignored: '{code}' is not a known language code.");
                }
            }

            var languages = new List<LanguageOption>
            {
                new LanguageOption(DefaultCode, GetDisplayName(CultureInfo.GetCultureInfo(DefaultCode)))
            };
            languages.AddRange(translations.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase));
            return languages;
        }

        private static string? GetCultureCode(string resourceName)
        {
            // "Pokebot.Messages.resources" (English) has no code between the prefix and the suffix
            if (resourceName.Length <= ResourcePrefix.Length + ResourceSuffix.Length
                || !resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resourceName.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                return null;
            }

            return resourceName.Substring(ResourcePrefix.Length, resourceName.Length - ResourcePrefix.Length - ResourceSuffix.Length);
        }

        private static string GetDisplayName(CultureInfo culture)
        {
            // Each language in its own words, so everyone finds theirs ("português (Brasil)" -> "Português (Brasil)")
            var name = culture.NativeName;
            if (name.Length == 0)
            {
                return culture.Name;
            }

            return char.ToUpper(name[0], culture) + name.Substring(1);
        }
    }

    public sealed class LanguageOption
    {
        public string Code { get; }
        public string DisplayName { get; }

        public LanguageOption(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
