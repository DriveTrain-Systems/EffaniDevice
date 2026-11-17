using DevExpress.Xpf.Core;
using Drivetrain.Robotics.Control;
using Drivetrain.Robotics.Process;
using Effani.Device.Control;
using Effani.Device.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace Effani.Device.UI
{
	public partial class UtilitiesView : ThemedWindow
	{
		public UtilitiesView(ISettingsService settingsService, ProcessEngine processEngine, EffaniDeviceControl device, ObservableCollection<string> consoleMessages, bool isAssayRunning = false)
		{
			InitializeComponent();
			ControlBoxButtonSet = ControlBoxButtons.None; // no title bar buttons: the Back button closes the window
			// settings may have been changed since the device configuration was built: rebuild it from the current settings,
			// unless an assay is running or commands are still queued or executing, which depend on the configuration they started with
			if (!isAssayRunning && !processEngine.IsBusy)
				device.RefreshSettings();
			DataContext = new UtilitiesViewModel(settingsService, processEngine, device, consoleMessages) { IsAssayRunning = isAssayRunning };
			Closed += (s, e) => (DataContext as IDisposable)?.Dispose();
			this.Height = (System.Windows.SystemParameters.PrimaryScreenHeight * 0.90);
			this.Width = (System.Windows.SystemParameters.PrimaryScreenWidth * 0.90);
#if DEBUG
			AddSimulateHomedToggle();
#endif
		}
#if DEBUG
		/// <summary>
		/// Debug builds only: a button in the lower right corner, beside the on-screen keyboard's, that shows every axis as homed
		/// (or goes back to what the board reports), so the controls of a homed axis can be tried without a board.
		/// </summary>
		private void AddSimulateHomedToggle()
		{
			if (Content is not Grid root || DataContext is not UtilitiesViewModel viewModel)
				return;
			var state = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
			var toggle = new ToggleButton
			{
				Width = 60,
				Height = 60,
				Padding = new Thickness(0),
				Focusable = false,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Bottom,
				Margin = new Thickness(0, 0, 72, 6), // to the left of the keyboard button, which is 60 wide and 6 from the corner
				Cursor = Cursors.Hand,
				Content = new StackPanel
				{
					Children =
					{
						new TextBlock { Text = "⌂", FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol"), FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center },
						state,
					},
				},
			};
			void Refresh()
			{
				toggle.IsChecked = viewModel.SimulateHomed;
				state.Text = viewModel.SimulateHomed ? "SIM" : "REAL";
				toggle.ToolTip = viewModel.SimulateHomed
					? "Debug build only. Simulated homing is on: every axis is shown as homed, whatever the board reports. Touch to go back to what the board reports."
					: "Debug build only. Axes are shown homed or not as the board reports. Touch to show every axis as homed, to try its controls without a board.";
			}
			toggle.Checked += (s, e) => { viewModel.SimulateHomed = true; Refresh(); };
			toggle.Unchecked += (s, e) => { viewModel.SimulateHomed = false; Refresh(); };
			Refresh();
			Grid.SetColumnSpan(toggle, Math.Max(1, root.ColumnDefinitions.Count));
			root.Children.Add(toggle);
		}
#endif
		private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
		/// <summary>
		/// The gear of an axis: shows or hides the axis's settings under its row. The panel is built the first time, from the
		/// fields of the settings pages (see <see cref="SettingsControl.CreateAxisSettingsPanel"/>).
		/// </summary>
		private void AxisSettings_Toggled(object sender, RoutedEventArgs e)
		{
			if (sender is not ToggleButton { DataContext: AxisSpec axis } gear || DataContext is not UtilitiesViewModel viewModel)
				return;
			DependencyObject? row = gear;
			while (row is not null and not GroupBox)
				row = System.Windows.Media.VisualTreeHelper.GetParent(row);
			if (row is null || FindTagged(row, "axisSettings") is not ContentControl host)
				return;
			if (gear.IsChecked == true && host.Content is null)
				host.Content = SettingsPagesControl.CreateAxisSettingsPanel(axis.Label, viewModel);
			host.Visibility = gear.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
		}
		private static FrameworkElement? FindTagged(DependencyObject root, string tag)
		{
			for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
			{
				var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
				if (child is FrameworkElement element && Equals(element.Tag, tag))
					return element;
				if (FindTagged(child, tag) is FrameworkElement deeper)
					return deeper;
			}
			return null;
		}
		/// <summary>Move... in an axis's list of commands: asks how far to move the axis and which way, then queues the move.</summary>
		private void MoveBy_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not FrameworkElement { DataContext: AxisSpec axis } button || DataContext is not UtilitiesViewModel viewModel)
				return;
			// the list the button is in is closed first, so it is not left open behind the question
			for (DependencyObject? parent = button; parent is not null; parent = LogicalTreeHelper.GetParent(parent))
			{
				if (parent is DevExpress.Xpf.Core.DropDownButton list)
				{
					list.IsPopupOpen = false;
					break;
				}
			}
			decimal? current = axis.Designation is not null && viewModel.Positions.TryGetValue(axis.Designation, out var now) ? now : null;
			var dialog = new MoveByView(axis.Label, axis.Minimum, axis.Maximum, current) { Owner = this };
			if (dialog.ShowDialog() == true && dialog.Distance is decimal distance)
				viewModel.MoveAxisBy(axis, distance);
		}

		#region Axis position slider
		// The slider of an axis shows where the axis is. A drag of its handle moves the axis to where the handle is let go; a tap
		// opens a window to type a position. The pointer is handled here rather than by the slider itself, so that a tap can be
		// told from a drag, nothing moves until the handle is let go, and a finger's drag is not taken by the list's touch scrolling.

		/// <summary>How far (in the slider's own units) the pointer may wander and still count as a tap.</summary>
		private const double TapDistance = 9;
		private Slider? _activeSlider;
		private double _sliderDownX;
		private bool _sliderDragged;

		private void AxisSlider_MouseDown(object sender, MouseButtonEventArgs e)
		{
			var slider = (Slider)sender;
			BeginSlide(slider, e.GetPosition(slider).X);
			slider.CaptureMouse();
			e.Handled = true; // the slider does not act on the press itself
		}
		private void AxisSlider_MouseMove(object sender, MouseEventArgs e)
		{
			var slider = (Slider)sender;
			if (ReferenceEquals(_activeSlider, slider) && slider.IsMouseCaptured)
				Slide(slider, e.GetPosition(slider).X);
		}
		private void AxisSlider_MouseUp(object sender, MouseButtonEventArgs e)
		{
			var slider = (Slider)sender;
			if (!ReferenceEquals(_activeSlider, slider))
				return;
			var x = e.GetPosition(slider).X;
			e.Handled = true;
			EndSlide(slider, x);
			slider.ReleaseMouseCapture(); // after the slide is ended, so that losing the capture does not cancel it
		}
		private void AxisSlider_TouchDown(object sender, TouchEventArgs e)
		{
			var slider = (Slider)sender;
			if (!slider.IsEnabled)
				return;
			BeginSlide(slider, e.GetTouchPoint(slider).Position.X);
			slider.CaptureTouch(e.TouchDevice);
			e.Handled = true; // not turned into a scroll of the list, nor into mouse events
		}
		private void AxisSlider_TouchMove(object sender, TouchEventArgs e)
		{
			var slider = (Slider)sender;
			if (!ReferenceEquals(_activeSlider, slider))
				return;
			Slide(slider, e.GetTouchPoint(slider).Position.X);
			e.Handled = true;
		}
		private void AxisSlider_TouchUp(object sender, TouchEventArgs e)
		{
			var slider = (Slider)sender;
			if (!ReferenceEquals(_activeSlider, slider))
				return;
			var x = e.GetTouchPoint(slider).Position.X;
			e.Handled = true;
			EndSlide(slider, x);
			slider.ReleaseTouchCapture(e.TouchDevice);
		}
		/// <summary>The press was taken away (another window came to the front, say): nothing is moved.</summary>
		private void AxisSlider_LostCapture(object sender, RoutedEventArgs e)
		{
			if (sender is Slider slider && ReferenceEquals(_activeSlider, slider))
				CancelSlide(slider);
		}

		private void BeginSlide(Slider slider, double x)
		{
			_activeSlider = slider;
			_sliderDownX = x;
			_sliderDragged = false;
		}
		private void Slide(Slider slider, double x)
		{
			if (!_sliderDragged && Math.Abs(x - _sliderDownX) <= TapDistance)
				return;
			_sliderDragged = true;
			var position = PositionAt(slider, x);
			slider.SetCurrentValue(RangeBase.ValueProperty, (double)position); // the handle follows; the binding to the axis's position stays
			SetTargetText(slider, $"to {position:0.#} mm");
		}
		private void EndSlide(Slider slider, double x)
		{
			var dragged = _sliderDragged;
			var position = PositionAt(slider, x);
			CancelSlide(slider);
			if (slider.DataContext is not AxisSpec axis || DataContext is not UtilitiesViewModel viewModel)
				return;

			if (dragged)
			{
				viewModel.MoveAxisTo(axis, position);
				return;
			}
			// a tap: ask for the position, once this press is over
			Dispatcher.BeginInvoke(() =>
			{
				decimal? current = axis.Designation is not null && viewModel.Positions.TryGetValue(axis.Designation, out var now) ? now : null;
				var dialog = new MoveAxisView(axis.Label, axis.Minimum, axis.Maximum, current) { Owner = this };
				if (dialog.ShowDialog() == true && dialog.Position is decimal typed)
					viewModel.MoveAxisTo(axis, typed);
			});
		}
		/// <summary>Ends a slide without moving the axis: the handle goes back to where the axis is.</summary>
		private void CancelSlide(Slider slider)
		{
			_activeSlider = null;
			_sliderDragged = false;
			SetTargetText(slider, String.Empty);
			BindingOperations.GetMultiBindingExpression(slider, RangeBase.ValueProperty)?.UpdateTarget();
		}
		/// <summary>
		/// The position on the axis's travel under a point of the slider, never outside the travel: whole millimetres on a long
		/// axis, tenths on a short one (a typed position can be finer).
		/// </summary>
		private static decimal PositionAt(Slider slider, double x)
		{
			// the handle's width: its centre cannot reach the very ends of the slider
			var handle = (slider.Template?.FindName("PART_Track", slider) as Track)?.Thumb?.ActualWidth ?? 12;
			var usable = Math.Max(1, slider.ActualWidth - handle);
			var fraction = Math.Clamp((x - (handle / 2)) / usable, 0, 1);
			var range = slider.Maximum - slider.Minimum;
			var position = (decimal)(slider.Minimum + (fraction * range));
			position = Math.Round(position, range >= 50 ? 0 : 1);
			return Math.Clamp(position, (decimal)slider.Minimum, (decimal)slider.Maximum);
		}
		private static void SetTargetText(Slider slider, string text)
		{
			if (slider.Parent is Panel panel && panel.Children.OfType<TextBlock>().FirstOrDefault(t => Equals(t.Tag, "target")) is TextBlock target)
				target.Text = text;
		}
		#endregion
	}
}
