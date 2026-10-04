using System;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using Serilog;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// The one way a track's singer or phonemizer is changed. The track header's flyout and the
    /// track voice panel both go through here, so the two can never drift apart.
    /// </summary>
    internal static class TrackSingerEdits {
        /// <summary>
        /// Points the track at another singer, picking up the voicebank's remembered phonemizer
        /// and resetting the renderer when the new singer cannot be rendered by the old one.
        /// Returns whether anything changed.
        /// </summary>
        public static bool ChangeSinger(UTrack track, USinger? singer) {
            if (track.Singer == singer) {
                return false;
            }
            DocManager.Inst.StartUndoGroup("command.track.singer");
            ApplySingerToTrack(track, singer);
            DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(track.TrackNo, true));
            DocManager.Inst.EndUndoGroup();
            UpdateRecentSingers(singer);
            Preferences.Save();
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
            MessageBus.Current.SendMessage(new TracksRefreshEvent());
            return true;
        }

        /// <summary>
        /// Switches the track's phonemizer and remembers it for the track's singer, the way the
        /// track header's menu does. Returns whether anything changed.
        /// </summary>
        public static bool ChangePhonemizer(UTrack track, PhonemizerFactory factory) {
            if (track.Phonemizer.GetType() == factory.type) {
                return false;
            }
            DocManager.Inst.StartUndoGroup("command.track.setting");
            var phonemizer = factory.Create();
            Log.Information($"Loading Phonemizer: {phonemizer} on track {track.TrackNo}");
            DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, track, phonemizer));
            DocManager.Inst.EndUndoGroup();
            var name = phonemizer.GetType().FullName!;
            if (!string.IsNullOrEmpty(track.Singer?.Id)) {
                Preferences.Default.SingerPhonemizers[track.Singer.Id] = name;
            }
            Preferences.Default.RecentPhonemizers.Remove(name);
            Preferences.Default.RecentPhonemizers.Insert(0, name);
            while (Preferences.Default.RecentPhonemizers.Count > 8) {
                Preferences.Default.RecentPhonemizers.RemoveRange(
                    8, Preferences.Default.RecentPhonemizers.Count - 8);
            }
            Preferences.Save();
            MessageBus.Current.SendMessage(new TracksRefreshEvent());
            return true;
        }

        static void ApplySingerToTrack(UTrack targetTrack, USinger? singer) {
            if (singer is USinger selectedSinger) {
                Log.Information($"Loading Singer: {selectedSinger.Name}");
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, targetTrack, selectedSinger));
                if (!string.IsNullOrEmpty(selectedSinger.Id) &&
                    Preferences.Default.SingerPhonemizers.TryGetValue(selectedSinger.Id, out var phonemizerName) &&
                    TryChangePhonemizer(targetTrack, phonemizerName)) {
                } else if (!string.IsNullOrEmpty(selectedSinger.DefaultPhonemizer)) {
                    TryChangePhonemizer(targetTrack, selectedSinger.DefaultPhonemizer);
                }
                if (!selectedSinger.Found || selectedSinger.SingerType != targetTrack.RendererSettings.Renderer?.SingerType) {
                    var settings = new URenderSettings();
                    if (selectedSinger.Found) {
                        settings = new URenderSettings {
                            renderer = Renderers.GetDefaultRenderer(selectedSinger.SingerType),
                        };
                    }
                    DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, targetTrack, settings));
                }
            } else {
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, targetTrack, USinger.CreateMissing(string.Empty)));
                var settings = new URenderSettings();
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, targetTrack, settings));
            }
        }

        static void UpdateRecentSingers(USinger? singer) {
            if (!string.IsNullOrEmpty(singer?.Id) && singer.Found) {
                Preferences.Default.RecentSingers.Remove(singer.Id);
                Preferences.Default.RecentSingers.Insert(0, singer.Id);
                if (Preferences.Default.RecentSingers.Count > 16) {
                    Preferences.Default.RecentSingers.RemoveRange(
                        16, Preferences.Default.RecentSingers.Count - 16);
                }
            }
        }

        static bool TryChangePhonemizer(UTrack targetTrack, string phonemizerName) {
            try {
                var factory = PhonemizerFactory.Get(phonemizerName);
                var phonemizer = factory?.Create();
                if (phonemizer != null) {
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, targetTrack, phonemizer));
                    return true;
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load phonemizer {phonemizerName}");
            }
            return false;
        }
    }
}
