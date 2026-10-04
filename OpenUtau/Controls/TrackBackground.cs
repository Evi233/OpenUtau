using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    class TrackBackground : TemplatedControl {
        public static readonly DirectProperty<TrackBackground, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackBackground, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<TrackBackground, bool> IsPianoRollProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsPianoRoll),
                o => o.IsPianoRoll,
                (o, v) => o.IsPianoRoll = v);
        public static readonly DirectProperty<TrackBackground, bool> IsKeyboardProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsKeyboard),
                o => o.IsKeyboard,
                (o, v) => o.IsKeyboard = v);
        public static readonly DirectProperty<TrackBackground, int> KeyProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, int>(
                nameof(Key),
                o => o.Key,
                (o, v) => o.Key = v);

        public double TrackHeight {
            get => _trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref _trackHeight, value);
        }
        public double TrackOffset {
            get => _trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref _trackOffset, value);
        }
        public bool IsPianoRoll {
            get => _isPianoRoll;
            set => SetAndRaise(IsPianoRollProperty, ref _isPianoRoll, value);
        }
        public bool IsKeyboard {
            get => _isKeyboard;
            set => SetAndRaise(IsKeyboardProperty, ref _isKeyboard, value);
        }
        public int Key {
            get => _key;
            set => SetAndRaise(KeyProperty, ref _key, value);
        }

        private double _trackHeight;
        private double _trackOffset;
        private bool _isPianoRoll;
        private bool _isKeyboard;
        private int _key;
        private int auditionTone = -1;

        internal void SetAuditionTone(int tone) {
            auditionTone = tone;
            InvalidateVisual();
        }

        internal Rect KeyboardKeyBounds(int tone) {
            double y = (ViewConstants.MaxTone - 1 - tone - TrackOffset) * TrackHeight;
            if (MusicMath.IsBlackKey(tone)) {
                return new Rect(0, y, Bounds.Width * 0.5, TrackHeight);
            }
            int[] whiteIndices = { 0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6 };
            int key = tone % 12;
            double height = TrackHeight * 12 / 7;
            double bottom = y + key * TrackHeight + TrackHeight;
            return new Rect(0, bottom - (whiteIndices[key] + 1) * height, Bounds.Width, height);
        }

        internal int KeyboardToneAt(Point point) {
            if (TrackHeight <= 0 || !new Rect(Bounds.Size).Contains(point)) {
                return -1;
            }
            foreach (bool black in new[] { true, false }) {
                for (int tone = 0; tone < ViewConstants.MaxTone; tone++) {
                    if (MusicMath.IsBlackKey(tone) == black && KeyboardKeyBounds(tone).Contains(point)) {
                        return tone;
                    }
                }
            }
            return -1;
        }

        void RenderKeyboard(DrawingContext context) {
            context.DrawRectangle(ThemeManager.BackgroundBrush, null, new Rect(Bounds.Size));
            foreach (bool black in new[] { false, true }) {
                for (int tone = 0; tone < ViewConstants.MaxTone; tone++) {
                    if (MusicMath.IsBlackKey(tone) != black) {
                        continue;
                    }
                    var bounds = KeyboardKeyBounds(tone);
                    if (bounds.Bottom <= 0 || bounds.Top >= Bounds.Height) {
                        continue;
                    }
                    double inset = black ? 0 : Math.Min(0.35, bounds.Height / 4);
                    var rect = new Rect(-3, bounds.Y + inset, bounds.Width + 3, bounds.Height - inset * 2);
                    context.DrawRectangle(black ? ThemeManager.BlackKeyBrush : ThemeManager.WhiteKeyBrush,
                        null, rect, 3, 3);
                    if (tone == auditionTone) {
                        using (context.PushOpacity(black ? 0.85 : 0.7)) {
                            context.DrawRectangle(ThemeManager.AccentBrush1, null, rect, 3, 3);
                        }
                    }
                    if (tone % 12 == 0 && TrackHeight >= 12) {
                        var text = TextLayoutCache.Get(MusicMath.GetToneName(tone), ThemeManager.WhiteKeyNameBrush, 11);
                        double rowY = (ViewConstants.MaxTone - 1 - tone - TrackOffset) * TrackHeight;
                        using (context.PushTransform(Matrix.CreateTranslation(
                            Bounds.Width - 7 - text.Width, rowY + (TrackHeight - text.Height) / 2))) {
                            text.Draw(context, new Point());
                        }
                    }
                }
            }
            context.DrawRectangle(ThemeManager.NeutralAccentBrushSemi, null,
                new Rect(Math.Max(0, Bounds.Width - 1), 0, 1, Bounds.Height));
        }

        public TrackBackground() {
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TrackHeightProperty ||
                change.Property == TrackOffsetProperty ||
                change.Property == ForegroundProperty ||
                change.Property == KeyProperty) {
                InvalidateVisual();
            }
        }

        int mod(int a, int b){
            return (a % b + b) % b;
        }

        public override void Render(DrawingContext context) {
            if (TrackHeight == 0) {
                return;
            }
            if (IsKeyboard) {
                RenderKeyboard(context);
                return;
            }
            int track = (int)TrackOffset;
            double top = TrackHeight * (track - TrackOffset);
            string[] degreeNames;
            switch(Preferences.Default.DegreeStyle){
                case 1:
                    degreeNames = MusicMath.Solfeges;
                    break;
                case 2:
                    degreeNames = MusicMath.NumberedNotations;
                    break;
                default:
                    degreeNames = Enumerable.Repeat("", 12).ToArray();
                    break;
            }
            while (top < Bounds.Height) {
                bool isAltTrack = IsAltTrack(track) ^ (ThemeManager.IsDarkMode && !IsKeyboard);
                bool isCenterKey = IsKeyboard && IsCenterKey(track);
                var brush = isCenterKey ? ThemeManager.CenterKeyBrush
                    : IsKeyboard ? (isAltTrack ? ThemeManager.BlackKeyBrush : ThemeManager.WhiteKeyBrush)
                    : isAltTrack ? Foreground : Background;
                // OpenSV rounds the keys at their front edge only: the rectangle runs past
                // the left edge, which ClipToBounds cuts off.
                var rect = IsKeyboard
                    ? new Rect(-3, (int)top, Bounds.Width + 3, TrackHeight)
                    : new Rect(0, (int)top, Bounds.Width, TrackHeight);
                context.DrawRectangle(brush, null, rect, IsKeyboard ? 3 : 0, IsKeyboard ? 3 : 0);
                if (IsKeyboard && TrackHeight >= 12) {
                    brush = isCenterKey ? ThemeManager.CenterKeyNameBrush
                        : isAltTrack ? ThemeManager.BlackKeyNameBrush
                            : ThemeManager.WhiteKeyNameBrush;
                    int tone = ViewConstants.MaxTone - 1 - track;
                    string toneName = MusicMath.GetToneName(tone);
                    var toneTextLayout = TextLayoutCache.Get(toneName, brush, 12);
                    var toneTextPosition = new Point(Bounds.Width - 4 - (int)toneTextLayout.Width, (int)(top + (TrackHeight - toneTextLayout.Height) / 2));
                    using (var state = context.PushTransform(Matrix.CreateTranslation(toneTextPosition))) {
                        toneTextLayout.Draw(context, new Point());
                    }
                    //scale degree display
                    int degree = mod(tone - Key, 12);
                    string degreeName = degreeNames[degree];
                    var degreeTextLayout = TextLayoutCache.Get(degreeName, brush, 12);
                    var degreeTextPosition = new Point(4, (int)(top + (TrackHeight - degreeTextLayout.Height) / 2));
                    using (var state = context.PushTransform(Matrix.CreateTranslation(degreeTextPosition))) {
                        degreeTextLayout.Draw(context, new Point());
                    }
                }
                track++;
                top += TrackHeight;
            }
        }

        private bool IsAltTrack(int track) {
            if (!IsPianoRoll) {
                return track % 2 == 1;
            }
            int tone = ViewConstants.MaxTone - 1 - track;
            if (tone < 0) {
                return false;
            }
            return MusicMath.IsBlackKey(tone);
        }

        private bool IsCenterKey(int track) {
            int tone = ViewConstants.MaxTone - 1 - track;
            return MusicMath.IsCenterKey(tone);
        }
    }
}
