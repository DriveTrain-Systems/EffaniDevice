using Drivetrain.Robotics.Control;
using System;
using System.Linq;

namespace Drivetrain.Robotics.Process
{
	public class SetTableTemp(decimal temperature, DeviceControl device, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			Device.SetTrayTemperature(_temperature);
		}

		public override string Description => $"Set table temperature to {_temperature}.";

		private readonly decimal _temperature = temperature;
	}
}
