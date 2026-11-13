using Drivetrain.Robotics.Control;

namespace Drivetrain.Robotics.Process
{
	public class MoveRelative(decimal distance, DeviceControl device, AxisSpec axisSpec = null, TimeSpan? timeout = null) : Command(device, timeout ?? SettingTimeout(device, s => s.MoveTimeoutSeconds))
	{
		protected override void Run()
		{
			if (AxisSpec is null)
				throw new ApplicationException($"{nameof(AxisSpec)} cannot be null in {nameof(MoveRelative)}");

			Device.MoveRelative(AxisSpec, Distance);
		}

		public override string Description => $"Move {AxisSpec.Label} {Distance}mm";

		public AxisSpec AxisSpec { get; protected set; } = axisSpec;
		public decimal Distance { get; } = distance;
	}
}
