using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace OmenCore.Controls
{
    /// <summary>
    /// A fully custom dark-themed context menu that eliminates all Windows system styling artifacts.
    /// No white margins, no icon gutter, fully dark theme with smooth animations.
    /// </summary>
    public class DarkContextMenu : ContextMenu
    {
        // Theme colors
        private static readonly Color SurfaceDark = Color.FromRgb(15, 17, 28);
        private static readonly Color SurfaceMedium = Color.FromRgb(21, 25, 43);
        private static readonly Color SurfaceLight = Color.FromRgb(30, 34, 55);
        private static readonly Color AccentPrimary = Color.FromRgb(255, 0, 92);    // OMEN Red/Pink
        private static readonly Color AccentSecondary = Color.FromRgb(0, 200, 200); // Cyan
        private static readonly Color BorderColor = Color.FromRgb(60, 65, 90);
        private static readonly Color HoverColor = Color.FromRgb(40, 45, 65);
        private static readonly Color PressedColor = Color.FromRgb(55, 60, 85);
        private static readonly Color TextPrimary = Color.FromRgb(240, 240, 245);
        private static readonly Color TextSecondary = Color.FromRgb(160, 165, 180);
        private static readonly Color TextMuted = Color.FromRgb(120, 125, 140);

        static DarkContextMenu()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DarkContextMenu),
                new FrameworkPropertyMetadata(typeof(DarkContextMenu)));
        }

        public DarkContextMenu()
        {
            // Set up the dark theme appearance
            var gradientBg = new LinearGradientBrush(
                Color.FromRgb(18, 20, 35),
                Color.FromRgb(25, 28, 48),
                new Point(0, 0),
                new Point(0, 1));

            Background = gradientBg;
            Foreground = new SolidColorBrush(TextPrimary);
            BorderBrush = new SolidColorBrush(BorderColor);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(4);
            HasDropShadow = true;

            // Override all system colors
            Resources.Add(SystemColors.MenuBarBrushKey, new SolidColorBrush(SurfaceDark));
            Resources.Add(SystemColors.MenuBrushKey, new SolidColorBrush(SurfaceDark));
            Resources.Add(SystemColors.MenuTextBrushKey, new SolidColorBrush(TextPrimary));
            Resources.Add(SystemColors.HighlightBrushKey, new SolidColorBrush(HoverColor));
            Resources.Add(SystemColors.HighlightTextBrushKey, Brushes.White);
            Resources.Add(SystemColors.MenuHighlightBrushKey, new SolidColorBrush(HoverColor));
            Resources.Add(SystemColors.ControlBrushKey, new SolidColorBrush(SurfaceDark));
            Resources.Add(SystemColors.ControlLightBrushKey, new SolidColorBrush(SurfaceDark));
            Resources.Add(SystemColors.ControlLightLightBrushKey, new SolidColorBrush(SurfaceDark));
            Resources.Add(SystemColors.ControlDarkBrushKey, new SolidColorBrush(SurfaceDark));
            Resources.Add(SystemColors.ControlDarkDarkBrushKey, new SolidColorBrush(SurfaceDark));
            Resources.Add(SystemColors.WindowBrushKey, new SolidColorBrush(SurfaceDark));

            // Apply custom styles
            Resources.Add(typeof(MenuItem), CreateMenuItemStyle());
            Resources.Add(typeof(Separator), CreateSeparatorStyle());
        }

        /// <summary>
        /// Creates a complete custom MenuItem style with ControlTemplate that has NO icon gutter.
        /// </summary>
        private Style CreateMenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));

            // Base setters
            style.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(TextPrimary)));
            style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(PaddingProperty, new Thickness(12, 8, 12, 8)));
            style.Setters.Add(new Setter(MinHeightProperty, 32.0));
            style.Setters.Add(new Setter(FontFamilyProperty, new FontFamily("Segoe UI")));
            style.Setters.Add(new Setter(FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(OverridesDefaultStyleProperty, true));

            // Create the ControlTemplate
            var template = CreateMenuItemTemplate();
            style.Setters.Add(new Setter(TemplateProperty, template));

            return style;
        }

        /// <summary>
        /// Creates a ControlTemplate for MenuItem that completely removes the icon column.
        /// </summary>
        private ControlTemplate CreateMenuItemTemplate()
        {
            var template = new ControlTemplate(typeof(MenuItem));

            // Grid to hold border + popup
            var grid = new FrameworkElementFactory(typeof(Grid), "Grid");

            // Main border (no icon column!)
            var border = new FrameworkElementFactory(typeof(Border), "Border");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.MarginProperty, new Thickness(2, 1, 2, 1));
            border.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));

            // Grid inside border for content + arrow
            // Note: We use DockPanel for simpler layout instead of Grid with columns

            // ContentPresenter for the Header
            var content = new FrameworkElementFactory(typeof(ContentPresenter), "HeaderPresenter");
            content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

            // Arrow path for submenus
            var arrow = new FrameworkElementFactory(typeof(Path), "Arrow");
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0,0 L 4,4 L 0,8 Z"));
            arrow.SetValue(Path.FillProperty, new SolidColorBrush(TextSecondary));
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(16, 0, 0, 0));
            arrow.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

            // Simple layout with DockPanel instead of Grid for easier setup
            var dockPanel = new FrameworkElementFactory(typeof(DockPanel));
            arrow.SetValue(DockPanel.DockProperty, Dock.Right);
            dockPanel.AppendChild(arrow);
            dockPanel.AppendChild(content);

            border.AppendChild(dockPanel);
            grid.AppendChild(border);

            // Popup for submenus
            var popup = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.Popup), "PART_Popup");
            popup.SetValue(System.Windows.Controls.Primitives.Popup.AllowsTransparencyProperty, true);
            popup.SetValue(System.Windows.Controls.Primitives.Popup.PlacementProperty, 
                System.Windows.Controls.Primitives.PlacementMode.Right);
            popup.SetValue(System.Windows.Controls.Primitives.Popup.HorizontalOffsetProperty, -2.0);
            popup.SetValue(System.Windows.Controls.Primitives.Popup.VerticalOffsetProperty, -4.0);
            popup.SetBinding(System.Windows.Controls.Primitives.Popup.IsOpenProperty,
                new Binding("IsSubmenuOpen") 
                { 
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) 
                });
            popup.SetValue(System.Windows.Controls.Primitives.Popup.PopupAnimationProperty, 
                System.Windows.Controls.Primitives.PopupAnimation.Fade);

            // Popup border with shadow
            var popupBorder = new FrameworkElementFactory(typeof(Border), "PopupBorder");
            var popupGradient = new LinearGradientBrush(
                Color.FromRgb(18, 20, 35),
                Color.FromRgb(25, 28, 48),
                new Point(0, 0),
                new Point(0, 1));
            popupBorder.SetValue(Border.BackgroundProperty, popupGradient);
            popupBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(BorderColor));
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
            popupBorder.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 8));
            popupBorder.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 4,
                Opacity = 0.5
            });

            // ItemsPresenter for submenu items
            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter), "ItemsPresenter");
            itemsPresenter.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
            popupBorder.AppendChild(itemsPresenter);
            popup.AppendChild(popupBorder);
            grid.AppendChild(popup);

            template.VisualTree = grid;

            // ======= TRIGGERS =======

            // Hover trigger
            var hoverTrigger = new Trigger 
            { 
                Property = MenuItem.IsHighlightedProperty, 
                Value = true 
            };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, 
                new SolidColorBrush(HoverColor), "Border"));
            template.Triggers.Add(hoverTrigger);

            // Pressed trigger
            var pressedTrigger = new Trigger 
            { 
                Property = MenuItem.IsPressedProperty, 
                Value = true 
            };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, 
                new SolidColorBrush(PressedColor), "Border"));
            template.Triggers.Add(pressedTrigger);

            // Submenu open trigger - show arrow and highlight
            var submenuTrigger = new Trigger 
            { 
                Property = MenuItem.HasItemsProperty, 
                Value = true 
            };
            submenuTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Arrow"));
            template.Triggers.Add(submenuTrigger);

            var submenuOpenTrigger = new Trigger 
            { 
                Property = MenuItem.IsSubmenuOpenProperty, 
                Value = true 
            };
            submenuOpenTrigger.Setters.Add(new Setter(Border.BackgroundProperty, 
                new SolidColorBrush(HoverColor), "Border"));
            template.Triggers.Add(submenuOpenTrigger);

            // Disabled trigger
            var disabledTrigger = new Trigger 
            { 
                Property = UIElement.IsEnabledProperty, 
                Value = false 
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            template.Triggers.Add(disabledTrigger);

            return template;
        }

        private Style CreateSeparatorStyle()
        {
            var style = new Style(typeof(Separator));
            style.Setters.Add(new Setter(Separator.BackgroundProperty, new SolidColorBrush(BorderColor)));
            style.Setters.Add(new Setter(Separator.MarginProperty, new Thickness(8, 6, 8, 6)));
            style.Setters.Add(new Setter(Separator.HeightProperty, 1.0));
            style.Setters.Add(new Setter(Separator.SnapsToDevicePixelsProperty, true));

            // Custom template to remove default separator chrome
            var template = new ControlTemplate(typeof(Separator));
            var rect = new FrameworkElementFactory(typeof(Rectangle));
            rect.SetValue(Rectangle.HeightProperty, 1.0);
            rect.SetValue(Rectangle.FillProperty, new SolidColorBrush(BorderColor));
            rect.SetValue(Rectangle.MarginProperty, new Thickness(8, 0, 8, 0));
            template.VisualTree = rect;
            style.Setters.Add(new Setter(Separator.TemplateProperty, template));

            return style;
        }

        // ====== HELPER METHODS FOR CREATING STYLED ITEMS ======

        /// <summary>
        /// Creates a header item (non-clickable title).
        /// </summary>
        public static MenuItem CreateHeader(string icon, string title, string? version = null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock 
            { 
                Text = icon, 
                FontSize = 14, 
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center 
            });
            panel.Children.Add(new TextBlock 
            { 
                Text = title, 
                FontWeight = FontWeights.Bold, 
                FontSize = 13, 
                Foreground = new SolidColorBrush(AccentPrimary),
                VerticalAlignment = VerticalAlignment.Center 
            });
            if (!string.IsNullOrEmpty(version))
            {
                panel.Children.Add(new TextBlock 
                { 
                    Text = $" {version}", 
                    FontSize = 11, 
                    Foreground = new SolidColorBrush(TextSecondary),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 1, 0, 0)
                });
            }

            return new MenuItem
            {
                Header = panel,
                IsEnabled = false,
                IsHitTestVisible = false
            };
        }

        /// <summary>
        /// Creates a monitoring display item (e.g., CPU: 65°C @ 45%).
        /// </summary>
        public static MenuItem CreateMonitoringItem(string icon, string label, string value, string? secondary = null, Color? accentColor = null)
        {
            var color = accentColor ?? TextPrimary;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            
            panel.Children.Add(new TextBlock 
            { 
                Text = icon, 
                FontSize = 12, 
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center 
            });
            panel.Children.Add(new TextBlock 
            { 
                Text = label + ":", 
                FontSize = 12, 
                Width = 36,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center 
            });
            panel.Children.Add(new TextBlock 
            { 
                Text = value, 
                FontSize = 12, 
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(color),
                MinWidth = 48,
                VerticalAlignment = VerticalAlignment.Center 
            });
            if (!string.IsNullOrEmpty(secondary))
            {
                panel.Children.Add(new TextBlock 
                { 
                    Text = secondary, 
                    FontSize = 11, 
                    Foreground = new SolidColorBrush(TextMuted),
                    VerticalAlignment = VerticalAlignment.Center 
                });
            }

            return new MenuItem
            {
                Header = panel,
                IsEnabled = false,
                IsHitTestVisible = false
            };
        }

        /// <summary>
        /// Creates a control item with current value indicator (e.g., Fan Mode: Auto ▸).
        /// </summary>
        public static MenuItem CreateControlItem(string icon, string label, string value, Color? accentColor = null)
        {
            var color = accentColor ?? AccentSecondary;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            
            panel.Children.Add(new TextBlock 
            { 
                Text = icon, 
                FontSize = 12, 
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center 
            });
            panel.Children.Add(new TextBlock 
            { 
                Text = label + ":", 
                FontSize = 12, 
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center 
            });
            panel.Children.Add(new TextBlock 
            { 
                Text = " " + value, 
                FontSize = 12, 
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center 
            });

            return new MenuItem { Header = panel };
        }

        /// <summary>
        /// Creates a submenu item with icon, title, and optional description.
        /// </summary>
        public static MenuItem CreateSubMenuItem(string icon, string label, string? description = null)
        {
            FrameworkElement header;
            
            if (string.IsNullOrEmpty(description))
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new TextBlock 
                { 
                    Text = icon, 
                    FontSize = 11, 
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center 
                });
                panel.Children.Add(new TextBlock 
                { 
                    Text = label, 
                    FontSize = 12, 
                    FontWeight = FontWeights.Medium,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center 
                });
                header = panel;
            }
            else
            {
                var panel = new StackPanel { Orientation = Orientation.Vertical };
                
                var mainRow = new StackPanel { Orientation = Orientation.Horizontal };
                mainRow.Children.Add(new TextBlock 
                { 
                    Text = icon, 
                    FontSize = 11, 
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center 
                });
                mainRow.Children.Add(new TextBlock 
                { 
                    Text = label, 
                    FontSize = 12, 
                    FontWeight = FontWeights.Medium,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center 
                });
                panel.Children.Add(mainRow);
                
                panel.Children.Add(new TextBlock 
                { 
                    Text = description, 
                    FontSize = 10, 
                    Foreground = new SolidColorBrush(TextMuted),
                    Margin = new Thickness(19, 2, 0, 0)
                });
                header = panel;
            }

            return new MenuItem { Header = header };
        }

        /// <summary>
        /// Creates a simple action item (e.g., Open Dashboard, Exit).
        /// </summary>
        public static MenuItem CreateActionItem(string icon, string label, bool isPrimary = false)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock 
            { 
                Text = icon, 
                FontSize = 12, 
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center 
            });
            panel.Children.Add(new TextBlock 
            { 
                Text = label, 
                FontSize = 12, 
                FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isPrimary ? Brushes.White : new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center 
            });

            return new MenuItem { Header = panel };
        }

        // Static color accessors for external use
        public static Color GetAccentPrimary() => AccentPrimary;
        public static Color GetAccentSecondary() => AccentSecondary;
        public static Color GetTextPrimary() => TextPrimary;
        public static Color GetTextSecondary() => TextSecondary;
        public static Color GetTextMuted() => TextMuted;
    }
}
