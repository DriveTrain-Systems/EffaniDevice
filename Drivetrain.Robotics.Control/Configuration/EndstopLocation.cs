using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	public enum EndstopLocation
	{
		[Display(Name = "High End")]
		HighEnd,
		[Display(Name = "Low End")]
		LowEnd
	}
}
