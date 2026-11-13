using Drivetrain.Robotics.Control;

namespace Drivetrain.Robotics.Process
{
	/// <summary>Asks the board where every axis is, which axes are homed and which outputs are on (see <see cref="DeviceControl.UpdatePositions"/>).</summary>
	public class ReadPositions(DeviceControl device, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			Device.UpdatePositions();
			Device.UpdateOutputs();
		}

		public override string Description => "Read the axis positions and output states.";
	}
}
