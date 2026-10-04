using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class AudioExportTrackViewModel : ViewModelBase {
        public UTrack Track { get; }
        [Reactive] public partial string Name { get; set; }
        [Reactive] public partial bool Enabled { get; set; }
        [Reactive] public partial bool Selected { get; set; }
        [Reactive] public partial double Progress { get; set; }
        public AudioExportTrackViewModel(UTrack track) { Track = track; Refresh(); }
        public void Refresh() { Name = Track.TrackName; Enabled = !Track.Muted; }
    }
    public partial class AudioExportViewModel : ViewModelBase, ICmdSubscriber {
        [Reactive] public partial string DestinationFolder { get; set; } = "./";
        [Reactive] public partial string FileName { get; set; } = "untitled";
        [Reactive] public partial bool MixDown { get; set; } = true;
        [Reactive] public partial double MixProgress { get; set; }
        [Reactive] public partial int ChannelIndex { get; set; }
        [Reactive] public partial int AspirationIndex { get; set; } = 2;
        [Reactive] public partial int BitDepthIndex { get; set; }
        [Reactive] public partial int SampleRateIndex { get; set; } = 1;
        [Reactive] public partial bool IsBusy { get; set; }
        [Reactive] public partial string Status { get; set; } = "";
        public string[] Channels { get; } = { "单声道", "立体声" };
        public string[] AspirationOutputs { get; } = { "无气声输出", "导出独立气声为文件", "合并气声到文件" };
        public string[] BitDepths { get; } = { "16-bit", "24-bit", "32-bit 浮点" };
        public string[] SampleRates { get; } = { "22050 Hz", "44100 Hz", "48000 Hz", "88200 Hz", "96000 Hz", "192000 Hz" };
        public ObservableCollection<AudioExportTrackViewModel> Tracks { get; } = new();
        public AudioExportViewModel() {
            Refresh(true);
            DocManager.Inst.AddSubscriber(this);
        }
        public void Refresh(bool newProject = false) {
            var project = DocManager.Inst.Project;
            if (newProject) {
                Tracks.Clear();
                FileName = string.IsNullOrEmpty(project.FilePath) ? "untitled" : Path.GetFileNameWithoutExtension(project.FilePath);
                Status = "";
            }
            var previous = Tracks.ToDictionary(t => t.Track);
            var next = project.tracks.Select(track => previous.TryGetValue(track, out var row) ? row : new AudioExportTrackViewModel(track)).ToArray();
            if (!Tracks.SequenceEqual(next)) {
                Tracks.Clear();
                foreach (var row in next) { Tracks.Add(row); }
            }
            foreach (var row in Tracks) { row.Refresh(); }
        }
        public async Task ExportAsync() {
            if (IsBusy) return;
            Refresh();
            if (!MixDown && !Tracks.Any(t => t.Selected && t.Enabled)) { Status = "请选择混音或至少一条音轨。"; return; }
            if (string.IsNullOrWhiteSpace(DestinationFolder) || string.IsNullOrWhiteSpace(FileName)) { Status = "请填写目标文件夹和文件名。"; return; }
            IsBusy = true;
            MixProgress = 0;
            foreach (var row in Tracks) { row.Progress = 0; }
            Status = "正在渲染并导出……";
            try {
                var project = DocManager.Inst.Project;
                var baseFolder = string.IsNullOrEmpty(project.FilePath) ? Environment.CurrentDirectory : Path.GetDirectoryName(project.FilePath)!;
                var folder = Path.GetFullPath(DestinationFolder, baseFolder);
                var options = new AudioExportOptions(ChannelIndex == 0 ? 1 : 2,
                    new[] { 16, 24, 32 }[BitDepthIndex], new[] { 22050, 44100, 48000, 88200, 96000, 192000 }[SampleRateIndex], (AspirationOutput)AspirationIndex);
                var rows = Tracks.ToArray();
                var selections = rows.Select(row => new AudioExportTrack(project.tracks.IndexOf(row.Track), row.Selected && row.Enabled, 1)).ToArray();
                var renderProgress = new System.Progress<AudioExportProgress>(progress => {
                    var row = rows.FirstOrDefault(r => project.tracks.IndexOf(r.Track) == progress.TrackNo);
                    if (row != null) { row.Progress = progress.Percent; }
                    var included = rows.Where(r => r.Enabled && (MixDown || r.Selected)).ToArray();
                    MixProgress = included.Length > 0 ? included.Average(r => r.Progress) : 0;
                });
                var files = await PlaybackManager.Inst.ExportAudio(project, folder, FileName, MixDown, 1, selections, options, renderProgress);
                foreach (var row in rows.Where(r => r.Enabled && (MixDown || r.Selected))) { row.Progress = 100; }
                MixProgress = 100;
                Status = $"已导出 {files.Length} 个 WAV 文件。";
            } catch (Exception error) {
                Status = "导出失败：" + error.Message;
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(error));
            } finally { IsBusy = false; }
        }
        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadProjectNotification) { Refresh(true); }
            else if (cmd is TrackRenderProgressNotification progress) {
                var row = Tracks.FirstOrDefault(r => r.Track.TrackNo == progress.TrackNo);
                if (row != null) { row.Progress = progress.Percent; }
                var included = Tracks.Where(r => r.Enabled && (MixDown || r.Selected)).ToArray();
                MixProgress = included.Length > 0 ? included.Average(r => r.Progress) : 0;
            }
            else if (cmd is TrackCommand || cmd is VolumeChangeNotification) { Refresh(); }
        }
    }
}
