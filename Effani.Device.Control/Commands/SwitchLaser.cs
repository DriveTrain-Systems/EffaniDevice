using Drivetrain.Robotics.Control;
using Drivetrain.Robotics.Process;

namespace Effani.Device.Control
{
	/// <summary>Switches the DNA laser (output port 3) or the NADH laser (output port 2) on or off; its description names the board and connector.</summary>
	public class SwitchLaser(bool dna, bool on, EffaniDeviceControl device, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			var device = (EffaniDeviceControl)Device;
			var error = (Dna, On) switch
			{
				(true, true) => device.LaserDnaOn(),
				(true, false) => device.LaserDnaOff(),
				(false, true) => device.LaserNadhOn(),
				(false, false) => device.LaserNadhOff(),
			};
			if (error is not null)
				throw new ApplicationException(error);
		}

		public bool Dna { get; } = dna;
		public bool On { get; } = on;
		public override string Description => $"Turn {(On ? "on" : "off")} the {(Dna ? $"DNA laser ({((EffaniDeviceControl)Device).DnaLaserLocation})" : $"NADH laser ({((EffaniDeviceControl)Device).NadhLaserLocation})")}.";
	}
}
