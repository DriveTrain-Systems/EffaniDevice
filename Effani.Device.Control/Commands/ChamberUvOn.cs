using Drivetrain.Robotics.Control;
using Drivetrain.Robotics.Process;

namespace Effani.Device.Control
{
	public class ChamberUvOn(EffaniDeviceControl device, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			var device = (EffaniDeviceControl)Device;
			var error = device.AccOn(1);
			if (error is not null)
				throw new ApplicationException(error);
		}

		public override string Description => $"Turn on the chamber UV sterilizer ({((EffaniDeviceControl)Device).ChamberUvLocation}).";
	}
}
