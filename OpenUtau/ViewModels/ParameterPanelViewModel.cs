using System;
using System.Collections.ObjectModel;
using System.Linq;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using static ReactiveUI.Primitives.SubscribeExtensions;

namespace OpenUtau.App.ViewModels {
    public class ParameterPanelViewModel : ViewModelBase {
        readonly NotesViewModel notes;
        bool syncing;
        UExpressionDescriptor? editParameter, displayParameter;
        double zoom = 1;
        int interpolation = 1;
        public ObservableCollection<UExpressionDescriptor> Parameters { get; } = new();
        public double CollapseArrowAngle => notes.ShowExpressions ? 0 : 180;
        public UExpressionDescriptor? EditParameter {
            get => editParameter;
            set {
                this.RaiseAndSetIfChanged(ref editParameter, value);
                if (!syncing && value != null) { notes.SelectParameter(value.abbr); }
                RefreshRanges();
            }
        }
        public UExpressionDescriptor? DisplayParameter {
            get => displayParameter;
            set {
                this.RaiseAndSetIfChanged(ref displayParameter, value);
                if (!syncing && value != null) { notes.SecondaryKey = value.abbr; }
                RefreshRanges();
            }
        }
        public int Interpolation {
            get => interpolation;
            set => this.RaiseAndSetIfChanged(ref interpolation, value);
        }
        public double Zoom {
            get => zoom;
            set { this.RaiseAndSetIfChanged(ref zoom, value); RefreshRanges(); }
        }
        public string EditMaximum => Range(editParameter, true);
        public string EditMinimum => Range(editParameter, false);
        public string DisplayMaximum => Range(displayParameter, true);
        public string DisplayMinimum => Range(displayParameter, false);

        public ParameterPanelViewModel(NotesViewModel notes) {
            this.notes = notes;
            notes.WhenAnyValue(n => n.ShowExpressions)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(CollapseArrowAngle)));
            notes.WhenAnyValue(n => n.PrimaryKey, n => n.SecondaryKey, n => n.Part, n => n.Project)
                .Subscribe(_ => Refresh());
        }
        public void Refresh() {
            syncing = true;
            try {
                var descriptors = notes.Project.expressions.Values.Select(d => {
                    if (notes.Part != null && notes.Project.tracks[notes.Part.trackNo]
                        .TryGetExpDescriptor(notes.Project, d.abbr, out var trackDescriptor)) {
                        return trackDescriptor;
                    }
                    return d;
                }).ToArray();
                if (!Parameters.SequenceEqual(descriptors)) {
                    Parameters.Clear();
                    foreach (var descriptor in descriptors) { Parameters.Add(descriptor); }
                }
                EditParameter = Parameters.FirstOrDefault(d => d.abbr == notes.PrimaryKey);
                DisplayParameter = Parameters.FirstOrDefault(d => d.abbr == notes.SecondaryKey);
            } finally { syncing = false; }
        }
        public void Swap() {
            var primary = notes.PrimaryKey;
            var secondary = notes.SecondaryKey;
            notes.SelectParameter(secondary);
            notes.SecondaryKey = primary;
        }
        string Range(UExpressionDescriptor? descriptor, bool upper) {
            if (descriptor == null) { return string.Empty; }
            if (descriptor.type == UExpressionType.Options) {
                return descriptor.options.ElementAtOrDefault(upper ? descriptor.options.Length - 1 : 0) ?? "";
            }
            var value = descriptor.defaultValue + ((upper ? descriptor.max : descriptor.min) - descriptor.defaultValue) / Zoom;
            return value.ToString("+0.##;-0.##;0") + (descriptor.abbr == "pitd" ? " cents" : "");
        }
        void RefreshRanges() {
            this.RaisePropertyChanged(nameof(EditMaximum));
            this.RaisePropertyChanged(nameof(EditMinimum));
            this.RaisePropertyChanged(nameof(DisplayMaximum));
            this.RaisePropertyChanged(nameof(DisplayMinimum));
        }
        public static double Interpolate(double t, int mode) => mode switch {
            0 => t < 1 ? 0 : 1,
            2 => t * t * (3 - 2 * t),
            _ => t,
        };
    }
}
