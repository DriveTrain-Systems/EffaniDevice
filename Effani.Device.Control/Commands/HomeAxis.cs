using Drivetrain.Robotics.Control;
using System;
using System.Linq;
using Drivetrain.Robotics.Process;

namespace Effani.Device.Control
{
	public class HomeAxis : Home
	{
		private readonly Axis _axis;

		public HomeAxis(Axis axis, EffaniDeviceControl device, TimeSpan? timeout = null) : base(device, null, timeout)
		{
			_axis = axis;
			AxisSpec = (Device as EffaniDeviceControl).Axes[_axis];
		}
	}
}
