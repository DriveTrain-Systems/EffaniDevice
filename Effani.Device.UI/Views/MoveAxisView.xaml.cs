using DevExpress.Xpf.Core;
using System;
using System.Windows;

namespace Effani.Device.UI
{
	/// <summary>
	/// Asks for the position to move an axis to. Only a position on the axis's travel is accepted: the editor holds what is typed
	/// to the travel, and Move stays off until the value is valid.
	/// </summary>
	public partial class MoveAxisView : ThemedWindow
	{
		/// <param name="axisLabel">The axis's name, as shown in the axis list.</param>
		/// <param name="minimum">The lowest position (mm) the axis can go to.</param>
		/// <param name="maximum">The highest position (mm) the axis can go to.</param>
		/// <param name="current">Where the axis is now, when that is known.</param>
		public MoveAxisView(string axisLabel, decimal minimum, decimal maximum, decimal? current)
		{
			InitializeComponent();
			_minimum = minimum;
			_maximum = maximum;
			Title = $"Move {axisLabel}";
			Heading.Text = $"Move {axisLabel}";
			Note.Text = $"Type the position to move the {axisLabel} axis to, in millimetres along its travel, then press Move. "
				+ $"A valid position is from {minimum:0.###} to {maximum:0.###} mm"
				+ (current is decimal now ? $"; the axis is at {now:0.###} mm now." : ".")
				+ " The move is added to the command queue.";
			PositionEdit.MinValue = minimum;
			PositionEdit.MaxValue = maximum;
			PositionEdit.EditValue = Math.Clamp(current ?? minimum, minimum, maximum);
			Loaded += (s, e) =>
			{
				PositionEdit.Focus();
				PositionEdit.SelectAll();
			};
		}

		/// <summary>The position that was asked for, once the window is closed with Move.</summary>
		public decimal? Position { get; private set; }

		private void PositionEdit_EditValueChanged(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e) => Validate(out _);

		private void Move_Click(object sender, RoutedEventArgs e)
		{
			PositionEdit.DoValidate(); // takes in what was typed last
			if (!Validate(out var position))
				return;
			Position = position;
			DialogResult = true;
		}
		/// <summary>Whether the editor holds a position on the axis's travel; says what is wrong when it does not.</summary>
		private bool Validate(out decimal position)
		{
			position = 0;
			var valid = PositionEdit.EditValue is not null
				&& decimal.TryParse(Convert.ToString(PositionEdit.EditValue, System.Globalization.CultureInfo.CurrentCulture), out position)
				&& position >= _minimum && position <= _maximum;
			Problem.Text = valid ? String.Empty : $"Enter a number from {_minimum:0.###} to {_maximum:0.###}.";
			Problem.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
			if (MoveButton is not null)
				MoveButton.IsEnabled = valid;
			return valid;
		}

		private readonly decimal _minimum;
		private readonly decimal _maximum;
	}
}
