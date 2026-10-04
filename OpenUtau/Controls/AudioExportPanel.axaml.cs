using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Controls {
    public partial class AudioExportPanel : UserControl {
        public AudioExportPanel() { InitializeComponent(); }
        async void ChooseExportFolder(object? sender, RoutedEventArgs args) {
            if (DataContext is not AudioExportViewModel model || TopLevel.GetTopLevel(this) is not Window window) return;
            var folder = await FilePicker.OpenFolder(window, "menu.file.exportwavto", model.DestinationFolder);
            if (!string.IsNullOrEmpty(folder)) { model.DestinationFolder = folder; }
        }
        async void ExportAudioClick(object? sender, RoutedEventArgs args) {
            if (DataContext is AudioExportViewModel model) { await model.ExportAsync(); }
        }
    }
}
