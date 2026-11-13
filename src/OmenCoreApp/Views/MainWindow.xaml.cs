using System.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OmenCore.Controls;
using OmenCore.Utils;
using OmenCore.ViewModels;

namespace OmenCore.Views
{
    public partial class MainWindow : Window
    {
        private bool _forceClose = false; // Flag for actual shutdown vs hide-to-tray
        private readonly HashSet<TabItem> _initializedTabs = new();
        private static readonly TimeSpan LogAutoScrollMinInterval = TimeSpan.FromMilliseconds(250);
        private bool _logAutoScrollPending;
        private DateTime _lastLogAutoScrollUtc = DateTime.MinValue;

        // Rail auto-fit: shrinks the nav tab list (text, icons, spacing - all together, via
        // one LayoutTransform) when the sidebar is shorter than the list needs, instead of
        // just leaving the list to scroll at full size. RailMinScale is the floor past which
        // it gives up shrinking further and lets the rail's own ScrollViewer take over -
        // the "unless it's obviously tiny" case.
        private FrameworkElement? _railItemsPanel;
        private ScaleTransform? _railScale;
        private const double RailMinScale = 0.72;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            OmenCore.Services.StartupTimeline.Mark("window_xaml_loaded");
            DataContext = viewModel;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            TabControlMain.SelectionChanged += TabControlMain_SelectionChanged;
            TabControlMain.SizeChanged += TabControlMain_SizeChanged;
            SystemParameters.StaticPropertyChanged += SystemParametersOnStaticPropertyChanged;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            
            // Apply Stay on Top setting from config
            Topmost = App.Configuration.Config.StayOnTop;

            // WP-33: restore the saved display scale and follow later changes (Settings row or
            // Ctrl +/-/0). This is the font-scale item WP-45 deferred: every view sets explicit
            // FontSize values, so scaling the host's layout transform is the only mechanism that
            // reaches them all at once.
            _uiScalePercent = UiScale.Normalize(App.Configuration.Config.UiScalePercent);
            SetUiScale(_uiScalePercent, persist: false);
            App.UiScaleChanged += OnUiScaleChanged;
            
            // Listen for LogBuffer changes to auto-scroll the system log
            if (viewModel is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
        
        /// <summary>
        /// Ctrl+1..9 jumps to the Nth page in the navigation rail. Counts visible pages only, so
        /// the numbers match what's on screen whether or not advanced pages are hidden. Leaves
        /// text boxes alone so Ctrl+digit still types where a field has focus.
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || e.OriginalSource is TextBox) return;

            // Ctrl +/-/0 - the same steps the Settings combo box offers (roadmap WP-33).
            int? requestedScale = e.Key switch
            {
                Key.OemPlus or Key.Add => UiScale.Next(_uiScalePercent),
                Key.OemMinus or Key.Subtract => UiScale.Previous(_uiScalePercent),
                Key.D0 or Key.NumPad0 => UiScale.Default,
                _ => null
            };
            if (requestedScale is int scalePercent)
            {
                SetUiScale(scalePercent, persist: true);
                e.Handled = true;
                return;
            }

            int number = e.Key switch
            {
                >= Key.D1 and <= Key.D9 => e.Key - Key.D1 + 1,
                >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1 + 1,
                _ => 0
            };
            if (number == 0) return;

            var visibility = new List<bool>();
            foreach (var item in TabControlMain.Items)
                visibility.Add(item is UIElement element && element.Visibility == Visibility.Visible);

            int index = ResolveNavShortcutIndex(visibility, number);
            if (index >= 0)
            {
                TabControlMain.SelectedIndex = index;
                e.Handled = true;
            }
        }

