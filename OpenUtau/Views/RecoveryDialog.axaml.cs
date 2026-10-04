using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUtau.App.Views {
    public partial class RecoveryDialog : Window {
        public RecoveryDialog() { InitializeComponent(); }
        void RestoreRecoveryClick(object? sender, RoutedEventArgs args) => Close(true);
        void DeclineRecoveryClick(object? sender, RoutedEventArgs args) => Close(false);
    }
}
