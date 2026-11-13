using Drivetrain.Robotics.Control;
using System;
using System.Linq;

namespace Effani.Device.Control
{
	public class AlignTableForWaste(EffaniDeviceControl device, TimeSpan? timeout = null) : MoveTableAbsolute(device, device.Configuration.Settings.WasteLocation, timeout)
	{
		public override string Description => $"Align table for waste disposal.";
	}
}