        /// <summary>Tab index for the Nth visible page (1-based), or -1 if there are fewer.</summary>
        internal static int ResolveNavShortcutIndex(IReadOnlyList<bool> tabVisibility, int number)
        {
            int seen = 0;
            for (int i = 0; i < tabVisibility.Count; i++)
            {
                if (!tabVisibility[i]) continue;
                if (++seen == number) return i;
            }
            return -1;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.LogBuffer))
            {
                ScheduleSystemLogAutoScroll();
            }
        }

        private void ScheduleSystemLogAutoScroll()
        {
            if (_logAutoScrollPending)
            {
                return;
            }

            if (DataContext is MainViewModel { LogsCollapsed: true })
            {
                return;
            }

            _logAutoScrollPending = true;
            Dispatcher.InvokeAsync(async () =>
            {
                var elapsed = DateTime.UtcNow - _lastLogAutoScrollUtc;
                if (elapsed < LogAutoScrollMinInterval)
                {
                    await Task.Delay(LogAutoScrollMinInterval - elapsed);
                }

                _logAutoScrollPending = false;
                if (SystemLogScrollViewer != null &&
                    DataContext is not MainViewModel { LogsCollapsed: true })
                {
                    SystemLogScrollViewer.ScrollToEnd();
                    _lastLogAutoScrollUtc = DateTime.UtcNow;
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            UpdateMaximizedBounds();

            // Mode colour on the chrome (roadmap 4.B): follow the General page's selected profile. Subscribed
            // here rather than in the constructor so the General view model is created in its usual order.
            if ((DataContext as MainViewModel)?.General is { } general)
            {
                general.PropertyChanged -= OnGeneralPropertyChanged;
                general.PropertyChanged += OnGeneralPropertyChanged;
            }
            ApplyModeAccent();
            
            // Initialize the OSD separately so a hotkey registration failure cannot leave
            // startup-minimized launches without their overlay service.
            (DataContext as MainViewModel)?.EnsureOsdInitialized();

            // Initialize global hotkeys.
            var windowHandle = new WindowInteropHelper(this).Handle;
            (DataContext as MainViewModel)?.InitializeHotkeys(windowHandle);

            InitializeRailAutoFit();

            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (this.TabControlMain == null)
                    {
                        return;
                    }

                    if (this.TabControlMain.SelectedIndex < 0)
                    {
                        this.TabControlMain.SelectedIndex = 0;
                    }

                    if (this.TabControlMain.SelectedItem is TabItem selectedTab)
                    {
                        EnsureTabContentCreated(selectedTab);
                    }
                }
                catch (Exception ex)
                {
                    App.Logging.Error($"[MainWindow] ERROR creating initial tab content: {ex.Message}\n{ex.StackTrace}");
                }
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// Looks up the nav rail's named template parts (the items panel and its
        /// LayoutTransform) so <see cref="ApplyRailAutoFit"/> can shrink the whole list as one
        /// unit when the sidebar is shorter than it needs. Template parts don't exist until
        /// the control has applied its template at least once.
        /// </summary>
        private void InitializeRailAutoFit()
        {
            try
            {
                TabControlMain.ApplyTemplate();
                _railItemsPanel = TabControlMain.Template?.FindName("PART_ItemsPanel", TabControlMain) as FrameworkElement;
                _railScale = TabControlMain.Template?.FindName("PART_RailScale", TabControlMain) as ScaleTransform;
                ApplyRailAutoFit();
            }
            catch (Exception ex)
            {
                App.Logging.Error($"[MainWindow] Rail auto-fit setup failed: {ex.Message}");
            }
        }

        private void TabControlMain_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Height is what matters here - the rail is a vertical list, and the sidebar
            // column's width barely moves (Grid MinWidth/MaxWidth keep it in a narrow band).
            if (e.HeightChanged)
            {
                ApplyRailAutoFit();
            }
        }

        /// <summary>
        /// Shrinks the nav tab list to fit the sidebar's available height when it doesn't fit
        /// at natural size, instead of leaving it to scroll at full size on a short window.
        /// Scales text, icons, and spacing together as one unit via the rail's LayoutTransform
        /// (not FontSize/Padding setters on individual tabs) so everything stays proportional.
        /// Clamped to <see cref="RailMinScale"/> - past that floor this stops shrinking and
        /// leaves the rail's own ScrollViewer to handle the rest, rather than shrinking the
        /// list down to an unreadable size on a very short window.
        /// </summary>
        private void ApplyRailAutoFit()
        {
            if (_railItemsPanel == null || _railScale == null)
            {
                return;
            }

            var availableHeight = TabControlMain.ActualHeight;
            if (availableHeight <= 0)
            {
                return;
            }

            // Measure the list at its natural (unscaled) size to know what it actually needs.
            _railScale.ScaleX = 1.0;
            _railScale.ScaleY = 1.0;
            _railItemsPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var naturalHeight = _railItemsPanel.DesiredSize.Height;

            if (naturalHeight <= 0 || naturalHeight <= availableHeight)
            {
                // Fits already - leave it at natural size.
                return;
            }

            var scale = Math.Max(RailMinScale, Math.Min(1.0, availableHeight / naturalHeight));
            _railScale.ScaleX = scale;
            _railScale.ScaleY = scale;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _ = sender;
            
            // Check if we should minimize to tray instead of closing
            bool minimizeToTray = App.Configuration.Config.Monitoring?.MinimizeToTrayOnClose ?? true;
            
            if (minimizeToTray && !_forceClose)
            {
                // Cancel the close and hide to tray instead
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                App.Logging.Debug("Window hidden to tray (close cancelled)");
                return;
            }
            
            // Actual close - clean up
            TabControlMain.SelectionChanged -= TabControlMain_SelectionChanged;
            TabControlMain.SizeChanged -= TabControlMain_SizeChanged;
            SystemParameters.StaticPropertyChanged -= SystemParametersOnStaticPropertyChanged;
            App.UiScaleChanged -= OnUiScaleChanged;
            if ((DataContext as MainViewModel)?.General is { } general)
                general.PropertyChanged -= OnGeneralPropertyChanged;
            (DataContext as MainViewModel)?.Dispose();
        }

        private void TabControlMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, TabControlMain))
            {
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var selectedTab = TabControlMain.SelectedItem as TabItem;
            bool created = false;

            if (selectedTab != null)
            {
                created = !_initializedTabs.Contains(selectedTab);
                EnsureTabContentCreated(selectedTab);
            }

            AnimateTabContentTransition();

            // WP-33 profile: one line per switch, so "navigation feels slow" has a number instead
            // of an impression. Views are cached per tab (see EnsureTabContentCreated), so a switch
            // normally reports "reused" - what it costs is a layout pass over an existing page.
            TabContentPresenter?.UpdateLayout();
            App.Logging.Info($"[Nav] {GetTabHeaderText(selectedTab)}: " +
                             $"{(created ? "created" : "reused")}, {stopwatch.Elapsed.TotalMilliseconds:F0} ms to layout");
        }

        private int _uiScalePercent = UiScale.Default;

        /// <summary>
        /// Walks every visible page the way a click would - by setting <c>SelectedIndex</c>, so the
        /// real handler runs (view creation, layout, the <c>[Nav]</c> line) - and then writes the
        /// collected timings to <see cref="OmenCore.Services.NavigationTimingReport.DefaultPath"/>.
        ///
        /// This exists because the cost being measured lives in the window, not in a testable
        /// service: nothing outside a running WPF app can time a tab switch. The app's
        /// <c>--nav-cycle</c> flag calls it and the CLI's <c>nav</c> verb launches the app that way,
        /// so the numbers can be collected without clicking through a dozen pages twice.
        /// </summary>
        public async Task RunNavCycleAsync(int passes)
        {
            passes = Math.Clamp(passes, 1, 20);

            var pages = new List<TabItem>();
            foreach (var item in TabControlMain.Items)
            {
                if (item is TabItem tab && tab.Visibility == Visibility.Visible)
                {
                    pages.Add(tab);
                }
            }

            App.Logging.Info($"[NavCycle] {pages.Count} visible pages, {passes} pass(es), " +
                             $"display scale {_uiScalePercent}%");

            for (int pass = 0; pass < passes; pass++)
            {
                foreach (var page in pages)
                {
                    TabControlMain.SelectedItem = page;

                    // Drain queued layout/render before switching again, so the next switch is timed
                    // against a settled page rather than against work the previous one left queued.
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            var report = OmenCore.Services.NavigationTimingReport.FromSnapshot(
                OmenCore.Services.StartupAndNavigationPerformanceTracker.GetSnapshot(), passes);
            report.Save();

            App.Logging.Info($"[NavCycle] wrote {report.Pages.Count} page timings to " +
                             OmenCore.Services.NavigationTimingReport.DefaultPath +
                             (report.Slowest is { } slowest ? $"; slowest {slowest.Page} at {slowest.AverageMs:F1} ms average" : string.Empty));
        }

        private void OnUiScaleChanged(int percent) => SetUiScale(percent, persist: false);

        /// <summary>
        /// Applies the display scale as a layout transform on the page host, and with it decides how
        /// the host measures that host's child.
        ///
        /// A layout transform (not RenderTransform) is deliberate: it changes measured size, so the
        /// host's ScrollViewer grows a scrollbar when the zoom outruns the window instead of the
        /// text spilling off the bottom edge. 100 % removes the transform entirely rather than
        /// applying an identity scale, so the normal case keeps the fast path.
        ///
        /// The scroller is only enabled above and below 100 % (see UiScale.PageScrollsAsOne): a
        /// scrolling ScrollViewer measures with infinite height, which turns a page's star-sized
        /// rows into Auto rows. Enabling it unconditionally would have quietly changed the default
        /// layout of every page that uses one to size a chart to the window.
        /// </summary>
        private void SetUiScale(int percent, bool persist)
        {
            _uiScalePercent = UiScale.Normalize(percent);
            double factor = UiScale.Factor(_uiScalePercent);

            if (TabContentPresenter != null)
            {
                TabContentPresenter.LayoutTransform = factor == 1.0
                    ? null
                    : new ScaleTransform(factor, factor);
            }

            if (PageScrollViewer != null)
            {
                PageScrollViewer.VerticalScrollBarVisibility = UiScale.PageScrollsAsOne(_uiScalePercent)
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled;
            }

            if (persist)
            {
                App.Configuration.Config.UiScalePercent = _uiScalePercent;
                App.Configuration.Save(App.Configuration.Config);
                App.Logging.Info($"[Zoom] display scale set to {_uiScalePercent}%");
            }
        }

        /// <summary>
        /// Fades the newly-selected tab's content in, instead of it just popping into place.
        /// Pillar 2.2 (docs/ROADMAP_v4.2.0.md): a small, easily-revertible first animation,
        /// gated by <see cref="MotionPreference.ShouldReduceMotion"/> like every animation this
        /// app adds must be. Deliberately triggered only from SelectionChanged - a real user
        /// tab switch or programmatic navigation - never from an unrelated property notification,
        /// which is exactly the class of bug the dashboard pulse animation had earlier this cycle
        /// (an animation restarting on every unrelated update because its trigger condition was
        /// too broad). Opacity is the one property WPF composites cheaply without forcing a
        /// layout pass, matching this pillar's "GPU-composited where possible" constraint.
        /// </summary>
        private void AnimateTabContentTransition()
        {
            if (TabContentPresenter == null)
            {
                return;
            }

            if (MotionPreference.ShouldReduceMotion(App.Configuration.Config.ReduceMotion))
            {
                // No animation: make sure a prior fade isn't left holding this at a partial
                // opacity if the preference changed mid-session.
                TabContentPresenter.BeginAnimation(UIElement.OpacityProperty, null);
                TabContentPresenter.Opacity = 1.0;
                return;
            }

            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            TabContentPresenter.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void EnsureTabContentCreated(TabItem? tab)
        {
            if (tab == null || _initializedTabs.Contains(tab))
            {
                return;
            }

            if (ReferenceEquals(tab, MonitoringTabItem))
            {
                if (tab.Content == null)
                {
                    if (DataContext is not MainViewModel viewModel)
                    {
                        return;
                    }

                    OmenCore.Services.StartupTimeline.Mark("tab_monitoring_creating");
                    tab.Content = new HardwareMonitoringDashboard
                    {
                        DataContext = viewModel
                    };
                    OmenCore.Services.StartupTimeline.Mark("tab_monitoring_created");
                    App.Logging.Info("[MainWindow] Created HardwareMonitoringDashboard for Monitoring tab");
                }

                _initializedTabs.Add(tab);
                return;
            }

            try
            {
                var content = CreateTabContent(tab);
                if (content != null)
                {
                    tab.Content = content;
                }

                _initializedTabs.Add(tab);
            }
            catch (Exception ex)
            {
                var tabName = GetTabHeaderText(tab);
                App.Logging.Error($"[MainWindow] Failed to create tab '{tabName}': {ex}");
                tab.Content = BuildTabLoadFailureContent(tabName, ex);
                _initializedTabs.Add(tab);
            }
        }

        private static string GetTabHeaderText(TabItem? tab)
        {
            if (tab == null)
            {
                return "?";
            }

            if (tab.Header is string text)
            {
                return text;
            }

            if (tab.Header is DependencyObject header)
            {
                var textBlock = FindVisualChild<TextBlock>(header);
                if (!string.IsNullOrWhiteSpace(textBlock?.Text))
                {
                    return textBlock.Text;
                }
            }

            return tab.Name?.Replace("TabItem", string.Empty) ?? "Unknown";
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T match)
            {
                return match;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var result = FindVisualChild<T>(VisualTreeHelper.GetChild(parent, i));
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private FrameworkElement? CreateTabContent(TabItem tab)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return null;
            }

            if (ReferenceEquals(tab, GeneralTabItem))
            {
                return new GeneralView { DataContext = viewModel.General };
            }

            if (ReferenceEquals(tab, OmenTabItem))
            {
                return new AdvancedView { DataContext = viewModel };
            }

            if (ReferenceEquals(tab, TuningTabItem))
            {
                return new TuningView { DataContext = viewModel };
            }

            if (ReferenceEquals(tab, DiagnosticsTabItem))
            {
                return new DiagnosticsView { DataContext = viewModel };
            }

            if (ReferenceEquals(tab, OptimizerTabItem))
            {
                return new SystemOptimizerView { DataContext = viewModel.SystemOptimizer };
            }

            if (ReferenceEquals(tab, MemoryTabItem))
            {
                return new MemoryOptimizerView { DataContext = viewModel.MemoryOptimizer };
            }

            if (ReferenceEquals(tab, BloatwareTabItem))
            {
                return new BloatwareManagerView { DataContext = viewModel.BloatwareManager };
            }

            if (ReferenceEquals(tab, RgbTabItem))
            {
                var lightingView = new LightingView { DataContext = viewModel.Lighting };
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    lightingView.DataContext = await viewModel.EnsureLightingInitializedAsync();
                });
                return lightingView;
            }

            if (ReferenceEquals(tab, SettingsTabItem))
            {
                return new SettingsView { DataContext = viewModel.Settings };
            }

            if (ReferenceEquals(tab, GamesTabItem))
            {
                return new GameLibraryView { DataContext = viewModel.GameLibrary };
            }

            return null;
        }

        private static FrameworkElement BuildTabLoadFailureContent(string tabName, Exception ex)
        {
            var message = ex.GetBaseException().Message;

            return new Border
            {
                Margin = new Thickness(24),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x1F, 0x27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0x00, 0x2E)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Failed to load the {tabName} tab.",
                            FontSize = 15,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = Brushes.White,
                            Margin = new Thickness(0, 0, 0, 8)
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD4, 0xDC))
                        }
                    }
                }
            };
        }

        private void SystemParametersOnStaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea))
            {
                UpdateMaximizedBounds();
            }
            else if (e.PropertyName == nameof(SystemParameters.HighContrast))
            {
                ApplyModeAccent();
            }
        }

        private void OnGeneralPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GeneralViewModel.SelectedProfile))
                Dispatcher.BeginInvoke(ApplyModeAccent);
        }

        private void ApplyModeAccent()
        {
            var profile = (DataContext as MainViewModel)?.General?.SelectedProfile;
            ModeAccent.Apply(Application.Current.Resources, profile, HighContrastTheme.ReadSystemHighContrast());
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeButtonGlyph();
            // Square corners when maximized, otherwise the rounded frame shows desktop at the screen corners (#232).
            var maximized = WindowState == WindowState.Maximized;
            RootBorder.CornerRadius = new CornerRadius(maximized ? 0 : 12);
            TitleBarBorder.CornerRadius = new CornerRadius(maximized ? 0 : 12, maximized ? 0 : 12, 0, 0);
            SidebarBorder.CornerRadius = new CornerRadius(0, 0, 0, maximized ? 0 : 12);
            if (maximized)
            {
                UpdateMaximizedBounds();
            }
            // Note: We no longer hide to tray on minimize - that was causing Issue #20
            // Minimize now properly minimizes to taskbar
            (DataContext as MainViewModel)?.Lighting?.NotifyHostMinimized(WindowState == WindowState.Minimized);
        }

        private void UpdateMaximizeButtonGlyph()
        {
            // Drawn vector icon, not a font glyph - see Icon.WindowMaximize/Icon.WindowRestore
            // in ModernStyles.xaml for why.
            MaximizeIconPath.Data = (Geometry)FindResource(
                WindowState == WindowState.Maximized ? "Icon.WindowRestore" : "Icon.WindowMaximize");
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(MaximizeHook);
        }

        // Keeps a thin strip of an auto-hidden taskbar's edge uncovered when maximised, so the taskbar can still pop up.
        private IntPtr MaximizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmGetMinMaxInfo = 0x24;
            if (msg != WmGetMinMaxInfo || !TaskbarAutoHide.TryGetState(out var autoHide, out var edge) || !autoHide)
                return IntPtr.Zero;

            var monitor = NativeMonitor.FromWindow(hwnd);
            if (monitor == null) return IntPtr.Zero;
            var (m, w) = monitor.Value;

            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMonitor.MinMaxInfo>(lParam);
            var fit = TaskbarAutoHide.Fit(w.left - m.left, w.top - m.top, w.right - w.left, w.bottom - w.top, true, edge);
            info.ptMaxPosition = new NativeMonitor.Point { x = fit.Left, y = fit.Top };
            info.ptMaxSize = new NativeMonitor.Point { x = fit.Width, y = fit.Height };
            System.Runtime.InteropServices.Marshal.StructureToPtr(info, lParam, true);
            handled = true;
            return IntPtr.Zero;
        }

        private void UpdateMaximizedBounds()
        {
            var workArea = SystemParameters.WorkArea;
            MaxHeight = workArea.Height + 12;
            MaxWidth = workArea.Width + 12;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks on window control buttons so minimize/maximize/close always work.
            if (IsFromWindowControl(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException ex)
                {
                    App.Logging.Debug($"[MainWindow] Ignored transient title-bar drag failure: {ex.Message}");
                }
            }
        }

        private static bool IsFromWindowControl(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is Button)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            // Minimize to taskbar (normal Windows behavior)
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            
            // Check if we should minimize to tray or actually close
            bool minimizeToTray = App.Configuration.Config.Monitoring?.MinimizeToTrayOnClose ?? true;
            
            if (minimizeToTray)
            {
                // Hide to tray on close button
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                // Actually close the application
                App.Current?.Shutdown();
            }
        }
    }
}
