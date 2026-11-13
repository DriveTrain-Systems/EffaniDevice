using System;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
    public class AspirateZAxis : LocatedAxis
    {
        public AspirateZAxis(Axis axis, BoardPort driverPort, BoardPort endstopPort, Coordinate location, int maximumMovement, EndstopLocation endstopLocation, Direction direction, int maxSpeed, decimal? startingPosition = null)
            : base(axis, driverPort, endstopPort, location, startingPosition)
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
                    FirstHomingSpeed = 4000
                }
            };
        }
    }
}
