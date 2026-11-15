using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace Effani.Device.UI
{
	/// <summary>Shows an axis's position: values[0] is the axis letter, values[1] the positions and values[2] (optional) the homed states, by axis letter.</summary>
	public class AxisPositionConverter : IMultiValueConverter
	{
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
		{
			if (values.Length >= 2 && values[0] is string letter && values[1] is IReadOnlyDictionary<string, decimal> positions
				&& positions.TryGetValue(letter, out var position))
			{
				var notHomed = values.Length >= 3 && values[2] is IReadOnlyDictionary<string, bool> homed && homed.TryGetValue(letter, out var isHomed) && !isHomed;
				return notHomed ? $"Position {position:0.000} mm (not homed)" : $"Position {position:0.000} mm";
			}
			return "Position not read yet";
		}
		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
	/// <summary>An axis's position as a number, for a slider: values[0] is the axis letter and values[1] the positions by axis letter. 0 until the position is read.</summary>
	public class AxisPositionValueConverter : IMultiValueConverter
	{
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
		{
			if (values.Length >= 2 && values[0] is string letter && values[1] is IReadOnlyDictionary<string, decimal> positions
				&& positions.TryGetValue(letter, out var position))
				return (double)position;
			return 0d;
		}
		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
