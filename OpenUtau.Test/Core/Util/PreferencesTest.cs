using System.Reflection;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Util {
    public class PreferencesTest {
        // The detached-piano-roll setting is gone: the piano roll is always docked. A preferences
        // file written by an older version still has the key, and must still load.
        [Fact]
        public void LegacyPreferencesWithDetachedPianoRollStillLoad() {
            Assert.Null(typeof(Preferences.SerializablePreferences).GetField("DetachPianoRoll"));

            var prefs = Json.Deserialize<Preferences.SerializablePreferences>(
                "{\"DetachPianoRoll\":true,\"Theme\":1,\"UndoLimit\":7}");

            Assert.NotNull(prefs);
            Assert.Equal(1, prefs!.Theme);
            Assert.Equal(7, prefs.UndoLimit);
        }
    }
}
