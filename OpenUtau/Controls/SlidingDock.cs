using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Controls {
    public class SlidingDock : Decorator {
        public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<SlidingDock, bool>(nameof(IsOpen));
        public static readonly StyledProperty<double> ExpandedWidthProperty = AvaloniaProperty.Register<SlidingDock, double>(nameof(ExpandedWidth), 340);
        public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
        public double ExpandedWidth { get => GetValue(ExpandedWidthProperty); set => SetValue(ExpandedWidthProperty, value); }
        public SlidingDock() {
            Width = 0;
            ClipToBounds = true;
            HorizontalAlignment = HorizontalAlignment.Left;
            Transitions = new Transitions {
                new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new CubicEaseOut() }
            };
        }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == IsOpenProperty || change.Property == ExpandedWidthProperty) {
                if (Preferences.Default.ReduceAnimations) Transitions = null;
                Width = IsOpen ? ExpandedWidth : 0;
                IsHitTestVisible = IsOpen;
            }
        }
    }
}
