using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NAudio.Wave;
using NWaves.Audio;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;
using Serilog;

namespace OpenUtau.App.Views {
    /// <summary>
    /// Which page of <see cref="SingersEditorWindow"/> to show when it opens.
    /// </summary>
    public enum SingersEditorTab {
        Oto,
        CharacterYaml,
        DsConfig,
    }

    /// <summary>
    /// The oto table, the waveform and the voicebank config editors. Too wide for
    /// a docked panel, so it is a window of its own, opened from the piano roll.
    /// </summary>
    public partial class SingersEditorWindow : Window, ICmdSubscriber {
        private bool editingCell = false;

        WaveFile? wav;
        string? wavPath;

        // Only stop on close what this window started; the panel plays singer
        // samples through the same audio output and may still be playing one.
        private bool ownsPlayback;

        public SingersEditorWindow() {
            InitializeComponent();
            DocManager.Inst.AddSubscriber(this);
            foreach (var (editor, tab) in YamlTabs) {
                editor.DirtyChanged += (s, e) => tab.Header = editor.IsDirty ? editor.FileName + " *" : editor.FileName;
            }
            DataContextChanged += (s, e) => {
                yamlSub?.Dispose();
                resetSub?.Dispose();
                yamlSub = resetSub = null;
                if (DataContext is SingersViewModel viewModel) {
                    var singer = viewModel.WhenAnyValue(vm => vm.Singer);
                    yamlSub = ObservableMixins.WhereNotNull(singer).Subscribe(s => LoadYaml(viewModel, s));
                    // Skip the current value: only an actual singer change should
                    // blank the plot.
                    resetSub = singer.Skip(1).Subscribe(_ => ResetOtoView());
                }
            };
        }

        IDisposable? yamlSub, resetSub;

        (YamlEditor editor, TabItem tab)[] YamlTabs => new[] {
            (CharacterYamlEditor, CharacterYamlTab),
            (DsConfigEditor, DsConfigTab),
        };

        USinger? yamlSinger;
        bool closeConfirmed;

        public void SelectTab(SingersEditorTab tab) {
            Tabs.SelectedItem = tab switch {
                SingersEditorTab.CharacterYaml => CharacterYamlTab,
                SingersEditorTab.DsConfig => DsConfigTab,
                _ => OtoTab,
            };
        }

        // The YAML tabs follow the selected singer, after asking about unsaved changes to the last one's files.
        async void LoadYaml(SingersViewModel viewModel, USinger singer) {
            if (singer == yamlSinger) {
                return;
            }
            yamlSinger = singer;
            foreach (var (editor, _) in YamlTabs) {
                await editor.ConfirmClose(this, canStay: false);
            }
            // Another singer was picked while asking; that change loads its files.
            if (yamlSinger != singer || singer == null) {
                return;
            }
            Action onSaved = () => {
                if (viewModel.Singer == singer) {
                    viewModel.RefreshSinger();
                } else {
                    singer.Reload();
                }
            };
            if (viewModel.HasCharacterYaml) {
                CharacterYamlEditor.Load(Path.Combine(singer.Location, "character.yaml"), typeof(VoicebankConfig), onSaved);
            }
            if (viewModel.HasDsConfig) {
                DsConfigEditor.Load(Path.Combine(singer.Location, "dsconfig.yaml"), typeof(Core.DiffSinger.DsConfig), onSaved);
            }
            if (Tabs.SelectedItem is TabItem { IsVisible: false }) {
                Tabs.SelectedItem = OtoTab;
            }
        }

        protected override async void OnClosing(WindowClosingEventArgs e) {
            base.OnClosing(e);
            if (closeConfirmed || !YamlTabs.Any(t => t.editor.IsDirty)) {
                return;
            }
            e.Cancel = true;
            foreach (var (editor, tab) in YamlTabs.Where(t => t.editor.IsDirty)) {
                Tabs.SelectedItem = tab;
                if (!await editor.ConfirmClose(this, canStay: true)) {
                    return;
                }
            }
            closeConfirmed = true;
            Close();
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            DocManager.Inst.RemoveSubscriber(this);
            yamlSub?.Dispose();
            resetSub?.Dispose();
            yamlSub = resetSub = null;
            if (ownsPlayback) {
                var playBack = PlaybackManager.Inst.AudioOutput;
                if (playBack.PlaybackState == PlaybackState.Playing) {
                    playBack.Stop();
                }
                ownsPlayback = false;
            }
        }

        private SingersViewModel? viewModel => DataContext as SingersViewModel;

