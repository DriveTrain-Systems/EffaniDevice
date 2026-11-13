using Drivetrain.Robotics.Control;

namespace Drivetrain.Robotics.Process
{
	/// <summary>Sends a G-code command to the controller board.</summary>
	/// <param name="command">The command text.</param>
	/// <param name="device">The device to send it to.</param>
	/// <param name="reportResponse">
	/// When true, waits for the board to finish the command, including any moves it started (M400), then keeps what it replied in
	/// <see cref="Command.Result"/> and the process engine reports it as messages. When false (the default) waits only for the board
	/// to acknowledge the command; anything it replies is published through <see cref="DeviceControl.DataReceived"/>.
	/// </param>
	/// <param name="timeout">How long to wait for the board; null uses the general board timeout from the settings.</param>
	public class SendCommand(string command, DeviceControl device, bool reportResponse = false, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			if (ReportResponse)
				Result = Device.SendCommandForResponse(_command);
			else
				Device.SendCommand(_command);
		}

		/// <summary>True if the board's response to the command is waited for and reported.</summary>
		public bool ReportResponse { get; } = reportResponse;

		public override string Description => $"Send {_command}.";
		private readonly string _command = command;
	}
}
