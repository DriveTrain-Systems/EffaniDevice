using Drivetrain.Robotics.Control;
using System;
using System.Linq;
using Drivetrain.Robotics.Process;

namespace Effani.Device.Control
{
	public class Aspirate(int microLiters, EffaniDeviceControl device, TimeSpan? timeout = null)
		: Command(device, timeout ?? DefaultTimeout(device))
	{
		// the longer of the setting and the dwell after lowering the needle (each wait for the board gets the whole timeout)
		private static TimeSpan? DefaultTimeout(EffaniDeviceControl device)
		{
			var setting = SettingTimeout(device, s => s.AspirateTimeoutSeconds);
			var dwell = TimeSpan.FromMilliseconds(device.Settings.AspirationDwell) + device.ReadyTimeout;
			return setting > dwell ? setting : dwell;
		}
		protected override void Run()
		{
			(Device as EffaniDeviceControl).Aspirate(MicroLiters);
		}

		public override string Description => $"Aspirate {MicroLiters}μl of KOH.";

		public int MicroLiters { get; } = microLiters;
	}
}
