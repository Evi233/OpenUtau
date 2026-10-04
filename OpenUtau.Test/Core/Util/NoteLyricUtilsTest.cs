using System;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// The two "editing" preferences: "-" as the vowel extension marker and
    /// "br" as the breath symbol. Everything here must be the identity while
    /// the preferences are off.
    /// </summary>
    [Collection(RenderSingletonCollection.Name)]
    public class NoteLyricUtilsTest : IDisposable {
        readonly bool previousDash = Preferences.Default.UseDashExtension;
        readonly bool previousBr = Preferences.Default.BreathAsBr;

        public void Dispose() {
            Preferences.Default.UseDashExtension = previousDash;
            Preferences.Default.BreathAsBr = previousBr;
        }

        static UProject BuildProject() {
            var project = new UProject();
            Format.Ustx.AddDefaultExpressions(project);
            return project;
        }

        /// <summary>Two abutting notes; the second one carries <paramref name="secondLyric"/>.</summary>
        static UNote[] BuildPair(UProject project, string firstLyric, string secondLyric, out UVoicePart part) {
            part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);
            var first = project.CreateNote(60, 0, 480);
            first.lyric = firstLyric;
            var second = project.CreateNote(60, 480, 480);
            second.lyric = secondLyric;
            part.notes.Add(first);
            part.notes.Add(second);
            part.Validate(new ValidateOptions { SkipPhonemizer = true }, project, project.tracks[0]);
            return new[] { first, second };
        }

        [Fact]
        public void DashIsGroupedAsExtensionOnlyWhenEnabled() {
            Preferences.Default.UseDashExtension = false;
            var offNotes = BuildPair(BuildProject(), "a", "-", out _);
            Assert.Null(offNotes[1].Extends);

            Preferences.Default.UseDashExtension = true;
            var onNotes = BuildPair(BuildProject(), "a", "-", out _);
            Assert.Same(onNotes[0], onNotes[1].Extends);
        }

        [Fact]
        public void PlusExtensionIsGroupedRegardlessOfThePreference() {
            Preferences.Default.UseDashExtension = true;
            var notes = BuildPair(BuildProject(), "a", "+~", out _);
            Assert.Same(notes[0], notes[1].Extends);
        }

        [Fact]
        public void BoundaryNormalizesStoredLyrics() {
            Preferences.Default.UseDashExtension = true;
            var project = BuildProject();
            var track = project.tracks[0];
            var notes = BuildPair(project, "a", "-", out var part);
            Assert.Equal("+~", notes[1].ToPhonemizerNote(track, part).lyric);

            // A tail note marked "&" must still reach the phonemizers as "-".
            notes[0].lyric = "&";
            Assert.Equal("-", notes[0].ToPhonemizerNote(track, part).lyric);
        }

        [Fact]
        public void BoundaryLeavesHeadDashNotesAlone() {
            Preferences.Default.UseDashExtension = true;
            var project = BuildProject();
            var track = project.tracks[0];
            // A lone "-" is a rest/placeholder head note, not an extension.
            var notes = BuildPair(project, "-", "a", out var part);
            Assert.Null(notes[0].Extends);
            Assert.Equal("-", notes[0].ToPhonemizerNote(track, part).lyric);
        }

        [Fact]
        public void ImportConvertsDashOnlyWhenDisabled() {
            Preferences.Default.UseDashExtension = false;
            Assert.Equal("+~", NoteLyricUtils.FromImport("-"));
            Assert.Equal("a", NoteLyricUtils.FromImport("a"));

            Preferences.Default.UseDashExtension = true;
            Assert.Equal("-", NoteLyricUtils.FromImport("-"));
        }

        [Fact]
        public void ExportConvertsExtensionsOnlyWhenEnabled() {
            Preferences.Default.UseDashExtension = false;
            Assert.Equal("+~", NoteLyricUtils.ToExport("+~"));
            Assert.Equal("+*", NoteLyricUtils.ToExport("+*"));
            Assert.Equal("-", NoteLyricUtils.ToExport("-"));

            Preferences.Default.UseDashExtension = true;
            Assert.Equal("-", NoteLyricUtils.ToExport("+~"));
            Assert.Equal("-", NoteLyricUtils.ToExport("+*"));
            Assert.Equal("-", NoteLyricUtils.ToExport("-"));
        }

        [Fact]
        public void TailMarkerMovesToAmpersandOnlyWhenEnabled() {
            Preferences.Default.UseDashExtension = false;
            Assert.Equal("-", NoteLyricUtils.TailLyricForWrite("-"));
            Assert.Equal("R", NoteLyricUtils.TailLyricForWrite("R"));

            Preferences.Default.UseDashExtension = true;
            Assert.Equal("&", NoteLyricUtils.TailLyricForWrite("-"));
            Assert.Equal("R", NoteLyricUtils.TailLyricForWrite("R"));
        }

        [Fact]
        public void ExtensionLyricForWriteFollowsThePreference() {
            Preferences.Default.UseDashExtension = false;
            Assert.Equal("+~", NoteLyricUtils.ExtensionLyricForWrite());

            Preferences.Default.UseDashExtension = true;
            Assert.Equal("-", NoteLyricUtils.ExtensionLyricForWrite());
        }

        [Fact]
        public void BreathAliasResolvesBrToApForTheModel() {
            Preferences.Default.BreathAsBr = false;
            Assert.Equal("br", NoteLyricUtils.BreathLyricForModel("br"));
            Assert.False(NoteLyricUtils.IsBreathLyric("br"));
            Assert.True(NoteLyricUtils.IsBreathLyric("AP"));

            Preferences.Default.BreathAsBr = true;
            Assert.Equal("AP", NoteLyricUtils.BreathLyricForModel("br"));
            Assert.True(NoteLyricUtils.IsBreathLyric("br"));
            Assert.True(NoteLyricUtils.IsBreathLyric("AP"));
            // Anything else is untouched.
            Assert.Equal("a", NoteLyricUtils.BreathLyricForModel("a"));
            Assert.False(NoteLyricUtils.IsBreathLyric("a"));
        }

        /// <summary>
        /// The English+ phonemizer has its own "br" branch, which must also keep
        /// resolving to the model's registered "AP" symbol.
        /// </summary>
        [Fact]
        public void DiffSingerBrLyricAlwaysResolvesToAP() {
            Preferences.Default.BreathAsBr = false;
            Assert.Equal("AP", DiffSingerBreathResult());

            Preferences.Default.BreathAsBr = true;
            Assert.Equal("AP", DiffSingerBreathResult());
        }

        static string DiffSingerBreathResult() {
            var phonemizer = new DiffSinger.DiffSingerARPAPlusEnglishPhonemizer();
            var note = new OpenUtau.Api.Phonemizer.Note {
                lyric = "br",
                position = 0,
                duration = 480,
                tone = 60,
            };
            var result = phonemizer.Process(
                new[] { note }, null, null, null, null, new OpenUtau.Api.Phonemizer.Note[0]);
            return result.phonemes[0].phoneme;
        }
    }
}
