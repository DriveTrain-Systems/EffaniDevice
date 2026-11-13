using Drivetrain.Robotics.Control;

namespace Drivetrain.Robotics.Process
{
	public class HomeAll(DeviceControl device, TimeSpan? timeout = null) : Command(device, timeout ?? SettingTimeout(device, s => s.HomeAllTimeoutSeconds))
	{
		protected override void Run()
		{
			Device.HomeAll();
		}

		public override string Description => $"Home all axes.";
	}
}
