using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// The track operations the arrangement menu and a track row's context menu both offer.
    /// They live here rather than on TrackHeaderViewModel because the arrangement menu acts on
    /// whichever track is selected, and a row scrolled out of the canvas has no view model to call.
    /// </summary>
    internal static class TrackEdits {
        public static void Remove(UTrack track) {
            DocManager.Inst.StartUndoGroup("command.track.delete");
            DocManager.Inst.ExecuteCmd(new RemoveTrackCommand(DocManager.Inst.Project, track));
            DocManager.Inst.EndUndoGroup();
        }

        public static void MoveUp(UTrack track) {
            Move(track, true);
        }

        public static void MoveDown(UTrack track) {
            Move(track, false);
        }

        /// <summary>Copies a track along with everything on it.</summary>
        public static void Duplicate(UTrack track) {
            DocManager.Inst.StartUndoGroup("command.track.duplicate");
            var newTrack = CloneTrack(track);
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, newTrack));
            var parts = DocManager.Inst.Project.parts
                .Where(part => part.trackNo == track.TrackNo)
                .Select(part => part.Clone()).ToList();
            foreach (var part in parts) {
                part.trackNo = newTrack.TrackNo;
                DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, part));
            }
            DocManager.Inst.EndUndoGroup();
        }

        /// <summary>Copies a track's settings but nothing on it.</summary>
        public static void DuplicateSettings(UTrack track) {
            DocManager.Inst.StartUndoGroup("command.track.duplicate");
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, CloneTrack(track)));
            DocManager.Inst.EndUndoGroup();
        }

        public static async void SelectTrackColor(UTrack track) {
            var dialog = new TrackColorDialog();
            dialog.DataContext = new TrackColorViewModel(track);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                await dialog.ShowDialog(desktop.MainWindow);
                // The track rows redraw their accent from the track, so tell them it moved.
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
            }
        }

        static void Move(UTrack track, bool up) {
            var tracks = DocManager.Inst.Project.tracks;
            if (up ? track == tracks.First() : track == tracks.Last()) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.order");
            DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, track, up));
            DocManager.Inst.EndUndoGroup();
        }

        static UTrack CloneTrack(UTrack track) {
            return new UTrack(track.TrackName + "_copy") {
                TrackNo = track.TrackNo + 1,
                Singer = track.Singer,
                Phonemizer = track.Phonemizer,
                RendererSettings = track.RendererSettings,
                Mute = track.Mute,
                Muted = track.Muted,
                Solo = false,
                Volume = track.Volume,
                Pan = track.Pan,
                TrackColor = track.TrackColor,
                TrackExpressions = track.TrackExpressions.Select(exp => exp.Clone()).ToList()
            };
        }
    }
}
