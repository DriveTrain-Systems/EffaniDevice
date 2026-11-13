using Drivetrain.Robotics.Control;
using System.Threading;
using Drivetrain.Robotics.Process;

namespace Effani.Device.Control
{
	public class ChamberUvOff(EffaniDeviceControl device, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			var device = (EffaniDeviceControl)Device;
			var error = device.AccOff(1);
			if (error is not null)
				throw new ApplicationException(error);
		}

		public override string Description => $"Turn off the chamber UV sterilizer ({((EffaniDeviceControl)Device).ChamberUvLocation}).";
	}
}
