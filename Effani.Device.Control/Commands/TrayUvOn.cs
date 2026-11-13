using Drivetrain.Robotics.Control;
using Drivetrain.Robotics.Process;

namespace Effani.Device.Control
{
	public class TrayUvOn(EffaniDeviceControl device, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			var device = (EffaniDeviceControl)Device;
			var error = device.AccOn(0);
			if (error is not null)
				throw new ApplicationException(error);
		}

		public override string Description => $"Turn on the tray UV sterilizer ({((EffaniDeviceControl)Device).TrayUvLocation}).";
	}
}
