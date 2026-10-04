using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.Api;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class TrackHeaderViewModel : ViewModelBase, IActivatableViewModel {
        public int TrackNo => track.TrackNo + 1;
        public USinger Singer => track.Singer;
        public Core.Render.IRenderer Renderer => track.RendererSettings.Renderer;
        public ReactiveCommand<USinger, RxVoid> SelectSingerCommand { get; }
        public IReadOnlyList<MenuItemViewModel>? RenderersMenuItems { get; set; }
        public ReactiveCommand<string, RxVoid> SelectRendererCommand { get; }
        [Reactive] public partial string TrackName { get; set; } = string.Empty;
        [Reactive] public partial SolidColorBrush TrackAccentColor { get; set; } = ThemeManager.GetTrackColor("Green").AccentColor;
        [Reactive] public partial TrackColor TrackColor { get; set; } = ThemeManager.GetTrackColor("Green");
        [Reactive] public partial double Volume { get; set; }
        [Reactive] public partial double Pan { get; set; }
        [Reactive] public partial bool Mute { get; set; }
        [Reactive] public partial bool Muted { get; set; }
        [Reactive] public partial bool Solo { get; set; }
        [Reactive] public partial bool IsSelected { get; set; }
        [Reactive] public partial bool IsSingerVisible { get; set; }
        [Reactive] public partial bool IsFxVisible { get; set; }
        [Reactive] public partial bool MixFxEnabled { get; set; }
        [Reactive] public partial IBrush HeaderBorderBrush { get; set; } = ThemeManager.NeutralAccentBrushSemi;
        [Reactive] public partial IBrush StripBrush { get; set; } = Brushes.Transparent;
        [Reactive] public partial IBrush StripIconBrush { get; set; } = ThemeManager.ForegroundBrush;

        public ViewModelActivator Activator { get; }

        private readonly UTrack track;

        // Parameterless constructor for Avalonia preview only.
        public TrackHeaderViewModel() {
            SelectSingerCommand = ReactiveCommand.Create<USinger>(_ => { });
            SelectRendererCommand = ReactiveCommand.Create<string>(_ => { });
            Activator = new ViewModelActivator();
            track = new UTrack(DocManager.Inst.Project);
            this.WhenAnyValue(x => x.IsSelected)
                .Subscribe(_ => RefreshSelectionStyle());
            RefreshSelectionStyle();
        }

        public TrackHeaderViewModel(UTrack track) {
            this.track = track;
            SelectSingerCommand = ReactiveCommand.Create<USinger>(singer => {
                if (TrackSingerEdits.ChangeSinger(track, singer)) {
                    this.RaisePropertyChanged(nameof(Singer));
                    this.RaisePropertyChanged(nameof(SingerDisplay));
                    this.RaisePropertyChanged(nameof(HasSinger));
                    this.RaisePropertyChanged(nameof(Renderer));
                }
            });
            SelectRendererCommand = ReactiveCommand.Create<string>(name => {
                var settings = new URenderSettings {
                    renderer = name,
                };
                DocManager.Inst.StartUndoGroup("command.track.setting");
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, track, settings));
                DocManager.Inst.EndUndoGroup();
                this.RaisePropertyChanged(nameof(Renderer));
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
            });

            Activator = new ViewModelActivator();

            TrackName = track.TrackName;
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Green");
            Volume = track.Volume;
            Pan = track.Pan;
            Mute = track.Mute;
            Muted = track.Muted;
            Solo = track.Solo;
            MixFxEnabled = track.MixFx?.Enabled ?? false;
            this.WhenAnyValue(x => x.Volume)
                .Subscribe(volume => {
                    track.Volume = volume;
                    DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(track.TrackNo, Muted ? -24 : volume));
                    this.RaisePropertyChanged(nameof(VolumeTip));
                });
            this.WhenAnyValue(x => x.Pan)
                .Subscribe(pan => {
                    track.Pan = pan;
                    DocManager.Inst.ExecuteCmd(new PanChangeNotification(track.TrackNo, pan));
                    this.RaisePropertyChanged(nameof(PanTip));
                });
            this.WhenAnyValue(x => x.Mute)
                .Subscribe(mute => {
                    track.Mute = mute;
                });
            this.WhenAnyValue(x => x.Muted)
                .Subscribe(muted => {
                    track.Muted = muted;
                    DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(track.TrackNo, muted ? -24 : Volume));
                });
            this.WhenAnyValue(x => x.Solo)
                .Subscribe(solo => {
                    track.Solo = solo;
                });
            this.WhenAnyValue(x => x.MixFxEnabled)
                .Subscribe(enabled => {
                    if (track.MixFx != null) {
                        track.MixFx.Enabled = enabled;
                    } else if (enabled) {
                        track.MixFx = new UMixFx { Enabled = true };
                    }
                });
            this.WhenAnyValue(x => x.IsSelected)
                .Subscribe(_ => RefreshSelectionStyle());

            RefreshSelectionStyle();
        }

        /// <summary>The voicebank line: the singer in brackets, or a prompt to pick one.</summary>
        public string SingerDisplay => track.Singer != null
            ? $"({track.Singer})"
            : ThemeManager.GetString("tracks.selectsinger");

        /// <summary>The menu items that need a voicebank: renderer and track settings.</summary>
        public bool HasSinger => track.Singer != null && track.Singer.Found;

        public string VolumeTip => $"{ThemeManager.GetString("tracks.volume")} {Volume:+#00.0;-#00.0}";

        public string PanTip => $"{ThemeManager.GetString("tracks.pan")} {Pan:00R;00L;C}";

        public void RefreshSelectionStyle() {
            HeaderBorderBrush = IsSelected
                ? ThemeManager.AccentBrush1
                : ThemeManager.NeutralAccentBrushSemi;
            StripBrush = IsSelected ? ThemeManager.AccentBrush1 : Brushes.Transparent;
            StripIconBrush = IsSelected ? Brushes.White : ThemeManager.ForegroundBrush;
        }

        public void ToggleSolo() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(track.TrackNo, !track.Solo, false));
        }

        public void SoloAdditionally() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(track.TrackNo, !track.Solo, true));
        }

        public void UnsoloAll() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(-1, false, false));
        }

        public void ToggleMute() {
            if (!Mute) {
                Mute = true;
            } else {
                Mute = false;
            }
            this.RaisePropertyChanged(nameof(Mute));
            JudgeMuted();
        }

        public void ToggleMuteWithBool(bool mute) {
            Mute = mute;
            this.RaisePropertyChanged(nameof(Mute));
            JudgeMuted();
        }

        public void MuteOnly() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, false));
            ToggleMute();
        }

        public void MuteAllOthers() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, true));
            ToggleMute();
        }

        public void UnmuteAll() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, false));
        }

        public void JudgeMuted() {
            if (Solo) {
                Muted = false;
            } else if (Mute) {
                Muted = true;
            } else if (DocManager.Inst.Project.SoloTrackExist) {
                Muted = true;
            } else {
                Muted = false;
            }
            this.RaisePropertyChanged(nameof(Muted));
        }

        public void RefreshRenderers() {
            var items = new List<MenuItemViewModel>();
            if (track != null && track.Singer != null && track.Singer.Found) {
                items.AddRange(Core.Render.Renderers.GetSupportedRenderers(track.Singer.SingerType)
                    .Select(name => new MenuItemViewModel() {
                        Header = name,
                        Command = SelectRendererCommand,
                        CommandParameter = name,
                    }));
            }
            RenderersMenuItems = items.ToArray();
            this.RaisePropertyChanged(nameof(RenderersMenuItems));
        }

        public void ManuallyRaise() {
            TrackName = track.TrackName;
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Green");
            RefreshSelectionStyle();
            this.RaisePropertyChanged(nameof(Singer));
            this.RaisePropertyChanged(nameof(SingerDisplay));
            this.RaisePropertyChanged(nameof(HasSinger));
            this.RaisePropertyChanged(nameof(TrackNo));
            this.RaisePropertyChanged(nameof(TrackName));
            this.RaisePropertyChanged(nameof(TrackAccentColor));
            this.RaisePropertyChanged(nameof(TrackColor));
            this.RaisePropertyChanged(nameof(Renderer));
            this.RaisePropertyChanged(nameof(Mute));
            this.RaisePropertyChanged(nameof(Muted));
            this.RaisePropertyChanged(nameof(Solo));
            MixFxEnabled = track.MixFx?.Enabled ?? false;
            this.RaisePropertyChanged(nameof(MixFxEnabled));
            this.RaisePropertyChanged(nameof(Volume));
            this.RaisePropertyChanged(nameof(VolumeTip));
            this.RaisePropertyChanged(nameof(Pan));
            this.RaisePropertyChanged(nameof(PanTip));
        }

        public void Remove() => TrackEdits.Remove(track);

        public void MoveUp() => TrackEdits.MoveUp(track);

        public void MoveDown() => TrackEdits.MoveDown(track);

        public void Rename() {
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("tracks.rename");
            dialog.SetText(track.TrackName);
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name) && name != track.TrackName) {
                    DocManager.Inst.StartUndoGroup("command.track.setting");
                    this.TrackName = name;
                    DocManager.Inst.ExecuteCmd(new RenameTrackCommand(DocManager.Inst.Project, track, name));
                    DocManager.Inst.EndUndoGroup();
                }
            };
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                dialog.ShowDialog(desktop.MainWindow);
            }
        }

        public void SelectTrackColor() => TrackEdits.SelectTrackColor(track);

        public void Duplicate() => TrackEdits.Duplicate(track);

        public void DuplicateSettings() => TrackEdits.DuplicateSettings(track);

        public void VoiceColorRemapping() {
            if (track.Singer != null && track.Singer.Found && track.VoiceColorExp != null) {
                DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(track.TrackNo, false));
            }
        }

        public void OpenMixFxDialog() {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                MixFxDialog.Open(desktop.MainWindow, track);
            }
        }
    }
}
