using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    public partial class TrackHeader : UserControl, IDisposable {
        public static readonly DirectProperty<TrackHeader, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackHeader, Point> OffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, Point>(
                nameof(Offset),
                o => o.Offset,
                (o, v) => o.Offset = v);
        public static readonly DirectProperty<TrackHeader, int> TrackNoProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, int>(
                nameof(TrackNo),
                o => o.TrackNo,
                (o, v) => o.TrackNo = v);

        public double TrackHeight {
            get => trackHeight;
            set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public Point Offset {
            get => offset;
            set => SetAndRaise(OffsetProperty, ref offset, value);
        }
        public int TrackNo {
            get => trackNo;
            set => SetAndRaise(TrackNoProperty, ref trackNo, value);
        }

        private double trackHeight;
        private Point offset;
        private int trackNo;
        private readonly KeyModifiers cmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        public TrackHeaderViewModel? ViewModel;

        private List<IDisposable> unbinds = new List<IDisposable>();

        private UTrack? track;
        private TrackHeaderCanvas? canvas;

        public TrackHeader() {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == OffsetProperty ||
                change.Property == TrackNoProperty ||
                change.Property == TrackHeightProperty) {
                SetPosition();
            }
        }

        internal void Bind(UTrack track, TrackHeaderCanvas canvas) {
            this.track = track;
            this.canvas = canvas;
            unbinds.Add(this.Bind(TrackHeightProperty, canvas.GetObservable(TrackHeaderCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(HeightProperty, canvas.GetObservable(TrackHeaderCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(OffsetProperty, canvas.WhenAnyValue(x => x.TrackOffset, trackOffset => new Point(0, -trackOffset * TrackHeight))));
            SetPosition();
        }

        private void SetPosition() {
            Canvas.SetLeft(this, 0);
            Canvas.SetTop(this, Offset.Y + (track?.TrackNo ?? 0) * trackHeight);
            if (ViewModel != null) {
                // The voicebank line and the fx button fold away on short tracks.
                ViewModel.IsSingerVisible = trackHeight >= ViewConstants.TrackHeightDelta * 3;
                ViewModel.IsFxVisible = trackHeight >= ViewConstants.TrackHeightDelta * 3;
            }
        }

        void HeaderPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
                track == null ||
                canvas?.DataContext is not TracksViewModel tracksViewModel) {
                return;
            }
            if (args.KeyModifiers == KeyModifiers.Shift) {
                tracksViewModel.SelectTracksUntil(track);
            } else if (args.KeyModifiers == cmdKey) {
                tracksViewModel.ToggleSelectTrack(track);
            } else {
                tracksViewModel.SelectTrack(track);
            }
        }

        void TrackNameButtonClicked(object sender, RoutedEventArgs args) {
            ViewModel?.Rename();
            args.Handled = true;
        }

        void SingerButtonClicked(object sender, RoutedEventArgs args) {
            args.Handled = true;
            ShowSingerFlyout(sender);
        }

        void SingerButtonContextRequested(object sender, ContextRequestedEventArgs args) {
            args.Handled = true;
            ShowSingerFlyout(sender);
        }

        void ShowSingerFlyout(object sender) {
            if (ViewModel == null) {
                return;
            }
            try {
                Control anchor = SingerButton;
                var viewModel = new SingerFlyoutViewModel(() => ViewModel.Singer, ViewModel.SelectSingerCommand);
                var content = new SingerFlyout() { DataContext = viewModel };
                var flyout = new Flyout() {
                    Content = content,
                    Placement = PlacementMode.BottomEdgeAlignedLeft,
                    ShowMode = FlyoutShowMode.Standard,
                };
                flyout.FlyoutPresenterClasses.Add("singerFlyout");
                viewModel.CloseRequested += flyout.Hide;
                flyout.Opened += (_, _) => DocManager.Inst.AddSubscriber(viewModel);
                flyout.Closed += (_, _) => DocManager.Inst.RemoveSubscriber(viewModel);
                content.FitToScreen(anchor);
                flyout.ShowAt(anchor);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        // The renderer list depends on the singer, so it is rebuilt when the menu opens.
        void TrackContextMenuOpening(object? sender, CancelEventArgs args) {
            ViewModel?.RefreshRenderers();
        }

        async void TrackSettingsButtonClicked(object sender, RoutedEventArgs args) {
            if (track?.Singer != null && track.Singer.Found) {
                if (TopLevel.GetTopLevel(this) is Window window) {
                    var dialog = new Views.TrackSettingsDialog(track);
                    await dialog.ShowDialog(window);
                }
            }
        }

        private void MoveHandleScrolled(object? sender, PointerWheelEventArgs e) {
            Dispatcher.UIThread.Post(() => MoveHandleMoved(sender, e));
        }

        private void MoveHandlePressed(object? sender, PointerPressedEventArgs e) {
            var control = sender as Control;
            var pointer = e.GetCurrentPoint(control);
            if (control != null && pointer.Properties.IsLeftButtonPressed) {
                e.Pointer.Capture(control);
            }
        }

        private void MoveHandleReleased(object? sender, PointerReleasedEventArgs e) {
            if (e.InitialPressMouseButton == MouseButton.Left && canvas != null) {
                Cursor = null;
                Point point = e.GetPosition(canvas);
                int tracksMaxIdx = DocManager.Inst.Project.tracks.Count - 1;
                double sizeHidden = Math.Abs(Offset.Y);
                if (track != null) {
                    bool isAboveCenter = Canvas.GetTop(this) + TrackHeight / 2 > point.Y;
                    int position = (int) Math.Round((sizeHidden + point.Y - (isAboveCenter ? 0 : TrackHeight)) / TrackHeight);
                    int idx = Math.Clamp(position, 0, tracksMaxIdx);

                    if (track.TrackNo != idx) {
                        DocManager.Inst.StartUndoGroup("command.track.order");
                        DocManager.Inst.ExecuteCmd(new SetTrackNoCommand(DocManager.Inst.Project, track, idx));
                        DocManager.Inst.EndUndoGroup();
                    }

                }
                if (canvas.TrackMover != null) {
                    canvas.TrackMover.IsVisible = false;
                }
            }
            e.Pointer.Capture(null);
        }

        private void MoveHandleMoved(object? sender, PointerEventArgs e) {
            var control = sender as Control;
            var pointer = e.GetCurrentPoint(control);
            if (pointer.Properties.IsLeftButtonPressed && e.Pointer.Captured == control && canvas?.TrackMover != null) {
                canvas.TrackMover.IsVisible = true;
                Cursor = ViewConstants.cursorSizeNS;
                double maxHeight = DocManager.Inst.Project.tracks.Count * TrackHeight;
                Point point = e.GetPosition(canvas);
                double offset = Math.Abs(Offset.Y);
                double leftOver = offset % TrackHeight;
                if (track != null) {
                    double position = Math.Round((point.Y + leftOver) / TrackHeight) * TrackHeight;
                    double finalPos = Math.Clamp(position - leftOver, -leftOver, maxHeight - offset);
                    Canvas.SetTop(canvas.TrackMover, finalPos);
                }
            }
        }

        public void Dispose() {
            unbinds.ForEach(u => u.Dispose());
            unbinds.Clear();
        }
    }
}
