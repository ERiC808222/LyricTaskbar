using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using DesktopLyrics.Models;
using DesktopLyrics.Services;
using DesktopLyrics.Utils;
using DesktopLyrics.ViewModels;
using DesktopLyrics.Views;
using Microsoft.Win32;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace DesktopLyrics
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;

        private NotifyIcon? _notifyIcon;
        private Icon? _appIcon;
        private TaskbarLyricsWindow? _lyricsWindow;
        private LyricsViewModel? _viewModel;
        private MediaSessionService? _mediaService;
        private LyricsService? _lyricsService;
        private SettingsService? _settingsService;
        private System.Threading.Mutex? _instanceMutex;
        private TimingSettingsWindow? _timingWindow;
        private AutoStartService? _autoStartService;



        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (e.Args.Length > 0 && !AutoStartService.IsStartupInvocation(e.Args))
            {
                try { await Diagnostics.RunAsync(e.Args); Shutdown(0); }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "diagnostics-error.txt"), ex.ToString());
                    Shutdown(1);
                }
                return;
            }

            // Retain the legacy mutex so old and renamed versions cannot run together.
            _instanceMutex = new System.Threading.Mutex(true, "Local\\LyricBarJP-Personal", out var created);
            if (!created) { Shutdown(); return; }

            // Global exception protection
            this.DispatcherUnhandledException += (s, args) =>
            {
                System.IO.Directory.CreateDirectory(SettingsService.DataFolder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsService.DataFolder, "errors.log"), DateTime.Now + " " + args.Exception + Environment.NewLine);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) => { };

            // Automatically hide any console window immediately
            var consoleHwnd = GetConsoleWindow();
            if (consoleHwnd != IntPtr.Zero)
            {
                ShowWindow(consoleHwnd, SW_HIDE);
            }

            _settingsService = new SettingsService();
            _autoStartService = new AutoStartService();
            try { _autoStartService.RefreshRegisteredPath(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AutoStart] {ex.Message}"); }
            _lyricsService = new LyricsService();
            _mediaService = new MediaSessionService();
            // Automatic update prompts remain disabled for this derivative.
            _viewModel = new LyricsViewModel(_mediaService, _lyricsService, _settingsService);

            _lyricsWindow = new TaskbarLyricsWindow(_viewModel);
            _lyricsWindow.Show();

            EnsureWindowVisibleOnScreen();

            // Multi-monitor disconnect / resolution change protection
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            SetupTrayIcon();



            await _mediaService.InitializeAsync();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(EnsureWindowVisibleOnScreen);
        }

        private void EnsureWindowVisibleOnScreen()
        {
            _lyricsWindow?.EnsureWindowOnScreen();
        }



        private void SetupTrayIcon()
        {
            _appIcon = CreateAppIcon();
            _notifyIcon = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "LyricTaskbar - 多语言双语歌词",
                Visible = true
            };

            RebuildContextMenu();

            _notifyIcon.DoubleClick += (s, e) =>
            {
                _viewModel?.ToggleLock();
                RebuildContextMenu();
            };
        }

        private void RebuildContextMenu()
        {
            if (_notifyIcon == null) return;

            var contextMenu = new ContextMenuStrip();
            if (_viewModel?.Language == AppLanguage.Tibetan) contextMenu.Font = new Font("Microsoft Himalaya", 16);
            else if (_viewModel?.Language == AppLanguage.Korean) contextMenu.Font = new Font("Malgun Gothic", 9);

            var isLocked = _viewModel?.IsLocked ?? true;
            var lockMenuItem = new ToolStripMenuItem(isLocked ? I18n.LockPosition : I18n.UnlockPosition)
            {
                Checked = isLocked
            };
            lockMenuItem.Click += (s, e) =>
            {
                _viewModel?.ToggleLock();
                var lockedNow = _viewModel?.IsLocked ?? true;
                lockMenuItem.Checked = lockedNow;
                lockMenuItem.Text = lockedNow ? I18n.LockPosition : I18n.UnlockPosition;
            };

            var dualLineMenuItem = new ToolStripMenuItem(I18n.DualLineLyrics)
            {
                Checked = _viewModel?.IsDualLine ?? false
            };
            dualLineMenuItem.Click += (s, e) =>
            {
                _viewModel?.ToggleDualLine();
                dualLineMenuItem.Checked = _viewModel?.IsDualLine ?? false;
            };

            // Theme Selection Submenu
            var themeMenu = new ToolStripMenuItem(I18n.ThemeMenu);
            var autoThemeItem = new ToolStripMenuItem(I18n.ThemeAuto)
            {
                Checked = (_viewModel?.Theme == AppTheme.Auto)
            };
            var darkThemeItem = new ToolStripMenuItem(I18n.ThemeDark)
            {
                Checked = (_viewModel?.Theme == AppTheme.Dark)
            };
            var lightThemeItem = new ToolStripMenuItem(I18n.ThemeLight)
            {
                Checked = (_viewModel?.Theme == AppTheme.Light)
            };

            autoThemeItem.Click += (s, e) =>
            {
                _viewModel?.SetTheme(AppTheme.Auto);
                autoThemeItem.Checked = true;
                darkThemeItem.Checked = false;
                lightThemeItem.Checked = false;
            };
            darkThemeItem.Click += (s, e) =>
            {
                _viewModel?.SetTheme(AppTheme.Dark);
                autoThemeItem.Checked = false;
                darkThemeItem.Checked = true;
                lightThemeItem.Checked = false;
            };
            lightThemeItem.Click += (s, e) =>
            {
                _viewModel?.SetTheme(AppTheme.Light);
                autoThemeItem.Checked = false;
                darkThemeItem.Checked = false;
                lightThemeItem.Checked = true;
            };

            themeMenu.DropDownItems.Add(autoThemeItem);
            themeMenu.DropDownItems.Add(darkThemeItem);
            themeMenu.DropDownItems.Add(lightThemeItem);

            var langMenu = new ToolStripMenuItem(I18n.LanguageMenu);
            foreach (var language in Enum.GetValues<AppLanguage>())
            {
                var item = new ToolStripMenuItem(I18n.LanguageName(language)) { Checked = _viewModel?.Language == language };
                if (language == AppLanguage.Tibetan) item.Font = new Font("Microsoft Himalaya", 15);
                item.Click += (s, e) => { _viewModel?.SetLanguage(language); RebuildContextMenu(); };
                langMenu.DropDownItems.Add(item);
            }

            // Multi-Monitor Selection Submenu
            var monitorMenu = new ToolStripMenuItem(I18n.DisplayMenu);
            contextMenu.Opening += (s, e) => PopulateMonitorMenu(monitorMenu);

            var refreshMenuItem = new ToolStripMenuItem(I18n.RefreshPlayback, null, async (s, e) =>
            {
                if (_viewModel != null && _lyricsWindow != null)
                    await RefreshLyricsFromTrayAsync(_lyricsWindow, () => _viewModel.ReloadLyricsAsync());
            });

            var resetPosMenuItem = new ToolStripMenuItem(I18n.ResetPosition, null, (s, e) =>
            {
                _lyricsWindow?.RestoreAfterTrayAction(reset: true);
            });

            var aboutMenuItem = new ToolStripMenuItem(I18n.AboutMenu, null, (s, e) =>
            {
                MessageBox.Show(
                    I18n.AboutDialog(UpdateCheckService.CurrentVersion),
                    "LyricTaskbar",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });

            var exitMenuItem = new ToolStripMenuItem(I18n.ExitMenu, null, (s, e) =>
            {
                ExitApplication();
            });

            var translationItem = new ToolStripMenuItem(I18n.ShowTranslation) { Checked = _viewModel?.ShowTranslation ?? true };
            translationItem.Click += (s, e) => { _viewModel?.ToggleTranslation(); RebuildContextMenu(); };
            var importItem = new ToolStripMenuItem(I18n.ImportLyrics, null, (s, e) => ImportLocalLyrics());
            var removeItem = new ToolStripMenuItem(I18n.RemoveLocal, null, async (s, e) =>
            {
                if (_viewModel != null) await _viewModel.ReloadLyricsAsync(removeLocal: true);
            });
            var sourceStatus = new ToolStripMenuItem(_viewModel?.SourceStatus ?? I18n.Idle) { Enabled = false };
            contextMenu.Opening += (s, e) => sourceStatus.Text = _viewModel?.SourceStatus ?? I18n.Idle;
            var sourceMenu = new ToolStripMenuItem(I18n.LyricsSourceMenu);
            foreach (var mode in Enum.GetValues<LyricsSourceMode>())
            {
                var item = new ToolStripMenuItem(I18n.SourceModeName(mode)) { Checked = _settingsService?.Settings.LyricsSource == mode };
                item.Click += async (s, e) =>
                {
                    if (_viewModel != null) await _viewModel.SetLyricsSourceAsync(mode);
                    RebuildContextMenu();
                };
                sourceMenu.DropDownItems.Add(item);
            }
            contextMenu.Items.Add(sourceStatus);
            contextMenu.Items.Add(sourceMenu);
            var lyricLanguageMenu = new ToolStripMenuItem(I18n.LyricsLanguageMenu);
            foreach (var language in Enum.GetValues<LyricsLanguageMode>())
            {
                var item = new ToolStripMenuItem(I18n.LyricsLanguageName(language)) { Checked = _settingsService?.Settings.LyricsLanguage == language };
                if (language == LyricsLanguageMode.Tibetan) item.Font = new Font("Microsoft Himalaya", 15);
                item.Click += async (s, e) => { if (_viewModel != null) await _viewModel.SetLyricsLanguageAsync(language); RebuildContextMenu(); };
                lyricLanguageMenu.DropDownItems.Add(item);
            }
            contextMenu.Items.Add(lyricLanguageMenu);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(lockMenuItem);
            contextMenu.Items.Add(dualLineMenuItem);
            contextMenu.Items.Add(translationItem);
            contextMenu.Items.Add(new ToolStripMenuItem(I18n.DisplayTiming, null, (s, e) =>
            {
                if (_viewModel == null) return;
                if (_timingWindow != null) { _timingWindow.Activate(); return; }
                _timingWindow = new TimingSettingsWindow(_viewModel);
                _timingWindow.Closed += (_, _) => _timingWindow = null;
                _timingWindow.Show();
            }));
            contextMenu.Items.Add(importItem);
            contextMenu.Items.Add(removeItem);
            contextMenu.Items.Add(themeMenu);
            contextMenu.Items.Add(langMenu);
            contextMenu.Items.Add(monitorMenu);
            var autoStartItem = new ToolStripMenuItem(I18n.AutoStart) { ToolTipText = I18n.AutoStartHint };
            void RefreshAutoStartItem()
            {
                try { autoStartItem.Checked = _autoStartService?.IsEnabled ?? false; autoStartItem.Enabled = _autoStartService != null; }
                catch (Exception ex) { autoStartItem.Enabled = false; autoStartItem.ToolTipText = I18n.AutoStartFailed + " " + ex.Message; }
            }
            autoStartItem.Click += (s, e) =>
            {
                try { _autoStartService?.SetEnabled(!(_autoStartService?.IsEnabled ?? false)); }
                catch (Exception ex) { MessageBox.Show(I18n.AutoStartFailed + "\n" + ex.Message, "LyricTaskbar", MessageBoxButton.OK, MessageBoxImage.Warning); }
                RefreshAutoStartItem();
            };
            RefreshAutoStartItem();
            contextMenu.Opening += (s, e) => RefreshAutoStartItem();
            contextMenu.Items.Add(autoStartItem);
            var fullscreenItem = new ToolStripMenuItem(I18n.FullscreenTopmost)
            {
                Checked = _viewModel?.FullscreenAlwaysOnTop ?? false, ToolTipText = I18n.FullscreenTopmostHint
            };
            fullscreenItem.Click += (s, e) => { _viewModel?.ToggleFullscreenTopmost(); fullscreenItem.Checked = _viewModel?.FullscreenAlwaysOnTop ?? false; };
            contextMenu.Items.Add(fullscreenItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(refreshMenuItem);
            contextMenu.Items.Add(resetPosMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add(aboutMenuItem);
            contextMenu.Items.Add(exitMenuItem);

            var previousMenu = _notifyIcon.ContextMenuStrip;
            _notifyIcon.ContextMenuStrip = contextMenu;
            previousMenu?.Dispose();
        }

        private void ImportLocalLyrics()
        {
            if (_mediaService?.CurrentTrack.IsEmpty != false)
            {
                MessageBox.Show(I18n.PlayFirst, "LyricTaskbar"); return;
            }
            try
            {
                using var original = new System.Windows.Forms.OpenFileDialog { Title = I18n.SelectOriginal, Filter = "LRC (*.lrc)|*.lrc|Text (*.txt)|*.txt" };
                if (original.ShowDialog() != DialogResult.OK) return;
                string? translation = null;
                using var translated = new System.Windows.Forms.OpenFileDialog { Title = I18n.SelectTranslation, Filter = "LRC (*.lrc)|*.lrc|Text (*.txt)|*.txt" };
                if (translated.ShowDialog() == DialogResult.OK) translation = LocalLyricsStore.ReadLrcFile(translated.FileName);
                _viewModel?.ImportLyrics(LocalLyricsStore.ReadLrcFile(original.FileName), translation);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "LyricTaskbar", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void PopulateMonitorMenu(ToolStripMenuItem monitorMenu)
        {
            monitorMenu.DropDownItems.Clear();
            var allScreens = DisplayPlacement.GetDisplays();

            for (int i = 0; i < allScreens.Count; i++)
            {
                var screen = allScreens[i];
                var screenIndex = i + 1;
                var label = I18n.DisplayItem(screenIndex, screen.Primary, screen.Bounds.Width, screen.Bounds.Height);

                var item = new ToolStripMenuItem(label);

                // Check if current window is on this screen
                if (_lyricsWindow != null)
                {
                    var rect = _lyricsWindow.NativeBounds;
                    item.Checked = screen.Bounds.Contains(new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2,
                        rect.Left + rect.Width / 2 + 1, rect.Top + rect.Height / 2 + 1));
                }

                var targetScreen = screen;
                item.Click += (s, e) =>
                {
                    if (_lyricsWindow != null)
                    {
                        _lyricsWindow.ResetTaskbarPosition(targetScreen);
                    }
                };

                monitorMenu.DropDownItems.Add(item);
            }
        }

        internal static async System.Threading.Tasks.Task RefreshLyricsFromTrayAsync(TaskbarLyricsWindow window, Func<System.Threading.Tasks.Task> reload)
        {
            window.RestoreAfterTrayAction();
            try { await reload(); }
            finally { window.RestoreAfterTrayAction(); }
        }

        internal static Icon CreateAppIcon()
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream("DesktopLyrics.Assets.App.ico")
                ?? throw new InvalidOperationException("Missing application icon resource.");
            using var icon = new Icon(stream, new System.Drawing.Size(32, 32));
            return (Icon)icon.Clone();
        }

        private void ExitApplication()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _notifyIcon?.Dispose();
            _lyricsWindow?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _notifyIcon?.Dispose();
            _appIcon?.Dispose();
            _appIcon = null;
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
