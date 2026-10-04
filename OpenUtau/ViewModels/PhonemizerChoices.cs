using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.DiffSinger;
using Serilog;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// Shared rules for turning the installed phonemizers into the choices a user picks from.
    /// </summary>
    public static class PhonemizerChoices {
        /// <summary>The DiffSinger series: the ones a voicebank's language list selects from.</summary>
        public static bool IsDiffSinger(PhonemizerFactory factory) =>
            factory != null && typeof(DiffSingerBasePhonemizer).IsAssignableFrom(factory.type);

        /// <summary>
        /// The phonemizers to offer when the voicebank is not DiffSinger. The DiffSinger series is
        /// left out because it is only meaningful with a DiffSinger voicebank.
        /// </summary>
        public static IEnumerable<PhonemizerFactory> NonDiffSinger() =>
            PhonemizerFactory.GetAll().Where(factory => !IsDiffSinger(factory));

        /// <summary>Group header for a phonemizer's language, as the track header's menu shows it.</summary>
        public static string GroupHeader(string key) {
            if (string.IsNullOrEmpty(key)) {
                return "General";
            }
            if (ThemeManager.TryGetString($"languages.{key.ToLowerInvariant()}", out var value)) {
                return $"{key}: {value}";
            }
            return key;
        }

        /// <summary>
        /// The localized name of a language code the way a DiffSinger voicebank spells it, which is
        /// not always the way the string table spells it: the voicebanks say "yue", the strings say
        /// "zh-yue".
        /// </summary>
        public static string LanguageName(string code) {
            if (string.IsNullOrEmpty(code)) {
                return string.Empty;
            }
            var key = string.Equals(code, "yue", StringComparison.OrdinalIgnoreCase) ? "zh-yue" : code.ToLowerInvariant();
            return ThemeManager.TryGetString($"languages.{key}", out var value) ? value : code;
        }

        /// <summary>
        /// The DiffSinger phonemizers by the language code they declare. Built on demand rather than
        /// cached, because the language a phonemizer claims is only knowable from an instance and
        /// plugin loading happens after this type could first be touched.
        /// </summary>
        public static Dictionary<string, PhonemizerFactory> DiffSingerByLanguage() {
            var result = new Dictionary<string, PhonemizerFactory>();
            foreach (var factory in PhonemizerFactory.GetAll().Where(IsDiffSinger)) {
                string? code;
                try {
                    code = (factory.Create() as DiffSingerBasePhonemizer)?.GetLangCode();
                } catch (Exception e) {
                    Log.Error(e, $"Failed to inspect phonemizer {factory.type.FullName}");
                    continue;
                }
                if (string.IsNullOrEmpty(code)) {
                    continue;
                }
                if (!result.TryGetValue(code, out var current) || Rank(factory, code).CompareTo(Rank(current, code)) < 0) {
                    result[code] = factory;
                }
            }
            return result;
        }

        /// <summary>The DiffSinger phonemizer for a language code, or null when none matches.</summary>
        public static PhonemizerFactory? ForLanguage(string code) =>
            DiffSingerByLanguage().TryGetValue(code ?? string.Empty, out var factory) ? factory : null;

        // Several phonemizers can claim one language (English and English+, two Korean ones). The
        // tag that is exactly the language code is the canonical one; otherwise the shortest name
        // wins, which picks "DiffSinger Korean Phonemizer" over "DiffSinger Korean G2P Phonemizer".
        // The type name breaks any remaining tie so the result does not depend on load order.
        static (int, int, string) Rank(PhonemizerFactory factory, string code) => (
            factory.tag == "DIFFS " + code.ToUpperInvariant() ? 0 : 1,
            factory.name?.Length ?? int.MaxValue,
            factory.type.FullName ?? string.Empty);
    }
}
