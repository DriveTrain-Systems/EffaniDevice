using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Effani.Device.UI
{
	/// <summary>
	/// The background of a Home button: yellow until its axes are homed, green once they are (yellow too while the homed states have
	/// not been read yet). The values and parameter name the axes as for <see cref="AxisHomedConverter"/>: "all" for Home All.
	/// </summary>
	public class AxisHomedBrushConverter : IMultiValueConverter
	{
		public static readonly Brush NotHomed = Frozen(Color.FromRgb(0xFF, 0xD5, 0x4F));
		public static readonly Brush IsHomed = Frozen(Color.FromRgb(0x81, 0xC7, 0x84));

		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
		{
			return AxisHomedConverter.AreHomed(values, parameter) ? IsHomed : NotHomed;
		}
		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
		private static Brush Frozen(Color color)
		{
			var brush = new SolidColorBrush(color);
			brush.Freeze();
			return brush;
		}
	}
}
