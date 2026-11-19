using Drivetrain.Robotics.Control;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace Effani.Device.UI
{
	/// <summary>
	/// The line under an axis or sensor on Component Control that says what it is connected to: an axis's motor driver
	/// ("Axis X, 1.0", parameter "driver") or endstop input ("1.io0.in", parameter "endstop"), or a sensor's number and input
	/// ("S0, 1.temp0").
	///
	/// The values are the axis or sensor and <see cref="UtilitiesViewModel.Connections"/>, which holds the connections as the
	/// settings now have them. The axis or sensor itself is as it was when Tools last took up the settings, so its own ports
	/// are only used when the settings cannot say (they are half edited).
	/// </summary>
	public class ConnectionConverter : IMultiValueConverter
	{
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
		{
			var connections = values.Length > 1 ? values[1] as IReadOnlyDictionary<string, string> : null;
			switch (values.Length > 0 ? values[0] : null)
			{
				case AxisSpec axis when (parameter as string) == "endstop":
					return connections != null && connections.TryGetValue(DeviceConfiguration.EndstopKey(axis.Label), out var endstop)
						? endstop : axis.Driver?.Endstop?.SensorPort ?? "";
				case AxisSpec axis:
					return $"Axis {axis.Designation}, " + (connections != null && connections.TryGetValue(DeviceConfiguration.DriverKey(axis.Label), out var driver)
						? driver : axis.DriverPort);
				case SensorSpec sensor:
					return connections != null && connections.TryGetValue(DeviceConfiguration.SensorKey(sensor.Label), out var input)
						? input : $"S{sensor.SensorNumber}, {sensor.PortDesignation}";
				default:
					return "";
			}
		}
		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
	}
}
