using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Colors;
using OpenUtau.Core;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// The settings UI, docked in the main window's right-hand panel. Everything
    /// that needs a parent for a dialog resolves the host window from the tree,
    /// so the panel stays host-agnostic.
    /// </summary>
    public partial class PreferencesPanel : UserControl {
        private PreferencesViewModel? viewModel => this.DataContext as PreferencesViewModel;

        private Window? HostWindow => TopLevel.GetTopLevel(this) as Window;

        public PreferencesPanel() {
            InitializeComponent();
        }

        void OnMetronomeSliderPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (sender is not Slider slider || viewModel == null) {
                return;
            }
            var point = e.GetCurrentPoint(slider);
            if (!point.Properties.IsRightButtonPressed) {
                return;
            }
            switch (slider.Tag as string) {
                case "MetronomeVolume":
                    viewModel.ResetMetronomeVolume();
                    break;
                case "MetronomeHighFrequency":
                    viewModel.ResetMetronomeHighFrequency();
                    break;
                case "MetronomeLowFrequency":
                    viewModel.ResetMetronomeLowFrequency();
                    break;
            }
            e.Handled = true;
        }

        void OpenSingersFolder(object sender, RoutedEventArgs e) {
            try {
                Directory.CreateDirectory(viewModel!.SingerPath);
                OS.OpenFolder(viewModel!.SingerPath);
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OpenAddlSingersFolder(object sender, RoutedEventArgs e) {
            try {
                if (Directory.Exists(viewModel!.AdditionalSingersPath)) {
                    OS.OpenFolder(viewModel!.AdditionalSingersPath);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void ResetAddlSingersPath(object sender, RoutedEventArgs e) {
            viewModel!.SetAddlSingersPath(string.Empty);
        }

        async void SelectAddlSingersPath(object sender, RoutedEventArgs e) {
            if (HostWindow is not { } window) {
                return;
            }
            var path = await FilePicker.OpenFolderAboutSinger(window, "prefs.paths.addlsinger");
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (Directory.Exists(path)) {
                viewModel!.SetAddlSingersPath(path);
            }
        }

        async void ReloadSingers(object sender, RoutedEventArgs e) {
            if (HostWindow is not { } window) {
                return;
            }
            LoadingWindow.BeginLoading(window);
            await Task.Run(() => {
                SingerManager.Inst.SearchAllSingers();
            });
            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            LoadingWindow.EndLoading();
        }

        void ResetVLabelerPath(object sender, RoutedEventArgs e) {
            viewModel!.SetVLabelerPath(string.Empty);
        }

        async void SelectVLabelerPath(object sender, RoutedEventArgs e) {
            if (HostWindow is not { } window) {
                return;
            }
            var type = OS.IsWindows() ? FilePicker.EXE : OS.IsMacOS() ? FilePicker.APP : FilePickerFileTypes.All;
            var path = await FilePicker.OpenFile(window, "prefs.advanced.vlabelerpath", type);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (OS.AppExists(path)) {
                viewModel!.SetVLabelerPath(path);
            }
        }

        void ResetSetParamPath(object sender, RoutedEventArgs e) {
            viewModel!.SetSetParamPath(string.Empty);
        }

        async void SelectSetParamPath(object sender, RoutedEventArgs e) {
            if (HostWindow is not { } window) {
                return;
            }
            var path = await FilePicker.OpenFile(window, "prefs.otoeditor.setparampath", FilePicker.EXE);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (File.Exists(path)) {
                viewModel!.SetSetParamPath(path);
            }
        }

        void ResetWinePath(object sender, RoutedEventArgs e) {
            viewModel!.SetWinePath(string.Empty);
        }

        async void SelectWinePath(object sender, RoutedEventArgs e) {
            if (HostWindow is not { } window) {
                return;
            }
            var path = await FilePicker.OpenFile(window, "prefs.advanced.winepath", FilePicker.UnixExecutable);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (File.Exists(path)) {
                viewModel!.SetWinePath(path);
            }
        }

        void DetectWinePath(object sender, RoutedEventArgs e) {
            string[] wineNames = { "wine", "wine64", "wine32", "wine32on64" };
            string winePath = string.Empty;

            foreach (string wineName in wineNames) {
                winePath = OS.WhereIs(wineName);
                if (!string.IsNullOrEmpty(winePath)) {
                    break;
                }
            }

            if (string.IsNullOrEmpty(winePath)) {
                return;
            }

            viewModel!.SetWinePath(winePath);
        }

        void OpenCustomThemeEditor(object sender, RoutedEventArgs e) {
            if (viewModel == null || CustomTheme.IsPackageTheme(viewModel.ThemeName)) return;
            ThemeEditorWindow.Show(CustomTheme.Themes[viewModel.ThemeName]);
        }

        void OnCustomThemeCreate(object sender, RoutedEventArgs e) {
            if (HostWindow is not { } window || viewModel == null) {
                return;
            }
            var dialog = new TypeInDialog {
                Title = ThemeManager.GetString("prefs.appearance.customtheme.create.title")
            };
            dialog.SetPrompt(ThemeManager.GetString("prefs.appearance.customtheme.create.prompt"));
            dialog.onFinish = s => {
                if (string.IsNullOrEmpty(s)) {
                    MessageBox.ShowModal(window,
                        ThemeManager.GetString("prefs.appearance.customtheme.create.empty"),
                        ThemeManager.GetString("prefs.appearance.customtheme.create.title"));
                    return;
                }

                string filename = string.Join("", s.Where(c => Char.IsLetterOrDigit(c) || c == ' '))
                                        .Replace(" ", "-").ToLower() + ".yaml";

                var themeYaml = new CustomTheme.ThemeYaml { Name = s };

                File.WriteAllText(Path.Join(PathManager.Inst.ThemesPath, filename),
                    Yaml.DefaultSerializer.Serialize(themeYaml));
                viewModel.RefreshThemes();
            };
            dialog.ShowDialog(window);
        }

        async void OnCustomThemeDelete(object sender, RoutedEventArgs e) {
            if (HostWindow is not { } window || viewModel == null) return;
            if (CustomTheme.IsPackageTheme(viewModel.ThemeName)) return;
            var result = await MessageBox.Show(
                window,
                ThemeManager.GetString("prefs.appearance.customtheme.delete.message"),
                ThemeManager.GetString("prefs.appearance.customtheme.delete.title"),
                MessageBox.MessageBoxButtons.YesNo);
            if (result == MessageBox.MessageBoxResult.Yes) {
                string previousTheme = viewModel.ThemeItems.TakeWhile(x => x != viewModel.ThemeName).LastOrDefault()!;
                File.Delete(CustomTheme.Themes[viewModel.ThemeName]);
                viewModel.RefreshThemes();
                viewModel.ThemeName = previousTheme;
            }
        }
    }
}
