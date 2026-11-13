using Drivetrain.Robotics.Control;

namespace Drivetrain.Robotics.Process
{
	/// <summary>Asks a controller board for its diagnostics (M122) and keeps what it replied in <see cref="Command.Result"/>.</summary>
	/// <param name="boardNumber">The board to ask (0 is the main board).</param>
	/// <param name="device">The device the boards belong to.</param>
	/// <param name="completeCallback">Called, on the process engine thread, once the reply has been received (or the request failed).</param>
	/// <param name="timeout">How long to wait for the board; null uses the general board timeout from the settings.</param>
	public class GetBoardDiagnostics(int boardNumber, DeviceControl device, Action<GetBoardDiagnostics>? completeCallback = null, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			Result = Device.GetBoardDiagnostics(BoardNumber);
			_completeCallback?.Invoke(this);
		}

		public override string Description => $"Get diagnostics for board {BoardNumber}.";
		public int BoardNumber { get; } = boardNumber;
		private readonly Action<GetBoardDiagnostics>? _completeCallback = completeCallback;
	}
}
