using System;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	public class TableAxis : AxisSpec
	{
		public TableAxis(Axis axis, BoardPort driverPort, BoardPort endstopPort, int maximumMovement, EndstopLocation endstopLocation, Direction direction, int maxSpeed, decimal? startingPosition = null)
			: base(axis, driverPort, endstopPort, startingPosition)
		{
			Maximum = maximumMovement;
			MaxSpeed = maxSpeed;
			Driver = new DriverSpec(Label)
			{
				Direction = direction,
				Endstop = new EndstopSpec($"{Label} Endstop")
                {
                    SensorPort = endstopPort.Designation,
                    Signal = PinSignal.High,
					Location = endstopLocation,
					FirstHomingSpeed = 3000
				}
			};
		}
	}
}
