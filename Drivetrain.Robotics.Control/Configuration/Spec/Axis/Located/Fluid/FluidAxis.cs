using System;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	public class FluidAxis : LocatedAxis
	{
		public FluidAxis(Axis axis, BoardPort driverPort, BoardPort endstopPort, Coordinate location, decimal? startingPosition = null)
			: base(axis, driverPort, endstopPort, location, startingPosition)
        {
			Driver = new DriverSpec(Label)
			{
				Endstop = new EndstopSpec(Label)
                {
                    SensorPort = endstopPort.Designation,
                    Signal = PinSignal.High,
				}
			};
		}
	}
}
