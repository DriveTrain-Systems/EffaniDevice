using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Drivetrain.Robotics.Control
{
	public class AxisSpec : Spec
	{
		public AxisSpec(Axis axis, BoardPort driverPort, BoardPort endstopPort, decimal? startingPosition = null)
			: base(axis.GetAttribute<DisplayAttribute>().Name)
		{
			Axis = axis;
			DriverBoardPort = driverPort.Expect(PortKind.Driver, $"The {Label} motor");
			EndstopBoardPort = endstopPort.Expect(PortKind.Input, $"The {Label} endstop");
			DriverPort = DriverBoardPort.Designation;
			Designation = axis.GetAttribute<DisplayAttribute>().ShortName;
			StartingPosition = startingPosition ?? 0;
		}
		public string? Designation { get; set; }
		public string? DriverPort { get; set; }
		public BoardPort DriverBoardPort { get; private set; }
		public BoardPort EndstopBoardPort { get; private set; }
		public int Minimum { get; set; }
		public int Maximum { get; set; }
		public decimal StartingPosition { get; set; }
		public decimal StepsPerMillimeter { get; set; }

		/// <summary>The fastest the axis may go, in mm/min (M203 in config.g): the board holds every move to this.</summary>
		public int MaxSpeed { get; set; } = 500;
		/// <summary>The speed moves use when they do not ask for one, in mm/min; 0 means the max speed.</summary>
		public int MoveSpeed { get; set; }
		/// <summary>The speed moves use when they do not ask for one: the move speed, but never more than the max speed.</summary>
		public int DefaultMoveSpeed => MoveSpeed > 0 ? Math.Min(MoveSpeed, MaxSpeed) : MaxSpeed;

		public int Acceleration { get; set; } = 500;

		/// <summary>Maximum instantaneous speed change (jerk) in mm/min.</summary>
		public int MaxInstantaneousSpeedChange { get; set; } = 900;
		public DriverSpec Driver { get; set; }
		public Axis Axis { get; set; }
	}
}
