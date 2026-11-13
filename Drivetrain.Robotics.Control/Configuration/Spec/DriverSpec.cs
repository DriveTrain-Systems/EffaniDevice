using System;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	public class DriverSpec : Spec
	{
		public DriverSpec(string label) : base(label)
		{

		}

		public int Current { get; set; } = 800; // milli amps

		public Direction Direction { get; set; } = Direction.Forwards;
		/// <summary>How the driver regulates the motor current (written to config.g as the D parameter of M569).</summary>
		public DriverMode Mode { get; set; } = DriverMode.SpreadCycle;
		public EndstopSpec Endstop { get; set; }
	}
}
