using Drivetrain.Robotics.Control;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Data;

namespace Effani.Device.UI
{
	/// <summary>
	/// True when the axes a button moves are homed. The values are the homed states by axis letter and, for a button of one axis,
	/// that axis's letter. The parameter names the axes instead: "all" for every axis, "table" for Table X and Y, or a comma
	/// separated list of <see cref="Axis"/> names ("DnaReagent,Koh,Nadh,Basal").
	/// </summary>
	public class AxisHomedConverter : IMultiValueConverter
	{
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => AreHomed(values, parameter);

		/// <summary>Whether the axes named by the values or the parameter are homed (false while the homed states are unknown).</summary>
		public static bool AreHomed(object[] values, object parameter)
		{
			var homed = values.OfType<IReadOnlyDictionary<string, bool>>().FirstOrDefault();
			if (homed is null || homed.Count == 0)
				return false;
			bool IsHomed(string letter) => homed.TryGetValue(letter, out var isHomed) && isHomed;

			var named = (parameter as string)?.Trim();
			if (String.Equals(named, "all", StringComparison.OrdinalIgnoreCase))
				return homed.Values.All(h => h);
			if (String.Equals(named, "table", StringComparison.OrdinalIgnoreCase))
				named = $"{nameof(Axis.TableX)},{nameof(Axis.TableY)}";
			if (!String.IsNullOrEmpty(named))
				return named.Split(',').Select(n => Letter(n.Trim())).All(l => l is not null && IsHomed(l));
			return values.OfType<string>().FirstOrDefault() is string axisLetter && IsHomed(axisLetter);
		}
		/// <summary>The letter the board knows an axis by (its <see cref="DisplayAttribute.ShortName"/>), e.g. "V" for DnaReagent.</summary>
		private static string? Letter(string axisName) =>
			Enum.TryParse<Axis>(axisName, ignoreCase: true, out var axis)
				? typeof(Axis).GetField(axis.ToString())?.GetCustomAttribute<DisplayAttribute>()?.ShortName
				: null;

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
	/// <summary>
	/// Shows something only once the axes it belongs to are homed, or (<see cref="WhenHomed"/> false) only until then. The values
	/// and the parameter are those of <see cref="AxisHomedConverter"/>.
	/// </summary>
	public class AxisHomedVisibilityConverter : IMultiValueConverter
	{
		public bool WhenHomed { get; set; } = true;
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
			AxisHomedConverter.AreHomed(values, parameter) == WhenHomed ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
