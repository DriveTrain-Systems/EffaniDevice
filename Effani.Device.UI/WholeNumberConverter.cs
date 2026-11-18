using System;
using System.Globalization;
using System.Windows.Data;

namespace Effani.Device.UI
{
	/// <summary>
	/// For a number field bound to a whole number: an empty field, or anything that is not a whole number that fits, goes to
	/// the view model as 0 instead of failing to convert (which shows "Value could not be converted" on the field). Whether
	/// the number is allowed is for the program's rule check to say, not for the binding.
	/// </summary>
	public class WholeNumberConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			try
			{
				return value switch
				{
					null => 0,
					string text => int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out var typed) ? typed : 0,
					IConvertible number => (int)Math.Clamp(Math.Round(number.ToDecimal(CultureInfo.InvariantCulture)), int.MinValue, int.MaxValue),
					_ => 0,
				};
			}
			catch (Exception)
			{
				return 0;
			}
		}
	}
}
