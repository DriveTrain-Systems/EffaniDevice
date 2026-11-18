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
			StartSpinners();
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
				Issue(button, () => viewModel.MoveAxisBy(axis, distance));
		}

		#region Spinners
		// A control that issues a command to the device carries a spinner from then until the command is over: it ran, failed,
		// or was cleared from the queue. The view model says what it queues (JobQueued); which control that was for is worked
		// out here, from the button being clicked at that moment or from the control named by the code that asked (Issue).

		static UtilitiesView()
		{
			// every button, also the ones in the lists that drop down (which are not under the window in the visual tree)
			EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(AnyButton_Click), true);
		}
		private static void AnyButton_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not ButtonBase button || !ReferenceEquals(e.OriginalSource, sender) || Application.Current is null)
				return;
			foreach (var tools in Application.Current.Windows.OfType<UtilitiesView>())
				tools.ButtonClicked(button);
		}
		private void StartSpinners()
		{
			if (DataContext is not UtilitiesViewModel viewModel)
				return;
			viewModel.JobQueued += Job_Queued;
			_busyTimer.Tick += (s, e) => CheckSpinners();
			Closed += (s, e) =>
			{
				viewModel.JobQueued -= Job_Queued;
				_busyTimer.Stop();
			};
		}
		/// <summary>
		/// A button was clicked. What it queues while the click is handled is its own; so is what was queued just before, which
		/// is how a switch works (its value is set, queuing the command, before its click is announced).
		/// </summary>
		private void ButtonClicked(ButtonBase button)
		{
			foreach (var job in _unclaimed)
				Attach(button, job);
			_unclaimed.Clear();
			_issuer = button;
			Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
			{
				if (ReferenceEquals(_issuer, button))
					_issuer = null;
			});
		}
		/// <summary>Runs something that queues commands on behalf of a control that is not a button being clicked (a slider, a dialog's answer).</summary>
		private void Issue(FrameworkElement control, Action action)
		{
			var before = _issuer;
			_issuer = control;
			try
			{
				action();
			}
			finally
			{
				_issuer = before;
			}
		}
		private void Job_Queued(Job job)
		{
			if (!Dispatcher.CheckAccess())
				return;
			// Enter in the command box sends the command, the same as the Send button, which then carries the spinner
			var issuer = _issuer ?? (SendText.IsKeyboardFocusWithin && Keyboard.IsKeyDown(Key.Return) ? SendButton : null);
			if (issuer is not null)
			{
				Attach(issuer, job);
				return;
			}
			// no control yet: a click announced within this same input claims it, otherwise it was not issued from a control
			_unclaimed.Add(job);
			Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => _unclaimed.Remove(job));
		}
		/// <summary>Marks a control as waiting for a job; a button in a list that drops down also marks the button that opens the list, which stays in view when the list closes.</summary>
		private void Attach(FrameworkElement control, Job job)
		{
			Track(control, job);
			for (var parent = ParentOf(control); parent is not null; parent = ParentOf(parent))
			{
				if (parent is DevExpress.Xpf.Core.DropDownButton list)
				{
					Track(list, job);
					break;
				}
			}
		}
		private static DependencyObject? ParentOf(DependencyObject element) =>
			LogicalTreeHelper.GetParent(element) ?? (element is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(element) : null);
		private void Track(FrameworkElement control, Job job)
		{
			if (!_busy.TryGetValue(control, out var spinner))
				_busy[control] = spinner = new Spinner(new BusyAdorner(control, centred: control is ToggleButton));
			spinner.Jobs.Add(job);
			Show(control, spinner);
			_busyTimer.Start();
		}
		/// <summary>Puts a control's spinner on the layer the control is now shown on (a list that drops down has a new one each time it opens).</summary>
		private static void Show(FrameworkElement control, Spinner spinner)
		{
			var layer = control.IsVisible ? System.Windows.Documents.AdornerLayer.GetAdornerLayer(control) : null;
			if (ReferenceEquals(layer, spinner.Layer))
				return;
			spinner.Layer?.Remove(spinner.Adorner);
			layer?.Add(spinner.Adorner);
			spinner.Layer = layer;
		}
		/// <summary>Takes the spinner off every control whose jobs are all over.</summary>
		private void CheckSpinners()
		{
			foreach (var (control, spinner) in _busy.ToList())
			{
				spinner.Jobs.RemoveAll(job => job.IsFinished);
				if (spinner.Jobs.Count > 0)
				{
					Show(control, spinner);
					continue;
				}
				spinner.Layer?.Remove(spinner.Adorner);
				spinner.Adorner.Stop();
				_busy.Remove(control);
			}
			if (_busy.Count == 0)
				_busyTimer.Stop();
		}
		private sealed class Spinner(BusyAdorner adorner)
		{
			public BusyAdorner Adorner { get; } = adorner;
			public System.Windows.Documents.AdornerLayer? Layer { get; set; }
			public System.Collections.Generic.List<Job> Jobs { get; } = new();
		}
		private readonly System.Collections.Generic.Dictionary<FrameworkElement, Spinner> _busy = new();
		private readonly System.Collections.Generic.List<Job> _unclaimed = new();
		private readonly System.Windows.Threading.DispatcherTimer _busyTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
		private FrameworkElement? _issuer;
		#endregion

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

			// the box around the slider carries the spinner while the move it asked for is under way
			var box = (slider.Parent as FrameworkElement)?.Parent as FrameworkElement ?? slider;
			if (dragged)
			{
				Issue(box, () => viewModel.MoveAxisTo(axis, position));
				return;
			}
			// a tap: ask for the position, once this press is over
			Dispatcher.BeginInvoke(() =>
			{
				decimal? current = axis.Designation is not null && viewModel.Positions.TryGetValue(axis.Designation, out var now) ? now : null;
				var dialog = new MoveAxisView(axis.Label, axis.Minimum, axis.Maximum, current) { Owner = this };
				if (dialog.ShowDialog() == true && dialog.Position is decimal typed)
					Issue(box, () => viewModel.MoveAxisTo(axis, typed));
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
