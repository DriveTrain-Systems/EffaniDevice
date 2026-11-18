using DevExpress.Xpf.Core;
using System;
using System.Windows;

namespace Effani.Device.UI
{
	/// <summary>
	/// Asks how far to move an axis from where it is, and which way. The distance is typed as a positive number and the direction
	/// chosen with a button, so a minus sign never has to be typed on the touchscreen. A direction is only offered when the axis
	/// has that much room left on its travel.
	/// </summary>
	public partial class MoveByView : ThemedWindow
	{
		/// <param name="axisLabel">The axis's name, as shown in the axis list.</param>
		/// <param name="minimum">The lowest position (mm) the axis can go to.</param>
		/// <param name="maximum">The highest position (mm) the axis can go to.</param>
		/// <param name="current">Where the axis is now, when that is known.</param>
		public MoveByView(string axisLabel, decimal minimum, decimal maximum, decimal? current)
		{
			InitializeComponent();
			_current = current;
			var travel = Math.Max(0, maximum - minimum);
			// how far the axis can go each way; the whole travel while its position is not known (the board then holds it to its travel)
			_back = current is decimal from ? Math.Clamp(from - minimum, 0, travel) : travel;
			_forward = current is decimal at ? Math.Clamp(maximum - at, 0, travel) : travel;
			Title = $"Move {axisLabel}";
			Heading.Text = $"Move {axisLabel}";
			Note.Text = $"Type how far to move the {axisLabel} axis, in millimetres, then press the direction to move it in. "
				+ (current is decimal now
					? $"The axis is at {now:0.###} mm on a travel of {minimum:0.###} to {maximum:0.###} mm, so it can go up to {_back:0.###} mm back and {_forward:0.###} mm forward."
					: $"Where the axis is has not been read, so the distance is only held to the length of its travel, {travel:0.###} mm.")
				+ " The move is added to the command queue.";
			// the field has no limits of its own: the most room the axis has either way is its rule, which Validate has checked
			SettingsControl.SetMost(DistanceEdit, (double)Math.Max(_back, _forward));
			DistanceEdit.EditValue = Math.Min(10m, Math.Max(_back, _forward));
			Validate(out _);
			Loaded += (s, e) =>
			{
				DistanceEdit.Focus();
				DistanceEdit.SelectAll();
			};
		}

		/// <summary>How far to move, once the window is closed with a direction: negative is back (toward 0), positive forward.</summary>
		public decimal? Distance { get; private set; }

		private void DistanceEdit_EditValueChanged(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e) => Validate(out _);
		private void Back_Click(object sender, RoutedEventArgs e) => Accept(forward: false);
		private void Forward_Click(object sender, RoutedEventArgs e) => Accept(forward: true);
		private void Accept(bool forward)
		{
			DistanceEdit.DoValidate(); // takes in what was typed last
			if (!Validate(out var distance) || distance > (forward ? _forward : _back))
				return;
			Distance = forward ? distance : -distance;
			DialogResult = true;
		}
		/// <summary>
		/// Whether the editor holds a distance the axis can move in at least one direction; offers each direction it has room
		/// for, with where the axis would end up, and says what is wrong when there is none.
		/// </summary>
		private bool Validate(out decimal distance)
		{
			distance = 0;
			var number = DistanceEdit.EditValue is not null
				&& decimal.TryParse(Convert.ToString(DistanceEdit.EditValue, System.Globalization.CultureInfo.CurrentCulture), out distance);
			var positive = number && distance > 0;
			var back = positive && distance <= _back;
			var forward = positive && distance <= _forward;
			if (BackButton is null || ForwardButton is null || Problem is null)
				return back || forward; // while the window is still being built
			BackButton.IsEnabled = back;
			ForwardButton.IsEnabled = forward;
			BackButton.Content = back && _current is decimal from ? $"\u2190 Back to {from - distance:0.###} mm" : "\u2190 Back";
			ForwardButton.Content = forward && _current is decimal at ? $"Forward to {at + distance:0.###} mm \u2192" : "Forward \u2192";
			Problem.Text = back || forward ? String.Empty
				: !positive ? "Enter a distance greater than 0."
				: (SettingsControl.RuleProblem(DistanceEdit, "The distance", (double)distance) is string problem ? problem + " " : String.Empty)
					+ $"The axis has room for {_back:0.###} mm back and {_forward:0.###} mm forward.";
			Problem.Visibility = back || forward ? Visibility.Collapsed : Visibility.Visible;
			return back || forward;
		}

		private readonly decimal? _current;
		private readonly decimal _back;
		private readonly decimal _forward;
	}
}
