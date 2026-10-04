using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// The track editor docked in the main window's right-hand panel: which voicebank sings the
    /// selected track, the parameters its renderer supports, and the language it sings in.
    /// </summary>
    public partial class TrackVoicePanel : UserControl {
        public TrackVoicePanel() {
            InitializeComponent();
        }

        TrackExpressionViewModel? Row(object? sender) =>
            (sender as Control)?.DataContext as TrackExpressionViewModel;

        // Parameters are heavy to write - the command rebuilds the project's expressions and every
        // part with them - so an edit is written once, when it ends, not on every step of a drag.
        // Commit is a no-op when the value did not move.

        void OnExpressionSliderPointerReleased(object? sender, PointerReleasedEventArgs args) =>
            Row(sender)?.Commit();

        void OnExpressionSliderLostFocus(object? sender, RoutedEventArgs args) =>
            Row(sender)?.Commit();

        void OnExpressionSliderKeyUp(object? sender, KeyEventArgs args) =>
            Row(sender)?.Commit();

        void OnExpressionSliderContextRequested(object? sender, ContextRequestedEventArgs args) {
            Row(sender)?.Reset();
            args.Handled = true;
        }

        void OnExpressionTextLostFocus(object? sender, RoutedEventArgs args) =>
            Row(sender)?.CommitText();

        void OnExpressionTextKeyDown(object? sender, KeyEventArgs args) {
            if (args.Key == Key.Enter) {
                Row(sender)?.CommitText();
                args.Handled = true;
            }
        }

        void OnExpressionOptionChanged(object? sender, SelectionChangedEventArgs args) =>
            Row(sender)?.Commit();

        void OnPhonemizerButtonClicked(object? sender, RoutedEventArgs args) {
            if (sender is Control target && Resources["PhonemizersMenuRes"] is ContextMenu menu) {
                menu.Open(target);
            }
            args.Handled = true;
        }

        // The button opens the menu itself; the default context menu would be an empty second one.
        void OnPhonemizerContextRequested(object? sender, ContextRequestedEventArgs args) {
            args.Handled = true;
        }
    }
}
