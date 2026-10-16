using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopLyrics.Utils;
using DesktopLyrics.ViewModels;
using DesktopLyrics.Services;

namespace DesktopLyrics.Views
{
    public partial class TaskbarLyricsWindow : Window
    {
        public LyricsViewModel ViewModel { get; }
        private HwndSource? _hwndSource;
        private readonly DispatcherTimer _topmostGuardTimer;
        private bool _hiddenForFullscreen;
        private bool _positionInitialized;
        private bool _placingWindow;
        private long _lastTopmostRaise;
        internal Func<bool>? FullscreenProbeOverride { get; set; }
        internal Func<IReadOnlyList<DesktopDisplay>>? DisplaysOverride { get; set; }
        internal bool PreserveOffscreenForDiagnostics { get; set; }

        public IReadOnlyList<DesktopDisplay> Displays => DisplaysOverride?.Invoke() ?? DisplayPlacement.GetDisplays();
        public PixelBounds NativeBounds => Win32Helper.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect) ? PixelBounds.From(rect) : default;


        public TaskbarLyricsWindow(LyricsViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = ViewModel;

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            SourceInitialized += OnSourceInitialized;
            Loaded += OnWindowLoaded;
            MouseEnter += OnWindowMouseEnter;
            MouseLeave += OnWindowMouseLeave;

            // Check full-screen visibility promptly; raise the visible window at most once a second.
            _topmostGuardTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _topmostGuardTimer.Tick += (s, e) =>
            {
                UpdateFullscreenVisibility();
            };
            _topmostGuardTimer.Start();
        }

