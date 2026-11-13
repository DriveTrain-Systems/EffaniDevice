using System;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	/// <summary>How a smart stepper driver regulates the motor current (the D parameter of M569).</summary>
	public enum DriverMode
	{
		/// <summary>D2: full torque at every speed, but the driver can whine or squeal while the motor stands still.</summary>
		SpreadCycle,
		/// <summary>D3: near silent, including while the motor holds still; a little less torque at high speed.</summary>
		StealthChop
	}
}
