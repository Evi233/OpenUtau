using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// One language a DiffSinger voicebank declares that a phonemizer can actually sing in.
    /// </summary>
    public class LanguageChoice {
        public string Code { get; }
        public string Name { get; }
        public PhonemizerFactory Factory { get; }

        public LanguageChoice(string code, string name, PhonemizerFactory factory) {
            Code = code;
            Name = name;
            Factory = factory;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// One row of the panel's Parameters section: the value every note of the track uses where its
    /// expression curve has no point.
    /// </summary>
    public partial class TrackExpressionViewModel : ViewModelBase {
        public string Name { get; }
        public string Abbr { get; }
        public float Min { get; }
        public float Max { get; }
        public bool IsNumerical { get; }
        public bool IsOptions { get; }
        public ObservableCollection<string> Options { get; } = new ObservableCollection<string>();

        [Reactive] public partial float Value { get; set; }
        [Reactive] public partial int SelectedOption { get; set; }
        /// <summary>Whether the track carries its own value for this expression.</summary>
        [Reactive] public partial bool IsModified { get; set; }
        [Reactive] public partial FontWeight NameFontWeight { get; set; } = FontWeight.Normal;
        /// <summary>The number as typed. Only read back when the edit ends, so typing is not fought.</summary>
        [Reactive] public partial string Text { get; set; } = string.Empty;

        readonly TrackVoiceViewModel parent;
        // The value the track currently holds. Edits are written only when they differ from it, so
        // the bindings firing on their own while the row is being built change nothing.
        float committedValue;

        // What the track would use without an override of its own, which is what Reset goes back to.
        readonly float projectValue;

        public TrackExpressionViewModel(
                TrackVoiceViewModel parent, UExpressionDescriptor descriptor, float projectValue, bool isModified) {
            this.parent = parent;
            this.projectValue = projectValue;
            Name = descriptor.name;
            Abbr = descriptor.abbr;
            Min = descriptor.min;
            Max = descriptor.max;
            IsNumerical = descriptor.type != UExpressionType.Options;
            IsOptions = descriptor.type == UExpressionType.Options;
            if (descriptor.options != null) {
                foreach (var option in descriptor.options) {
                    Options.Add(option);
                }
            }
            committedValue = descriptor.CustomDefaultValue;
            Value = committedValue;
            SelectedOption = (int)committedValue;
            Text = Format(committedValue);
            IsModified = isModified;
            NameFontWeight = isModified ? FontWeight.Bold : FontWeight.Normal;
            this.WhenAnyValue(row => row.IsModified)
                .Subscribe(modified => NameFontWeight = modified ? FontWeight.Bold : FontWeight.Normal);
            this.WhenAnyValue(row => row.Value)
                .Subscribe(value => Text = Format(value));
        }

        static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// Writes the value to the track. Called when an edit ends - a slider release, a text box
        /// losing focus, a menu pick - never while the value is still being dragged.
        /// </summary>
        public void Commit() {
            var value = IsOptions ? SelectedOption : Value;
            if (value == committedValue) {
                return;
            }
            committedValue = value;
            IsModified = parent.SetParameterDefault(Abbr, value);
        }

        /// <summary>Drops the track's own value and goes back to what the project uses.</summary>
        public void Reset() {
            Value = projectValue;
            SelectedOption = (int)projectValue;
            Commit();
        }

        /// <summary>Reads the typed number, then writes it. Called when the text box loses focus.</summary>
        public void CommitText() {
            var text = Text.Trim();
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                float.TryParse(text, out parsed)) {
                Value = Math.Clamp(parsed, Min, Max);
            }
            // Unreadable input falls back to what the track already has, which also puts the
            // displayed number back in order.
            Text = Format(Value);
            Commit();
        }
    }

    /// <summary>
    /// The right-hand panel's view of the selected track: the voicebank singing it, the parameters
    /// its renderer supports, and the language it sings in.
    /// </summary>
    public partial class TrackVoiceViewModel : ViewModelBase, ICmdSubscriber {
        /// <summary>The track being edited, or null before a project is loaded.</summary>
        public UTrack? Track { get; private set; }

        public ReactiveCommand<PhonemizerFactory, RxVoid> SelectPhonemizerCommand { get; }

        [Reactive] public partial IReadOnlyList<USinger> Singers { get; set; } = Array.Empty<USinger>();
        [Reactive] public partial Bitmap? Avatar { get; set; }
        [Reactive] public partial bool HasAvatar { get; set; }
        [Reactive] public partial bool HasSinger { get; set; }
        [Reactive] public partial string SingerName { get; set; } = string.Empty;
        [Reactive] public partial string Author { get; set; } = string.Empty;
        [Reactive] public partial bool HasAuthor { get; set; }
        [Reactive] public partial string Version { get; set; } = string.Empty;
        [Reactive] public partial bool HasVersion { get; set; }
        /// <summary>Placeholder for per-voicebank version management, which does not exist yet.</summary>
        [Reactive] public partial IReadOnlyList<string> DatabaseVersions { get; set; } = new string[] { "-" };
        [Reactive] public partial IReadOnlyList<TrackExpressionViewModel> Parameters { get; set; } = Array.Empty<TrackExpressionViewModel>();
        [Reactive] public partial bool HasParameters { get; set; }
        [Reactive] public partial bool IsLanguageVisible { get; set; }
        /// <summary>DiffSinger: the dropdown lists languages. Otherwise it lists phonemizers.</summary>
        [Reactive] public partial bool IsLanguageChoice { get; set; }
        [Reactive] public partial bool IsPhonemizerChoice { get; set; }
        [Reactive] public partial IReadOnlyList<LanguageChoice> Languages { get; set; } = Array.Empty<LanguageChoice>();
        [Reactive] public partial IReadOnlyList<MenuItemViewModel> PhonemizerMenuItems { get; set; } = Array.Empty<MenuItemViewModel>();
        [Reactive] public partial string PhonemizerName { get; set; } = string.Empty;

        USinger? singer;
        LanguageChoice? selectedLanguage;
        // Set while the panel copies state out of the track, so the setters below do not write it
        // straight back as if the user had picked something.
        bool syncing;

        public TrackVoiceViewModel() {
            SelectPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(factory => {
                var track = Track;
                if (track != null && TrackSingerEdits.ChangePhonemizer(track, factory)) {
                    Refresh();
                }
            });
            DocManager.Inst.AddSubscriber(this);
            MessageBus.Current.Listen<TrackSelectionEvent>()
                .Subscribe(e => SelectTrack(e.selectedTracks.FirstOrDefault()));
            MessageBus.Current.Listen<TracksRefreshEvent>()
                .Subscribe(_ => Refresh());
            RefreshSingers();
            SelectTrack(DocManager.Inst.Project.tracks.FirstOrDefault());
        }

        /// <summary>The voicebank singing the track. Picking one goes through the track header's path.</summary>
        public USinger? Singer {
            get => singer;
            set {
                if (syncing || ReferenceEquals(singer, value)) {
                    return;
                }
                var track = Track;
                if (track != null && TrackSingerEdits.ChangeSinger(track, value)) {
                    Refresh();
                }
            }
        }

        /// <summary>The language the voicebank is asked to sing in, for a DiffSinger voicebank.</summary>
        public LanguageChoice? SelectedLanguage {
            get => selectedLanguage;
            set {
                if (syncing || ReferenceEquals(selectedLanguage, value) || value == null) {
                    return;
                }
                var track = Track;
                if (track != null && TrackSingerEdits.ChangePhonemizer(track, value.Factory)) {
                    Refresh();
                }
            }
        }

        /// <summary>
        /// Points the panel at a track. Called from the selection event, and once by the
        /// main window at open, because the panel is built on first use and a selection
        /// made before that raised no event it could hear.
        /// </summary>
        public void SelectTrack(UTrack? track) {
            Track = track;
            this.RaisePropertyChanged(nameof(Track));
            Refresh();
        }

        /// <summary>Reads everything the panel shows back out of the track.</summary>
        public void Refresh() {
            syncing = true;
            try {
                var track = Track;
                singer = track?.Singer;
                this.RaisePropertyChanged(nameof(Singer));
                RefreshSinger();
                RefreshParameters();
                RefreshLanguage();
            } finally {
                syncing = false;
            }
        }

        void RefreshSingers() {
            var ordered = SingerFlyoutViewModel.OrderSingers(
                SingerManager.Inst.Singers,
                SingerManager.Inst.SingerGroups,
                Preferences.Default.RecentSingers,
                Preferences.Default.FavoriteSingers);
            Singers = ordered.SelectMany(section => section).ToList();
        }

        void RefreshSinger() {
            var track = Track;
            var current = track?.Singer;
            Avatar = current == null
                ? null
                : SingerAvatarCache.Get(current, bitmap => {
                    if (ReferenceEquals(Track?.Singer, current)) {
                        Avatar = bitmap;
                        HasAvatar = bitmap != null;
                    }
                });
            HasAvatar = Avatar != null;
            HasSinger = current != null;
            SingerName = current == null ? string.Empty : current.LocalizedName;
            Author = current?.Author ?? string.Empty;
            HasAuthor = !string.IsNullOrWhiteSpace(Author);
            Version = current?.Version ?? string.Empty;
            HasVersion = !string.IsNullOrWhiteSpace(Version);
            DatabaseVersions = new string[] { HasVersion ? Version : "-" };
        }

        // The parameters the track's renderer says it can use, in the order the project lists them,
        // followed by the ones only the renderer knows about. Nothing else is offered: an engine
        // that does not support a parameter should not show it.
        void RefreshParameters() {
            var track = Track;
            var rows = new List<TrackExpressionViewModel>();
            if (track != null) {
                var project = DocManager.Inst.Project;
                foreach (var descriptor in SupportedExpressions(track, project)) {
                    var projectValue = project.expressions.TryGetValue(descriptor.abbr, out var projectDescriptor) &&
                        projectDescriptor != null
                        ? projectDescriptor.CustomDefaultValue
                        : descriptor.defaultValue;
                    var entry = track.TrackExpressions.FirstOrDefault(exp => exp.abbr == descriptor.abbr);
                    rows.Add(new TrackExpressionViewModel(this, descriptor, projectValue,
                        entry != null && entry.CustomDefaultValue != entry.defaultValue));
                }
            }
            Parameters = rows;
            HasParameters = rows.Count > 0;
        }

        static IEnumerable<UExpressionDescriptor> SupportedExpressions(UTrack track, UProject project) {
            var seen = new HashSet<string>();
            foreach (var descriptor in track.GetSupportedExps(project)) {
                if (IsSelectable(descriptor) && seen.Add(descriptor.abbr)) {
                    yield return descriptor;
                }
            }
            // A renderer can support expressions the project does not register, which is exactly what
            // "what does this engine support" asks about: DiffSinger's velocity/energy/expressiveness
            // curves only reach the project through the expressions dialog's suggestion button.
            var settings = track.RendererSettings;
            var renderer = settings?.Renderer;
            if (settings == null || renderer == null) {
                yield break;
            }
            UExpressionDescriptor[]? suggestions = null;
            try {
                suggestions = renderer.GetSuggestedExpressions(track.Singer, settings);
            } catch (Exception e) {
                // Some renderers read their resampler's manifest here and have nothing to read yet.
                Log.Error(e, "Failed to get suggested expressions.");
            }
            if (suggestions == null) {
                yield break;
            }
            foreach (var descriptor in suggestions) {
                if (descriptor != null && IsSelectable(descriptor) &&
                    seen.Add(descriptor.abbr) &&
                    renderer.SupportsExpression(descriptor)) {
                    yield return descriptor;
                }
            }
        }

        // Voice color is not a value the whole track can be pinned to: the options are the singer's
        // subbanks, and a track-level copy would go stale the moment another voicebank is picked.
        // Masked curves have no value where none is drawn, so there is no default to set.
        static bool IsSelectable(UExpressionDescriptor descriptor) =>
            descriptor.type != UExpressionType.MaskedCurve &&
            descriptor.abbr != Core.Format.Ustx.CLR &&
            descriptor.abbr != Core.Format.Ustx.CLRY;

        void RefreshLanguage() {
            var track = Track;
            var current = track?.Singer;
            var choices = new List<LanguageChoice>();
            if (current != null && current.SingerType == USingerType.DiffSinger) {
                // Only languages the voicebank declares and a phonemizer can sing are offered.
                foreach (var code in current.Languages) {
                    var factory = PhonemizerChoices.ForLanguage(code);
                    if (factory != null) {
                        choices.Add(new LanguageChoice(code, PhonemizerChoices.LanguageName(code), factory));
                    }
                }
            }
            Languages = choices;
            IsLanguageChoice = choices.Count > 0;
            IsPhonemizerChoice = choices.Count == 0;
            IsLanguageVisible = IsLanguageChoice || IsPhonemizerChoice;
            selectedLanguage = choices.FirstOrDefault(choice => track != null && choice.Factory.type == track.Phonemizer.GetType());
            this.RaisePropertyChanged(nameof(SelectedLanguage));
            RefreshPhonemizers();
        }

        void RefreshPhonemizers() {
            var items = new List<MenuItemViewModel>();
            var track = Track;
            foreach (var group in PhonemizerChoices.NonDiffSinger()
                    .GroupBy(factory => factory.language)
                    .OrderBy(group => group.Key)) {
                items.Add(new MenuItemViewModel {
                    Header = PhonemizerChoices.GroupHeader(group.Key),
                    IsEnabled = false,
                });
                foreach (var factory in group) {
                    items.Add(new MenuItemViewModel {
                        Header = factory.ToString(),
                        Command = SelectPhonemizerCommand,
                        CommandParameter = factory,
                        IsChecked = track != null && factory.type == track.Phonemizer.GetType(),
                    });
                }
            }
            PhonemizerMenuItems = items;
            PhonemizerName = track == null ? string.Empty : track.Phonemizer.ToString();
        }

        /// <summary>
        /// Sets the track's own value for one expression, the value every note uses where its curve
        /// has no point. Returns whether the track now carries an override.
        ///
        /// The command rebuilds the project's expressions and re-reads every part, so this is called
        /// once per edit rather than continuously while a slider moves.
        /// </summary>
        public bool SetParameterDefault(string abbr, float value) {
            var project = DocManager.Inst.Project;
            var track = Track;
            if (track == null || !track.TryGetExpDescriptor(project, abbr, out var current)) {
                return false;
            }
            var descriptors = track.TrackExpressions.Select(exp => exp.Clone()).ToList();
            var index = descriptors.FindIndex(exp => exp.abbr == abbr);
            if (index < 0) {
                descriptors.Add(current.Clone());
                index = descriptors.Count - 1;
            }
            descriptors[index].CustomDefaultValue = value;
            // A track copy that says the same as the project's own is not an override. Expressions the
            // project does not have at all are kept, because dropping one would drop its row.
            if (project.expressions.TryGetValue(abbr, out var projectDescriptor) &&
                projectDescriptor != null &&
                descriptors[index].Equals(projectDescriptor)) {
                descriptors.RemoveAt(index);
            }
            DocManager.Inst.StartUndoGroup("command.track.exp");
            DocManager.Inst.ExecuteCmd(new ConfigureExpressionsCommand(
                project, project.expressions.Values.ToArray(), track, descriptors.ToArray()));
            DocManager.Inst.EndUndoGroup();
            return descriptors.Any(exp => exp.abbr == abbr && exp.CustomDefaultValue != exp.defaultValue);
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadProjectNotification) {
                // The project the held track belongs to has just been replaced, and every track
                // in the new one is a new object, so the panel has to re-seat itself here rather
                // than refresh against a track the current project does not have.
                SelectTrack(DocManager.Inst.Project.tracks.FirstOrDefault());
            } else if (cmd is ConfigureExpressionsCommand) {
                Refresh();
            } else if (cmd is TrackCommand) {
                if (Track == null) {
                    SelectTrack(DocManager.Inst.Project.tracks.FirstOrDefault());
                } else {
                    Refresh();
                }
            } else if (cmd is SingersRefreshedNotification refreshed && refreshed.singer == null) {
                RefreshSingers();
            }
        }
    }
}
