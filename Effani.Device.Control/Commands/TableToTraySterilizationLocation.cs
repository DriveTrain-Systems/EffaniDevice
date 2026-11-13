using Drivetrain.Robotics.Control;
using Drivetrain.Robotics.Process;
using System;
using System.Linq;

namespace Effani.Device.Control
{
	public class TableToTraySterilizationLocation(EffaniDeviceControl device, TimeSpan? timeout = null) : Command(device, timeout ?? SettingTimeout(device, s => s.MoveTimeoutSeconds))
	{
		protected override void Run()
		{
			(Device as EffaniDeviceControl).TableGoTo(Device.Configuration.Settings.TraySterilizationLocation);
		}

		public override string Description => $"Move table to tray sterilization location.";
	}
}