        // Called when the singer changed, from the panel's combo box. The wav and
        // the plot belong to the old singer, so drop them; the selection that the
        // table settles on afterwards draws the new one.
        private void ResetOtoView() {
            _isChangingSinger = true;

            _otoLoadCts?.Cancel();
            _otoLoadCts = null;

            wav = null;
            wavPath = null;
            OtoPlot.WaveFile = null;
            OtoPlot.F0 = null;

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                _isChangingSinger = false;
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        private CancellationTokenSource? _otoLoadCts;
        private bool _isChangingSinger;

        void OnSelectedOtoChanged(object sender, SelectionChangedEventArgs e) {
            if (_isChangingSinger) {
                return;
            }

            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel?.Singer == null || e.AddedItems.Count < 1) {
                return;
            }
            var oto = (UOto?)e.AddedItems[0];
            if (oto == null || !File.Exists(oto.File)) {
                return;
            }
            DrawOto(oto);
        }

        void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e) {
            editingCell = true;
        }

        void OnCellEditEnded(object sender, DataGridCellEditEndedEventArgs e) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (e.EditAction == DataGridEditAction.Commit) {
                viewModel?.NotifyOtoChanged();
            }
            editingCell = false;
        }

        void GotoSourceFile(object sender, RoutedEventArgs args) {
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }
            try {
                OS.GotoFile(oto.File);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void GotoVLabelerOto(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }
            OpenInVLabeler(viewModel.Singer, oto);
        }

        void OnEditInVLabeler(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer != null) {
                OpenInVLabeler(viewModel.Singer, null);
            }
        }

        private void OpenInVLabeler(USinger singer, UOto? oto) {
            string path = Core.Util.Preferences.Default.VLabelerPath;
            if (string.IsNullOrEmpty(path) || !OS.AppExists(path)) {
                MessageBox.Show(
                    this,
                    ThemeManager.GetString("singers.editoto.setvlabelerpath"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            try {
                Integrations.VLabelerClient.Inst.GotoOto(singer, oto);
            } catch (Exception e) {
                MessageBox.Show(
                    this,
                    e.ToString(),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
            }
        }

        void GotoSetParamOto(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }
            OpenInSetParam(viewModel.Singer, oto);
        }

        private void OpenInSetParam(USinger singer, UOto oto) {
            string path = Core.Util.Preferences.Default.SetParamPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                MessageBox.Show(
                    this,
                    ThemeManager.GetString("singers.editoto.setsetparampath"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            try {
                var tempFile = Path.Combine(PathManager.Inst.CachePath, "temp.tmp");
                Ust.WriteForSetParam(DocManager.Inst.Project, tempFile, new List<UOto> { oto });

                var startInfo = new ProcessStartInfo() {
                    FileName = path,
                    Arguments = $"\"{tempFile}\"",
                    WorkingDirectory = Path.GetDirectoryName(path)
                };
                using (var process = Process.Start(startInfo)) {
                    process!.WaitForExit();
                }
            } catch (Exception e) {
                MessageBox.Show(
                    this,
                    e.ToString(),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
            }
        }

        public void OnPlaySelectedFile(object sender, RoutedEventArgs e) {
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }

            PlaybackManager.Inst.PlayFile(oto.File);
            ownsPlayback = true;
        }

        void RegenFrq(object sender, RoutedEventArgs args) {
            if (OtoGrid != null &&
                sender is Control control &&
                DataContext is SingersViewModel viewModel) {
                string[] files = OtoGrid.SelectedItems
                    .Cast<UOto>()
                    .Select(oto => oto.File)
                    .ToHashSet()
                    .ToArray();
                MessageBox? msgbox = null;
                string text = ThemeManager.GetString("singers.editoto.regenfrq.regenerating");
                if (files.Length > 1) {
                    msgbox = MessageBox.ShowModal(this, text, text);
                }
                var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                viewModel.RegenFrq(files, control.Tag as string, count => {
                    msgbox?.SetText(string.Format("{0}\n{1} / {2}", text, count, files.Length));
                }).ContinueWith(task => {
                    msgbox?.Close();
                    if (task.IsFaulted && task.Exception != null) {
                        MessageBox.ShowError(this, task.Exception);
                    } else {
                        DrawOto(viewModel.SelectedOto);
                    }
                }, scheduler);
            }
        }

        async void DrawOto(UOto? oto) {
            _otoLoadCts?.Cancel();
            _otoLoadCts = new CancellationTokenSource();
            var token = _otoLoadCts.Token;

            if (oto == null) {
                wavPath = null;
                wav = null;
                OtoPlot.WaveFile = null;
                OtoPlot.F0 = null;
                return;
            }

            OtoPlot.Timing = new() {
                cutoff = oto.Cutoff,
                offset = oto.Offset,
                consonant = oto.Consonant,
                preutter = oto.Preutter,
                overlap = oto.Overlap,
            };

            var targetFile = oto.File;

            try {
                var wavTask = Task.Run(() => LoadWav(targetFile), token);
                var f0Task = Task.Run(() => LoadF0(targetFile), token);

                await Task.WhenAll(wavTask, f0Task);

                if (token.IsCancellationRequested) {
                    return;
                }

                wav = await wavTask;
                wavPath = targetFile;

                OtoPlot.WaveFile = wav;
                OtoPlot.F0 = await f0Task;
            } catch (OperationCanceledException) {
                // Ignore cancellations from fast navigation
            } catch (Exception e) {
                Log.Error(e, $"Failed to draw OTO for {targetFile}");
            }
        }

        WaveFile? LoadWav(string filePath) {
            if (wavPath == filePath && wav != null) {
                return wav;
            }
            try {
                using var memStream = new MemoryStream();
                using (var waveStream = Core.Format.Wave.OpenFile(filePath)) {
                    NAudio.Wave.WaveFileWriter.WriteWavFileToStream(memStream, waveStream);
                }
                memStream.Seek(0, SeekOrigin.Begin);
                return new WaveFile(memStream);
            } catch (Exception e) {
                Log.Error(e, $"Failed to load wav: {filePath}");
                return null;
            }
        }

        Tuple<int, double[]>? LoadF0(string filePath) {
            var frq = new Classic.Frq();
            if (frq.Load(filePath)) {
                return Tuple.Create(frq.hopSize, frq.f0);
            }
            return null;
        }

        void OnKeyDown(object sender, KeyEventArgs args) {
            // The single-key shortcuts are for the oto view; elsewhere, such as in a YAML tab, keys are typing.
            if (args.Handled || editingCell || (FocusManager?.GetFocusedElement() is TextBox) || Tabs.SelectedItem != OtoTab) {
                return;
            }
            var viewModel = DataContext as SingersViewModel;
            if (viewModel == null || OtoPlot == null || OtoPlot.WaveFile == null) {
                return;
            }
            double durationMs = OtoPlot.WaveFile.Signals[0].Duration * 1000;
            args.Handled = true;
            switch (args.Key) {
                case Key.D1:
                    viewModel.SetOffset(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D2:
                    viewModel.SetOverlap(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D3:
                    viewModel.SetPreutter(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D4:
                    viewModel.SetFixed(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D5:
                    viewModel.SetCutoff(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.W:
                    OtoPlot.Zoom(0.5, 0.5);
                    break;
                case Key.S:
                    OtoPlot.Zoom(1.5, 0.5);
                    break;
                case Key.A:
                    OtoPlot.Pan(-0.25);
                    break;
                case Key.D:
                    OtoPlot.Pan(0.25);
                    break;
                case Key.Q:
                    if (OtoGrid != null) {
                        OtoGrid.SelectedIndex = Math.Max(0, OtoGrid.SelectedIndex - 1);
                        OtoGrid.ScrollIntoView(OtoGrid.SelectedItem, null);
                    }
                    break;
                case Key.E:
                    if (OtoGrid != null) {
                        OtoGrid.SelectedIndex++;
                        OtoGrid.ScrollIntoView(OtoGrid.SelectedItem, null);
                    }
                    break;
                case Key.F:
                    OtoPlot.Zoom(double.PositiveInfinity, 0.5);
                    break;
                default:
                    args.Handled = false;
                    break;
            }
        }

        #region ICmdSubscriber

        // The loading notifications are raised by the view model around its slow work. This
        // window is where that work is visible, so it hosts the busy indicator.
        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadingNotification loadingNotif && loadingNotif.window == typeof(SingersEditorWindow)) {
                if (loadingNotif.startLoading) {
                    LoadingWindow.BeginLoading(this);
                } else {
                    LoadingWindow.EndLoading();
                }
            } else if (cmd is OtoChangedNotification otoChanged) {
                if (viewModel is not { } vm) {
                    return;
                }
                if (otoChanged.external) {
                    vm.RefreshSinger();
                }
                DrawOto(vm.SelectedOto);
            } else if (cmd is GotoOtoNotification editOto) {
                if (viewModel is not { } vm) {
                    return;
                }
                if (editOto.singer != null) {
                    vm.GotoOto(editOto.singer, editOto.oto);
                }
                SelectTab(SingersEditorTab.Oto);
                OtoGrid?.ScrollIntoView(OtoGrid.SelectedItem, null);
                Activate();
            }
        }

        #endregion
    }
}
