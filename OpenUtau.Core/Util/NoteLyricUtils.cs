namespace OpenUtau.Core.Util {

    /// <summary>
    /// Lyric spelling helpers that follow the two "editing" preferences:
    /// <see cref="Preferences.SerializablePreferences.UseDashExtension"/> makes "-"
    /// the vowel-extension marker instead of "+~", and
    /// <see cref="Preferences.SerializablePreferences.BreathAsBr"/> lets the user
    /// write "br" where a DiffSinger model expects "AP".
    ///
    /// Both preferences default to off, and every method here is the identity (or
    /// matches the old inline check exactly) while they are off. Storage keeps the
    /// user's preferred spelling; the phonemizer and render pipeline boundaries
    /// (UNote.ToPhonemizerNote, PhraseSource.NoteSource) canonicalize back to "+~"
    /// and "-" so no phonemizer needs to know about these preferences.
    /// </summary>
    public static class NoteLyricUtils {

        /// <summary>
        /// Whether a stored lyric is a vowel extension. Equivalent to the old
        /// <c>lyric.StartsWith("+")</c> while the preference is off.
        /// </summary>
        public static bool IsExtensionLyric(string lyric)
            => !string.IsNullOrEmpty(lyric)
               && (lyric.StartsWith("+") || (Preferences.Default.UseDashExtension && lyric == "-"));

        /// <summary>Extension lyric as the phonemizer and render pipeline expect it.</summary>
        public static string CanonicalExtension(string lyric)
            => Preferences.Default.UseDashExtension && lyric == "-" ? "+~" : lyric;

        /// <summary>
        /// Tail note lyric as the phonemizer expects it. Only "&amp;" is rewritten,
        /// so a rest/placeholder "-" head note keeps its meaning.
        /// </summary>
        public static string CanonicalTail(string lyric) => lyric == "&" ? "-" : lyric;

        /// <summary>Lyric to write for a newly inserted slur.</summary>
        public static string ExtensionLyricForWrite()
            => Preferences.Default.UseDashExtension ? "-" : "+~";

        /// <summary>
        /// Resolves the tail note marker. The tail marker is only moved off "-"
        /// while the "-" extension preference is on, since a tail note abuts the
        /// previous note and would otherwise be grouped as an extension.
        /// </summary>
        public static string TailLyricForWrite(string baseLyric)
            => baseLyric == "-" && Preferences.Default.UseDashExtension ? "&" : baseLyric;

        /// <summary>Import: keep "-" as written when the preference is on, else convert.</summary>
        public static string FromImport(string lyric)
            => lyric == "-" && !Preferences.Default.UseDashExtension ? "+~" : lyric;

        /// <summary>Export: write extensions in the preferred spelling.</summary>
        public static string ToExport(string lyric)
            => Preferences.Default.UseDashExtension && (lyric == "+~" || lyric == "+*") ? "-" : lyric;

        /// <summary>
        /// DiffSinger breath alias. The user writes "br" and the model receives
        /// "AP", so no voicebank needs to be changed. Identity while the
        /// preference is off.
        /// </summary>
        public static string BreathLyricForModel(string lyric)
            => Preferences.Default.BreathAsBr && lyric == "br" ? "AP" : lyric;

        /// <summary>
        /// Whether a lyric denotes a breath note. "AP" is the model's own
        /// spelling; "br" only counts while the alias preference is on.
        /// </summary>
        public static bool IsBreathLyric(string lyric)
            => lyric == "AP" || (Preferences.Default.BreathAsBr && lyric == "br");
    }
}
