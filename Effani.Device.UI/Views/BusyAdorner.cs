using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Effani.Device.UI
{
	/// <summary>
	/// A small spinner drawn over a control while a command issued from it is queued or running. It is in the control's upper
	/// right corner, or in its middle for a small control without a caption (a switch). It takes no input: the control under
	/// it works as before.
	/// </summary>
	internal sealed class BusyAdorner : Adorner
	{
		public BusyAdorner(UIElement element, bool centred) : base(element)
		{
			_centred = centred;
			IsHitTestVisible = false;
			var arc = new Path
			{
				Width = 16,
				Height = 16,
				Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
				StrokeThickness = 2.5,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				Data = Geometry.Parse("M 8,1.5 A 6.5,6.5 0 1 1 1.5,8"), // three quarters of a circle
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = _turn,
			};
			// nothing behind the arc: the control shows through
			_spinner = new Grid { Width = Size, Height = Size };
			_spinner.Children.Add(arc);
			AddVisualChild(_spinner);
			_turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
		}
		/// <summary>Stops the turning, when the spinner is taken off its control for good.</summary>
		public void Stop() => _turn.BeginAnimation(RotateTransform.AngleProperty, null);
		protected override int VisualChildrenCount => 1;
		protected override Visual GetVisualChild(int index) => _spinner;
		protected override Size MeasureOverride(Size constraint)
		{
			_spinner.Measure(new Size(Size, Size));
			return AdornedElement.RenderSize;
		}
		protected override Size ArrangeOverride(Size finalSize)
		{
			var x = _centred ? (finalSize.Width - Size) / 2 : finalSize.Width - Size - Inset;
			var y = _centred ? (finalSize.Height - Size) / 2 : Inset;
			_spinner.Arrange(new Rect(Math.Max(0, x), Math.Max(0, y), Size, Size));
			return finalSize;
		}
		private const double Size = 20;
		private const double Inset = 3;
		private readonly bool _centred;
		private readonly Grid _spinner;
		private readonly RotateTransform _turn = new();
	}
}
