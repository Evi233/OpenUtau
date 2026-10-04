using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>
    /// Smoke tests that launch the real main window on the headless platform.
    /// Checks are meant to survive UI redesigns: no errors, the right app state (from view models),
    /// key parts present with real size (by type or x:Name, not text or position), and a non-blank frame.
    /// </summary>
    public class MainWindowTest {
        static bool coreInitialized;

        // What SplashWindow.Start does before showing the main window, minus audio. The splash itself
        // can't run headless: it waits for screens, which the headless platform doesn't report.
        internal static void InitCore() {
            if (coreInitialized) {
                return;
            }
            coreInitialized = true;
            UpdaterDialog.CheckForUpdateEnabled = false;
            ToolsManager.Inst.Initialize();
            SingerManager.Inst.Initialize();
            DocManager.Inst.Initialize(Thread.CurrentThread, TaskScheduler.FromCurrentSynchronizationContext());
            DocManager.Inst.PostOnUIThread = action => Avalonia.Threading.Dispatcher.UIThread.Post(action);
            DocManager.Inst.AddSubscriber(HeadlessUi.Errors);
        }

        // Opens the main window as the splash window would, runs the test, then saves a screenshot
        // of the final state (also when the test fails) and closes the window.
        static void WithMainWindow(string name, Action<MainWindow> test) {
            HeadlessUi.Run(() => {
                InitCore();
                HeadlessUi.Errors.Clear();
                // One project is shared by the whole run, and "New Project" asks to save the
                // current one first when it has unsaved changes. A test that leaves changes
                // behind would therefore leave the next one's new-project button showing a save
                // dialog, which headless never answers. Start every test from a clean project.
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(OpenUtau.Core.Format.Ustx.Create()));
                var window = new MainWindow { Width = 1280, Height = 800 };
                try {
                    window.Show();
                    window.InitProject();
                    HeadlessUi.Flush();
                    test(window);
                    HeadlessUi.Flush();
                    var errors = HeadlessUi.Errors.Snapshot();
                    Assert.True(errors.Count == 0, "Errors reported:\n" + string.Join("\n", errors));
                } finally {
                    HeadlessUi.Flush();
                    HeadlessUi.SaveScreenshot(window, name);
                    window.Close();
                }
            });
        }

        static void AssertHasSize(Control? control) {
            Assert.NotNull(control);
            Assert.True(control.IsEffectivelyVisible, $"{control} is not visible");
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, $"{control} has no size");
        }

        static void AssertRendered(Window window) {
            // A blank window has one or two colors; the smallest drawn page has dozens.
            Assert.True(HeadlessUi.CountRenderedColors(window) > 10, "The window looks blank");
        }

        static void Click(Window window, Control target) {
            // A tooltip left open by an earlier hover is an overlay above the window, and a click
            // aimed at the control underneath it lands on the tooltip instead.
            foreach (var control in window.GetVisualDescendants().OfType<Control>()) {
                if (ToolTip.GetIsOpen(control)) {
                    ToolTip.SetIsOpen(control, false);
                }
            }
            var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
            Assert.NotNull(center);
            window.MouseDown(center.Value, MouseButton.Left);
            window.MouseUp(center.Value, MouseButton.Left);
            HeadlessUi.Flush();
        }

        // Finds a button by its localized label's resource key, so it survives text and layout changes.
        static Button FindButtonWithText(Window window, string resourceKey) {
            var text = window.FindResource(resourceKey) as string;
            Assert.False(string.IsNullOrEmpty(text));
            var button = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.IsEffectivelyVisible &&
                    b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));
            Assert.NotNull(button);
            return button;
        }

        static MainWindowViewModel ViewModelOf(Window window) => Assert.IsType<MainWindowViewModel>(window.DataContext);

        [Fact]
        public void LaunchShowsStartPage() {
            WithMainWindow(nameof(LaunchShowsStartPage), window => {
                Assert.Equal(0, ViewModelOf(window).Page);
                AssertHasSize(window.GetVisualDescendants().OfType<Menu>().FirstOrDefault());
                AssertHasSize(FindButtonWithText(window, "welcome.new"));
                AssertRendered(window);
            });
        }

        [Fact]
        public void NewProjectOpensEditor() {
            WithMainWindow(nameof(NewProjectOpensEditor), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));

                Assert.Equal(1, ViewModelOf(window).Page);
                var trackCount = DocManager.Inst.Project.tracks.Count;
                Assert.True(trackCount > 0);
                // One header per track in the new project.
                var headers = window.GetVisualDescendants().OfType<TrackHeader>().ToList();
                Assert.Equal(trackCount, headers.Count);
                Assert.All(headers, header => AssertHasSize(header));
                AssertHasSize(window.FindControl<Control>("partsCanvas"));
                AssertRendered(window);
            });
        }

        // A project always has a track selected: entering one picks the first, and
        // neither deselecting the last track nor deleting it can empty the selection.
        [Fact]
        public void ProjectKeepsATrackSelected() {
            WithMainWindow(nameof(ProjectKeepsATrackSelected), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                var tracks = ViewModelOf(window).TracksViewModel;
                Assert.Same(tracks.Tracks[0], Assert.Single(tracks.SelectedTracks));

                // Command-clicking the only selected header again would toggle it off.
                tracks.ToggleSelectTrack(tracks.Tracks[0]);
                Assert.Same(tracks.Tracks[0], Assert.Single(tracks.SelectedTracks));

                // Deleting the selected track hands the selection to the one left.
                var project = DocManager.Inst.Project;
                var first = tracks.Tracks[0];
                DocManager.Inst.StartUndoGroup("command.track.add");
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = 1 }));
                DocManager.Inst.EndUndoGroup();
                Assert.Equal(2, tracks.Tracks.Count);
                DocManager.Inst.StartUndoGroup("command.track.delete");
                DocManager.Inst.ExecuteCmd(new RemoveTrackCommand(project, first));
                DocManager.Inst.EndUndoGroup();
                Assert.Same(tracks.Tracks[0], Assert.Single(tracks.SelectedTracks));
            });
        }

        // The settings live in a docked panel opened from the gear in the right-hand
        // function bar; the gear toggles it and the panel only exists while open.
        [Fact]
        public void PreferencesPanelToggles() {
            WithMainWindow(nameof(PreferencesPanelToggles), window => {
                var viewModel = ViewModelOf(window);
                var gear = window.FindControl<ToggleButton>("PreferencesButton");
                Assert.NotNull(gear);
                Assert.False(viewModel.IsPreferencesPanelOpen);

                Click(window, gear!);
                Assert.True(viewModel.IsPreferencesPanelOpen);
                Assert.Same(gear, window.FindControl<ToggleButton>("PreferencesButton"));
                Assert.True(gear!.IsChecked);
                var panel = window.GetVisualDescendants().OfType<PreferencesPanel>().Single();
                AssertHasSize(panel);
                // The panel's sections are populated from the view model.
                Assert.NotNull(viewModel.PreferencesViewModel);
                Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("section"));
                AssertRendered(window);
                // The window's own screenshot is taken after the test, when the panel is
                // closed again, so keep one of the open panel for layout review.
                HeadlessUi.SaveScreenshot(window, nameof(PreferencesPanelToggles) + "-open");

                Click(window, gear);
                Assert.False(viewModel.IsPreferencesPanelOpen);
                Assert.False(panel.IsEffectivelyVisible);
            });
        }

        [Fact]
        public void RenderPanelTogglesAndSharesPreferences() {
            WithMainWindow(nameof(RenderPanelTogglesAndSharesPreferences), window => {
                var viewModel = ViewModelOf(window);
                var button = window.FindControl<ToggleButton>("RenderButton");
                Assert.NotNull(button);
                Assert.False(viewModel.IsRenderPanelOpen);

                Click(window, button!);
                Assert.True(viewModel.IsRenderPanelOpen);
                Assert.True(button!.IsChecked);
                window.MouseMove(new Point(600, 400));
                HeadlessUi.Flush();
                var icon = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
                Assert.Equal(OpenUtau.App.ThemeManager.AccentBrush1.ToString(), icon.Stroke!.ToString());
                var panel = window.GetVisualDescendants().OfType<RenderPreferencesPanel>().Single();
                AssertHasSize(panel);
                Assert.Same(viewModel.PreferencesViewModel, panel.DataContext);
                foreach (var key in new[] { "prefs.rendering", "prefs.utau", "prefs.diffsinger" }) {
                    Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text == OpenUtau.App.ThemeManager.GetString(key));
                }
                AssertRendered(window);
                HeadlessUi.SaveScreenshot(window, nameof(RenderPanelTogglesAndSharesPreferences) + "-open");

                Click(window, button);
                Assert.False(viewModel.IsRenderPanelOpen);
                Assert.False(panel.IsEffectivelyVisible);
                HeadlessUi.Flush();
                Assert.NotEqual(OpenUtau.App.ThemeManager.AccentBrush1.ToString(), icon.Stroke!.ToString());
                Click(window, window.FindControl<ToggleButton>("PreferencesButton")!);
                var preferences = window.GetVisualDescendants().OfType<PreferencesPanel>().Single();
                Assert.Same(panel.DataContext, preferences.DataContext);
                foreach (var key in new[] { "prefs.rendering", "prefs.utau", "prefs.diffsinger" }) {
                    Assert.DoesNotContain(preferences.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text == OpenUtau.App.ThemeManager.GetString(key));
                }
            });
        }

        [Fact]
        public void DiffSingerPresetsApplyAndCustomSettingsExpand() {
            WithMainWindow(nameof(DiffSingerPresetsApplyAndCustomSettingsExpand), window => {
                Click(window, window.FindControl<ToggleButton>("RenderButton")!);
                var vm = ViewModelOf(window).PreferencesViewModel!;
                var panel = window.GetVisualDescendants().OfType<RenderPreferencesPanel>().Single();
                var selector = panel.FindControl<ComboBox>("DiffSingerPresetSelector")!;
                var custom = panel.FindControl<StackPanel>("DiffSingerCustomSettings")!;
                int original = vm.DiffSingerPreset;
                try {
                    foreach (var (preset, acoustic, variance, pitch, live) in new[] {
                        (1, 50, 50, 20, LivePitchMode.Quality),
                        (2, 10, 10, 5, LivePitchMode.Fast),
                        (0, 20, 20, 10, LivePitchMode.Normal),
                    }) {
                        selector.SelectedIndex = preset;
                        HeadlessUi.Flush();
                        Assert.False(custom.IsEffectivelyVisible);
                        Assert.True(panel.FindControl<ComboBox>("DiffSingerOnnxRunner")!.IsEffectivelyVisible);
                        Assert.True(panel.FindControl<ComboBox>("DiffSingerGameBackend")!.IsEffectivelyVisible);
                        Preferences.Default.SkipRenderingMutedTracks = false;
                        Assert.True(Preferences.Default.SkipRenderingMutedTracks);
                        Assert.Equal(acoustic, Preferences.Default.DiffSingerSteps);
                        Assert.Equal(variance, Preferences.Default.DiffSingerStepsVariance);
                        Assert.Equal(pitch, Preferences.Default.DiffSingerStepsPitch);
                        Assert.Equal((int)live, Preferences.Default.RealTimePitchMode);
                        Preferences.Default.MigrateRealTimePitchMode();
                        Assert.Equal((int)live, Preferences.Default.RealTimePitchMode);
                    }
                    selector.SelectedIndex = 3;
                    HeadlessUi.Flush();
                    Assert.True(custom.IsEffectivelyVisible);
                    vm.DiffSingerSteps = 100;
                    vm.DiffSingerStepsVariance = 50;
                    vm.DiffSingerStepsPitch = 20;
                    vm.LivePitchModeIndex = 1;
                    Preferences.Default.MigrateRealTimePitchMode();
                    Assert.Equal(100, Preferences.Default.DiffSingerSteps);
                    Assert.Equal(50, Preferences.Default.DiffSingerStepsVariance);
                    Assert.Equal(20, Preferences.Default.DiffSingerStepsPitch);
                    Assert.Equal((int)LivePitchMode.Fast, Preferences.Default.RealTimePitchMode);
                } finally {
                    vm.DiffSingerPreset = original;
                }
            });
        }

        // The voice panel shares the right-hand dock with the settings panel; it is opened
        // from its own button and does not close the other one.
        [Fact]
        public void VoicePanelToggles() {
            WithMainWindow(nameof(VoicePanelToggles), window => {
                var viewModel = ViewModelOf(window);
                var voice = window.FindControl<ToggleButton>("VoiceButton");
                Assert.NotNull(voice);
                Assert.False(viewModel.IsVoicePanelOpen);

                Click(window, voice!);
                Assert.True(viewModel.IsVoicePanelOpen);
                Assert.NotNull(viewModel.VoiceViewModel);
                var panel = window.GetVisualDescendants().OfType<TrackVoicePanel>().Single();
                AssertHasSize(panel);
                Assert.True(voice!.IsChecked);

                // Both panels can be open at once.
                Click(window, window.FindControl<ToggleButton>("PreferencesButton")!);
                Assert.True(viewModel.IsPreferencesPanelOpen);
                Assert.True(panel.IsEffectivelyVisible);
                AssertRendered(window);
                HeadlessUi.SaveScreenshot(window, nameof(VoicePanelToggles) + "-both");

                Click(window, voice);
                Assert.False(viewModel.IsVoicePanelOpen);
                Assert.False(panel.IsEffectivelyVisible);
            });
        }

        // The panel edits whichever track is selected, so changing the selection retargets it.
        [Fact]
        public void VoicePanelFollowsTheSelectedTrack() {
            WithMainWindow(nameof(VoicePanelFollowsTheSelectedTrack), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                Click(window, window.FindControl<ToggleButton>("VoiceButton")!);
                var tracks = ViewModelOf(window).TracksViewModel;
                var voice = ViewModelOf(window).VoiceViewModel;
                Assert.NotNull(voice);
                Assert.Same(tracks.Tracks[0], voice!.Track);

                // A track added from anywhere else is a TrackCommand, which the panel listens for.
                var project = DocManager.Inst.Project;
                DocManager.Inst.StartUndoGroup("command.track.add");
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = 1 }));
                DocManager.Inst.EndUndoGroup();
                HeadlessUi.Flush();

                tracks.SelectTrack(tracks.Tracks[1]);
                HeadlessUi.Flush();
                Assert.Same(tracks.Tracks[1], voice.Track);
                Assert.Same(tracks.Tracks[1], Assert.Single(tracks.SelectedTracks));

                tracks.SelectTrack(tracks.Tracks[0]);
                HeadlessUi.Flush();
                Assert.Same(tracks.Tracks[0], voice.Track);
            });
        }

        // The panel is built the first time it is opened, so a selection made before that raised
        // no event it could hear: opening it must show the track the arrangement has selected,
        // not the project's first track.
        [Fact]
        public void VoicePanelOpensOnTheSelectedTrack() {
            WithMainWindow(nameof(VoicePanelOpensOnTheSelectedTrack), window => WithFakeSingers(1, Array.Empty<int>(), Array.Empty<int>(), singers => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                var tracks = ViewModelOf(window).TracksViewModel;
                var project = DocManager.Inst.Project;
                DocManager.Inst.StartUndoGroup("command.track.add");
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = 1 }));
                DocManager.Inst.EndUndoGroup();
                tracks.Tracks[1].Singer = singers[0];
                tracks.SelectTrack(tracks.Tracks[1]);
                HeadlessUi.Flush();

                // Nothing has opened the panel yet, which is exactly why the selection above
                // could not reach it.
                Assert.Null(ViewModelOf(window).VoiceViewModel);
                Click(window, window.FindControl<ToggleButton>("VoiceButton")!);

                var voice = ViewModelOf(window).VoiceViewModel;
                Assert.NotNull(voice);
                Assert.Same(tracks.Tracks[1], voice!.Track);
                Assert.True(voice.HasSinger);
                Assert.Same(singers[0], voice.Singer);
            }));
        }

        // The note properties panel shares the dock with the voice and settings panels, and is
        // built the first time it is opened. So, like the voice panel, it has to be seated on
        // what is already selected: the notes the piano roll has selected in the part it has
        // open, which the selection event it never heard cannot tell it.
        [Fact]
        public void NotePropertiesPanelOpensOnTheSelectedNotes() {
            WithMainWindow(nameof(NotePropertiesPanelOpensOnTheSelectedNotes), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                var viewModel = ViewModelOf(window);
                Assert.Null(viewModel.NotePropertiesViewModel);

                var project = DocManager.Inst.Project;
                var part = new UVoicePart { trackNo = 0, position = 0 };
                // CreateNote is what gives a note its two pitch points, which validation
                // reads: a bare UNote has an empty curve and would throw on the first flush.
                var note = project.CreateNote(60, 0, 480);
                note.lyric = "a";
                part.notes.Add(note);
                DocManager.Inst.StartUndoGroup("command.part.add");
                DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
                DocManager.Inst.EndUndoGroup();
                HeadlessUi.Flush();

                // Open the part in the piano roll the way the arrangement does, by double
                // clicking it. Two clicks with no Flush between them are one double click.
                var partsCanvas = window.FindControl<PartsCanvas>("partsCanvas")!;
                // The template project already has a part on track 0; this is the one just added.
                var partControl = partsCanvas.Children.OfType<PartControl>().Single(c => c.part == part);
                var point = partControl.TranslatePoint(
                    new Point(partControl.Bounds.Width / 2, partControl.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                HeadlessUi.Flush();
                HeadlessUi.Flush();
                Assert.True(viewModel.ShowPianoRoll);

                var notesViewModel = window.GetVisualDescendants().OfType<PianoRoll>().Single().ViewModel.NotesViewModel;
                notesViewModel.Selection.Select(note);

                Click(window, window.FindControl<ToggleButton>("NotePropertiesButton")!);
                Assert.True(viewModel.IsNotePropertiesPanelOpen);
                var noteProperties = viewModel.NotePropertiesViewModel;
                Assert.NotNull(noteProperties);
                Assert.Same(part, noteProperties!.Part);
                Assert.True(noteProperties.IsNoteSelected);
                Assert.Equal("a", noteProperties.Lyric);
                AssertHasSize(window.GetVisualDescendants().OfType<NotePropertiesControl>().Single());

                // And it edits the notes it was seated on.
                noteProperties.SetNoteParams("Lyric", "ka");
                Assert.Equal("ka", note.lyric);
            });
        }

        // "What does this engine support" is the whole point of the parameter list: a Classic
        // track offers the classic renderer's parameters, and the ones it cannot use are absent.
        [Fact]
        public void VoicePanelShowsTheEnginesParameters() {
            WithMainWindow(nameof(VoicePanelShowsTheEnginesParameters), window => WithFakeSingers(3, Array.Empty<int>(), Array.Empty<int>(), singers => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                Click(window, window.FindControl<ToggleButton>("VoiceButton")!);
                var voice = ViewModelOf(window).VoiceViewModel;
                Assert.NotNull(voice);
                // No singer and no renderer yet, so there is nothing the track can be asked for.
                Assert.Empty(voice!.Parameters);

                voice.Singer = singers[0];
                HeadlessUi.Flush();
                Assert.True(voice.HasParameters);
                Assert.Contains(voice.Parameters, row => row.Abbr == "vel");
                // The voice color is not a value a whole track can be pinned to.
                Assert.DoesNotContain(voice.Parameters, row => row.Abbr == "clr");
                HeadlessUi.SaveScreenshot(window, nameof(VoicePanelShowsTheEnginesParameters) + "-parameters");
            }));
        }

        // The language list is derived from the DiffSinger phonemizers' own language codes, which
        // are not the same as their tags.
        [Fact]
        public void DiffSingerPhonemizersMapToTheirLanguages() {
            InitCore();
            Assert.IsType<DiffSingerEnglishPhonemizer>(PhonemizerChoices.ForLanguage("en")!.Create());
            Assert.IsType<DiffSingerKoreanPhonemizer>(PhonemizerChoices.ForLanguage("ko")!.Create());
            Assert.IsType<DiffSingerJyutpingPhonemizer>(PhonemizerChoices.ForLanguage("yue")!.Create());
            Assert.Null(PhonemizerChoices.ForLanguage("no-such-language"));
            // A voicebank's language code reaches the string table, which spells Cantonese its own way.
            Assert.NotEqual("yue", PhonemizerChoices.LanguageName("yue"));
            Assert.Contains(PhonemizerChoices.NonDiffSinger(),
                factory => factory.type == typeof(OpenUtau.Core.DefaultPhonemizer));
            Assert.DoesNotContain(PhonemizerChoices.NonDiffSinger(),
                factory => PhonemizerChoices.IsDiffSinger(factory));
        }

        class FakeSinger : USinger {
            readonly string name;
            public FakeSinger(string name) {
                this.name = name;
                found = true;
            }
            public override string Id => name;
            public override string Name => name;
            public override USingerType SingerType => USingerType.Classic;
            public override IList<string> SearchTerms { get; } = new List<string>();
        }

        // Adds singers that aren't loadable, so the test must not select them.
        static void WithFakeSingers(int count, IEnumerable<int> favorites, IEnumerable<int> recents, Action<List<FakeSinger>> test) {
            var singers = Enumerable.Range(0, count).Select(i => new FakeSinger($"Fake Singer {i:00}")).ToList();
            var manager = SingerManager.Inst;
            var prefs = Preferences.Default;
            var (oldFavorites, oldRecents) = (prefs.FavoriteSingers, prefs.RecentSingers);
            manager.SingerGroups.TryGetValue(USingerType.Classic, out var oldGroup);
            try {
                singers.ForEach(singer => manager.Singers[singer.Id] = singer);
                manager.SingerGroups[USingerType.Classic] = (oldGroup ?? new List<USinger>()).Concat(singers).ToList();
                prefs.FavoriteSingers = favorites.Select(i => singers[i].Id).ToList();
                prefs.RecentSingers = recents.Select(i => singers[i].Id).ToList();
                test(singers);
            } finally {
                singers.ForEach(singer => manager.Singers.Remove(singer.Id));
                if (oldGroup == null) {
                    manager.SingerGroups.Remove(USingerType.Classic);
                } else {
                    manager.SingerGroups[USingerType.Classic] = oldGroup;
                }
                (prefs.FavoriteSingers, prefs.RecentSingers) = (oldFavorites, oldRecents);
            }
        }

        [Fact]
        public void SingerFlyoutSearches() {
            WithMainWindow(nameof(SingerFlyoutSearches), window => WithFakeSingers(11, new[] { 0, 1, 2 }, new[] { 5, 6, 7 }, singers => {
                singers[4].SearchTerms.Add("kasane teto");
                Click(window, FindButtonWithText(window, "welcome.new"));
                var header = window.GetVisualDescendants().OfType<TrackHeader>().First();
                Click(window, header.FindControl<Button>("SingerButton")!);

                var flyout = window.GetVisualDescendants().OfType<SingerFlyout>().SingleOrDefault();
                AssertHasSize(flyout);
                var viewModel = Assert.IsType<SingerFlyoutViewModel>(flyout.DataContext);
                var searchBox = flyout.FindControl<TextBox>("SearchBox");
                var grid = flyout.FindControl<ScrollViewer>("TileScroller");
                Assert.NotNull(searchBox);
                AssertHasSize(grid);
                // Favorites, recents, then the rest (with any real singers after the fake ones).
                Assert.Equal(new[] { 3, 6 }, viewModel.SectionStarts);
                // Typing searches right away.
                Assert.Same(searchBox, window.FocusManager?.GetFocusedElement());
                var gridSize = grid.Bounds.Size;

                window.KeyTextInput("fake singer 1");
                HeadlessUi.Flush();
                Assert.Equal(new[] { singers[10] }, viewModel.Tiles.Select(tile => tile.Singer));
                Assert.Empty(viewModel.SectionStarts);
                // Sections without matches get no divider.
                viewModel.SearchText = "FAKE SINGER 0";
                HeadlessUi.Flush();
                Assert.Equal(new[] { 0, 1, 2, 5, 6, 7, 3, 4, 8, 9 }.Select(i => singers[i]), viewModel.Tiles.Select(tile => tile.Singer));
                Assert.Equal(new[] { 3, 6 }, viewModel.SectionStarts);
                viewModel.SearchText = "fake singer 01";
                HeadlessUi.Flush();
                Assert.Equal(new[] { singers[1] }, viewModel.Tiles.Select(tile => tile.Singer));
                Assert.Empty(viewModel.SectionStarts);
                // The flyout keeps its size while searching.
                Assert.Equal(gridSize, grid.Bounds.Size);

                // Search terms from the voicebank config find the singer and show in its tip.
                viewModel.SearchText = "kasane";
                HeadlessUi.Flush();
                var tile = Assert.Single(viewModel.Tiles);
                Assert.Same(singers[4], tile.Singer);
                Assert.Contains("kasane teto", tile.ToolTipText);

                viewModel.SearchText = "no such singer";
                HeadlessUi.Flush();
                Assert.Empty(viewModel.Tiles);
                Assert.True(viewModel.NoMatch);

                // Escape clears the search before closing the flyout.
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                HeadlessUi.Flush();
                Assert.Equal(string.Empty, viewModel.SearchText);
                Assert.Equal(viewModel.AllTileCount, viewModel.Tiles.Count);
                Assert.True(flyout.IsEffectivelyVisible);
                AssertRendered(window);

                // Right-clicking a tile offers to open its location and edit its search terms,
                // and to remove it from recent singers only when it is one.
                List<object?> MenuHeaders(FakeSinger singer) {
                    var tileControl = flyout.GetVisualDescendants().OfType<Border>()
                        .First(b => b.Classes.Contains("singerTile") && (b.DataContext as SingerTileViewModel)?.Singer == singer);
                    var point = tileControl.TranslatePoint(new Point(10, 10), window)!.Value;
                    window.MouseDown(point, MouseButton.Right);
                    window.MouseUp(point, MouseButton.Right);
                    HeadlessUi.Flush();
                    var headers = window.GetVisualDescendants().OfType<MenuItem>().Select(item => item.Header).ToList();
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    HeadlessUi.Flush();
                    return headers;
                }
                var headers = MenuHeaders(singers[0]);
                Assert.Contains(window.FindResource("tracks.openlocation"), headers);
                Assert.Contains(window.FindResource("tracks.searchterms.edit"), headers);
                Assert.DoesNotContain(window.FindResource("tracks.removefromrecent"), headers);
                Assert.Contains(window.FindResource("tracks.removefromrecent"), MenuHeaders(singers[5]));
            }));
        }

        // Finds a track header and the menu on it, and opens that menu with a right-click on
        // empty row space (the knobs reset on right-click, so they must not be hit).
        static ContextMenu OpenTrackRowMenu(Window window, TrackHeader header) {
            var point = header.TranslatePoint(
                new Point(header.Bounds.Width / 2, header.Bounds.Height - 10), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            HeadlessUi.Flush();
            return header.GetVisualDescendants().OfType<Grid>()
                .First(grid => grid.ContextMenu != null).ContextMenu!;
        }

        static MenuItem MenuItemWithHeader(Window window, object header) =>
            window.GetVisualDescendants().OfType<MenuItem>().First(item => Equals(item.Header, header));

        // Two clicks with nothing in between, so the gesture recognizer counts them as one
        // double-click.
        static void DoubleClick(Window window, Control control) {
            var point = control.TranslatePoint(
                new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            for (int i = 0; i < 2; i++) {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            HeadlessUi.Flush();
        }

        // The time signature and the tempo sit in the ruler's left end to be read, so they open
        // their editors on a double-click only.
        [Fact]
        public void RulerNumbersEditOnDoubleClick() {
            WithMainWindow(nameof(RulerNumbersEditOnDoubleClick), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                var timeSignature = window.FindControl<TextBlock>("TimeSignatureText")!;
                var tempo = window.FindControl<TextBlock>("TempoText")!;
                AssertHasSize(timeSignature);
                AssertHasSize(tempo);

                Click(window, timeSignature);
                Assert.Empty(window.OwnedWindows);

                DoubleClick(window, timeSignature);
                Assert.IsType<TimeSignatureDialog>(Assert.Single(window.OwnedWindows)).Close();
                HeadlessUi.Flush();

                DoubleClick(window, tempo);
                Assert.IsType<TypeInDialog>(Assert.Single(window.OwnedWindows)).Close();
                HeadlessUi.Flush();
            });
        }

        // The renderer picker and the track settings dialog live in the row's menu now, and
        // both need a voicebank: without one they are there but greyed out.
        [Fact]
        public void TrackRowMenuOffersRendererAndSettings() {
            WithMainWindow(nameof(TrackRowMenuOffersRendererAndSettings), window =>
                WithFakeSingers(1, Array.Empty<int>(), Array.Empty<int>(), singers => {
                    Click(window, FindButtonWithText(window, "welcome.new"));
                    var header = window.GetVisualDescendants().OfType<TrackHeader>().First();
                    var viewModel = header.ViewModel!;
                    var rendererHeader = window.FindResource("tracks.selectrenderer")!;
                    var settingsHeader = window.FindResource("tracks.tracksettings")!;

                    OpenTrackRowMenu(window, header);
                    Assert.False(MenuItemWithHeader(window, rendererHeader).IsEnabled);
                    Assert.False(MenuItemWithHeader(window, settingsHeader).IsEnabled);
                    // The list is rebuilt when the menu opens, so nothing is listed without a voicebank.
                    Assert.Empty(viewModel.RenderersMenuItems!);
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    HeadlessUi.Flush();

                    viewModel.SelectSingerCommand.Execute(singers[0]).Subscribe();
                    HeadlessUi.Flush();
                    Assert.True(viewModel.HasSinger);
                    OpenTrackRowMenu(window, header);
                    Assert.True(MenuItemWithHeader(window, rendererHeader).IsEnabled);
                    Assert.True(MenuItemWithHeader(window, settingsHeader).IsEnabled);
                    Assert.NotEmpty(viewModel.RenderersMenuItems!);
                    var submenu = MenuItemWithHeader(window, rendererHeader);
                    Assert.Equal(viewModel.RenderersMenuItems!.Count,
                        Assert.IsAssignableFrom<IEnumerable>(submenu.ItemsSource).Cast<object>().Count());
                }));
        }

        // The arrangement menu sits at the top of the track column and drives the selected
        // track, so its commands work even for a track scrolled out of the canvas.
        [Fact]
        public void ArrangementMenuAddsATrack() {
            WithMainWindow(nameof(ArrangementMenuAddsATrack), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                var tracks = ViewModelOf(window).TracksViewModel;
                var count = tracks.Tracks.Count;
                var button = window.FindControl<Button>("ArrangementMenuButton");
                AssertHasSize(button);

                Click(window, button!);
                Click(window, MenuItemWithHeader(window, window.FindResource("command.track.add")!));
                Assert.Equal(count + 1, tracks.Tracks.Count);
                Assert.Equal(count + 1, window.GetVisualDescendants().OfType<TrackHeader>().Count());
            });
        }

        // The row keeps its shape at the smallest track height: the voicebank line and the fx
        // button fold away, the name, the knobs and mute/solo stay.
        [Fact]
        public void TrackRowSurvivesTheSmallHeight() {
            WithMainWindow(nameof(TrackRowSurvivesTheSmallHeight), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                ViewModelOf(window).TracksViewModel.SetTrackHeight(44);
                HeadlessUi.Flush();

                var header = window.GetVisualDescendants().OfType<TrackHeader>().Single();
                AssertHasSize(header);
                Assert.False(header.ViewModel!.IsSingerVisible);
                Assert.False(header.ViewModel.IsFxVisible);
                AssertHasSize(header.FindControl<Button>("TrackNameButton"));
                var knobs = header.GetVisualDescendants().OfType<Knob>().ToList();
                Assert.Equal(2, knobs.Count);
                Assert.All(knobs, AssertHasSize);
                Assert.All(header.GetVisualDescendants().OfType<ToggleButton>(), AssertHasSize);
                AssertRendered(window);
            });
        }

        // The default row is the compact one the arrangement layout is modelled on: 300 wide and
        // 64 tall, which still leaves room for every control, so nothing folds away.
        [Fact]
        public void DefaultTrackRowIsCompactAndComplete() {
            WithMainWindow(nameof(DefaultTrackRowIsCompactAndComplete), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                Assert.Equal(64d, ViewModelOf(window).TracksViewModel.TrackHeight);
                HeadlessUi.Flush();

                var header = window.GetVisualDescendants().OfType<TrackHeader>().Single();
                AssertHasSize(header);
                Assert.True(header.ViewModel!.IsSingerVisible);
                Assert.True(header.ViewModel.IsFxVisible);
                AssertHasSize(header.FindControl<Button>("TrackNameButton"));
                AssertHasSize(header.FindControl<Button>("SingerButton"));
                Assert.All(header.GetVisualDescendants().OfType<Knob>().ToList(), AssertHasSize);
                AssertRendered(window);
            });
        }

        // The volume and pan knobs replaced the faders: the value shows in the hover tip, and
        // a right-click (like the double-click) puts it back to the default.
        [Fact]
        public void TrackKnobsResetOnRightClick() {
            WithMainWindow(nameof(TrackKnobsResetOnRightClick), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                var header = window.GetVisualDescendants().OfType<TrackHeader>().First();
                var knob = header.GetVisualDescendants().OfType<Knob>().First();
                var track = DocManager.Inst.Project.tracks[0];

                knob.Value = 7;
                HeadlessUi.Flush();
                Assert.Equal(7, track.Volume);
                Assert.Contains("+07.0", ToolTip.GetTip(knob) as string);

                var point = knob.TranslatePoint(
                    new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Right);
                window.MouseUp(point, MouseButton.Right);
                HeadlessUi.Flush();
                Assert.Equal(0, knob.Value);
                Assert.Equal(0, track.Volume);
                Assert.Contains("+00.0", ToolTip.GetTip(knob) as string);
            });
        }
    }
}
