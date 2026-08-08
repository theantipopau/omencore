using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OmenCore.ViewModels;

namespace OmenCore.Views
{
    /// <summary>
    /// Mouse handling for the per-key editor: click to toggle a key, drag to rubber-band a region.
    ///
    /// Only the INPUT lives here. Which keys a band catches is
    /// <see cref="KeyboardMapViewModel.SelectWithin"/>'s decision, because that is the part worth
    /// being able to reason about without a window on screen - a wrong bounds check there does not
    /// throw, it silently selects the neighbouring key.
    ///
    /// A click and a drag are the same gesture until the mouse moves, so both are started here and
    /// only told apart on mouse-up, by distance. Committing at press time would make every drag
    /// begin by toggling whatever key it started on.
    /// </summary>
    public partial class KeyboardMapEditor : UserControl
    {
        /// <summary>
        /// Movement below this, in logical pixels, is a click rather than a drag. Set above a
        /// trackpad's incidental travel: on a touchpad a "click" routinely moves a pixel or two,
        /// and treating that as a zero-area drag would clear the selection instead of toggling.
        /// </summary>
        private const double DragThreshold = 4.0;

        private Point _origin;
        private bool _tracking;

        public KeyboardMapEditor()
        {
            InitializeComponent();
        }

        private KeyboardMapViewModel? Model => DataContext as KeyboardMapViewModel;

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Model == null) return;

            _origin = e.GetPosition(MapRoot);
            _tracking = true;

            Band.Visibility = Visibility.Collapsed;
            MapRoot.CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_tracking || e.LeftButton != MouseButtonState.Pressed) return;

            var current = e.GetPosition(MapRoot);
            if (!HasMoved(current)) return;

            double left = Math.Min(_origin.X, current.X);
            double top = Math.Min(_origin.Y, current.Y);

            Canvas.SetLeft(Band, left);
            Canvas.SetTop(Band, top);
            Band.Width = Math.Abs(current.X - _origin.X);
            Band.Height = Math.Abs(current.Y - _origin.Y);
            Band.Visibility = Visibility.Visible;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_tracking || Model == null) return;

            _tracking = false;
            MapRoot.ReleaseMouseCapture();
            Band.Visibility = Visibility.Collapsed;

            var end = e.GetPosition(MapRoot);
            bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;

            if (HasMoved(end))
            {
                Model.SelectWithin(_origin.X, _origin.Y, end.X, end.Y, additive);
                return;
            }

            // A click. Resolve it through the visual tree rather than by hit-testing coordinates
            // against the layout: the element under the cursor is what the user believes they
            // clicked, and it stays correct however the Viewbox has scaled things.
            if (e.OriginalSource is FrameworkElement { DataContext: KeyLampViewModel key })
                Model.ToggleKey(key);
            else if (!additive)
                Model.SelectWithin(0, 0, 0, 0, additive: false); // empty band clears
        }

        private bool HasMoved(Point p) =>
            Math.Abs(p.X - _origin.X) > DragThreshold || Math.Abs(p.Y - _origin.Y) > DragThreshold;
    }
}
