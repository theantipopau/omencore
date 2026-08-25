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
            DataContext = viewModel;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            TabControlMain.SelectionChanged += TabControlMain_SelectionChanged;
            TabControlMain.SizeChanged += TabControlMain_SizeChanged;
            SystemParameters.StaticPropertyChanged += SystemParametersOnStaticPropertyChanged;
            
            // Apply Stay on Top setting from config
            Topmost = App.Configuration.Config.StayOnTop;
            
            // Listen for LogBuffer changes to auto-scroll the system log
            if (viewModel is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += OnViewModelPropertyChanged;
            }
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
            (DataContext as MainViewModel)?.Dispose();
        }

        private void TabControlMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, TabControlMain))
            {
                return;
            }

            if (TabControlMain.SelectedItem is TabItem tab)
            {
                EnsureTabContentCreated(tab);
            }

            AnimateTabContentTransition();
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

                    tab.Content = new HardwareMonitoringDashboard
                    {
                        DataContext = viewModel
                    };
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

        private static string GetTabHeaderText(TabItem tab)
        {
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
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeButtonGlyph();
            if (WindowState == WindowState.Maximized)
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
