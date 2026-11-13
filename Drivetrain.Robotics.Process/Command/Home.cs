using Drivetrain.Robotics.Control;

namespace Drivetrain.Robotics.Process
{
	public class Home(DeviceControl device, AxisSpec axisSpec = null, TimeSpan? timeout = null) : Command(device, timeout ?? SettingTimeout(device, s => s.HomeAxisTimeoutSeconds))
	{
		protected override void Run()
		{
			Device.Home(AxisSpec);
		}

		public override string Description => $"Home {AxisSpec.Label}.";
		public AxisSpec AxisSpec { get; set; } = axisSpec;
	}
}