        private void OnWindowMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            HoverControlsPanel.IsHitTestVisible = true;
        }

        private void OnWindowMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            HoverControlsPanel.IsHitTestVisible = false;
        }

        private async void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.PreviousTrackAsync();
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.TogglePlayPauseAsync();
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.NextTrackAsync();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);

            // Attach window to Taskbar (Shell_TrayWnd) as owner
            Win32Helper.AttachToTaskbar(this);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Prevent taking focus when clicked
            if (msg == Win32Helper.WM_MOUSEACTIVATE)
            {
                handled = true;
                return (IntPtr)Win32Helper.MA_NOACTIVATE;
            }

            // Ensure window maintains topmost Z-order when other windows reorder
            if (msg == Win32Helper.WM_WINDOWPOSCHANGING && !_hiddenForFullscreen)
            {
                try
                {
                    var pos = Marshal.PtrToStructure<Win32Helper.WINDOWPOS>(lParam);
                    pos.hwndInsertAfter = Win32Helper.HWND_TOPMOST;
                    Marshal.StructureToPtr(pos, lParam, true);
                }
                catch { }
            }

            return IntPtr.Zero;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (!_positionInitialized) { PositionWindowOnTaskbar(); _positionInitialized = true; }
            ApplyLockState();
        }

        private void UpdateFullscreenVisibility(bool forceTopmost = false)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            bool fullscreen;
            try { fullscreen = FullscreenProbeOverride?.Invoke() ?? FullscreenService.ForegroundCoversLyricsDisplay(hwnd); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); fullscreen = false; }
            bool hide = FullscreenPolicy.ShouldHide(ViewModel.FullscreenAlwaysOnTop, fullscreen, sameDisplay: true);
            if (hide)
            {
                if (!_hiddenForFullscreen) { _hiddenForFullscreen = true; Topmost = false; Hide(); }
                return;
            }
            if (_hiddenForFullscreen)
            {
                _hiddenForFullscreen = false;
                Topmost = true;
                Show(); // ShowActivated=false: restoring lyrics never takes focus.
                forceTopmost = true;
            }
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (IsVisible && (forceTopmost || (now - _lastTopmostRaise) / (double)System.Diagnostics.Stopwatch.Frequency >= 1))
            {
                _lastTopmostRaise = now;
                Win32Helper.EnsureTopMost(this);
            }
        }

        private void PositionWindowOnTaskbar()
        {
            var settings = ViewModel.Settings;
            _placingWindow = true;
            try
            {
                if (double.IsFinite(settings.Left) && double.IsFinite(settings.Top) && (settings.Left != -1 || settings.Top != -1))
                {
                    Left = settings.Left;
                    Top = settings.Top;
                    Width = double.IsFinite(settings.Width) && settings.Width > 200 ? settings.Width : 520;
                    Height = double.IsFinite(settings.Height) && settings.Height > 20 ? settings.Height : 40;
                }
                else ResetTaskbarPosition();
            }
            finally { _placingWindow = false; }
            if (!PreserveOffscreenForDiagnostics) EnsureWindowOnScreen();
        }

        public void ResetTaskbarPosition(DesktopDisplay? target = null)
        {
            var displays = Displays;
            target ??= displays.FirstOrDefault(display => display.Primary) ?? displays.FirstOrDefault();
            if (target == null) return;
            var placement = DisplayPlacement.OnTaskbar(target);
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            _placingWindow = true;
            try
            {
                // SetWindowPos works in native pixels under the PerMonitorV2 manifest,
                // including negative monitor origins and different scale factors.
                Win32Helper.AttachToTaskbar(this);
                if (!Win32Helper.SetWindowPos(hwnd, Win32Helper.HWND_TOPMOST, placement.Left, placement.Top,
                    placement.Width, placement.Height, Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { _placingWindow = false; }
            ViewModel.SavePosition(Left, Top, Width, Height);
            RestoreVisibility();
        }

        public void EnsureWindowOnScreen()
        {
            if (!DisplayPlacement.IsVisible(NativeBounds, Displays)) ResetTaskbarPosition();
        }

        public void RestoreAfterTrayAction(bool reset = false)
        {
            if (reset) ResetTaskbarPosition();
            else EnsureWindowOnScreen();
            RestoreVisibility();
        }

        private void RestoreVisibility()
        {
            // Owner/taskbar recreation or a hidden native HWND must not leave an invisible
            // WPF window behind. Preserve the user's intentional full-screen policy.
            Win32Helper.AttachToTaskbar(this);
            UpdateFullscreenVisibility(forceTopmost: true);
            if (!_hiddenForFullscreen)
            {
                if (!IsVisible) Show();
                Win32Helper.EnsureTopMost(this);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.IsLocked))
            {
                ApplyLockState();
            }
            else if (e.PropertyName == nameof(ViewModel.FullscreenAlwaysOnTop)) UpdateFullscreenVisibility(forceTopmost: true);
        }

        private void ApplyLockState()
        {
            Dispatcher.Invoke(() =>
            {
                // We keep NOACTIVATE + TOOLWINDOW + TOPMOST so hover buttons are clickable without stealing focus
                if (!_hiddenForFullscreen) Win32Helper.SetClickThrough(this, false);
                UpdateFullscreenVisibility(forceTopmost: true);
            });
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!ViewModel.IsLocked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                ViewModel.SavePosition(Left, Top, Width, Height);
            }
        }

        private void Window_LocationChanged(object? sender, EventArgs e)
        {
            if (_positionInitialized && !_placingWindow && !ViewModel.IsLocked)
            {
                ViewModel.SavePosition(Left, Top, Width, Height);
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_positionInitialized && !_placingWindow && !ViewModel.IsLocked)
            {
                ViewModel.SavePosition(Left, Top, Width, Height);
            }
        }

        private void RightResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!ViewModel.IsLocked)
            {
                double newWidth = Width + e.HorizontalChange;
                if (newWidth >= 280 && newWidth <= SystemParameters.PrimaryScreenWidth)
                {
                    Width = newWidth;
                }
            }
        }

        private void RightResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (!ViewModel.IsLocked)
            {
                ViewModel.SavePosition(Left, Top, Width, Height);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _topmostGuardTimer.Stop();
            _hwndSource?.RemoveHook(WndProc);
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            base.OnClosed(e);
        }
    }
}
