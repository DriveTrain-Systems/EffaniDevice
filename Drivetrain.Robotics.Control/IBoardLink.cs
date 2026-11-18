namespace Drivetrain.Robotics.Control
{
	/// <summary>
	/// A connection to a controller board that is not a COM port: lines of G-code go out, the board's reply text comes back.
	/// <see cref="DeviceControl.UseLink"/> makes the device talk over one instead of the serial port; the board emulator
	/// (Drivetrain.Robotics.Emulator) is one, so the program can be run without a board.
	/// </summary>
	public interface IBoardLink
	{
		/// <summary>What the connection is called in messages, such as "the board emulator".</summary>
		string Name { get; }
		/// <summary>True while the board is there to be connected to; false while it is restarting, as a board's USB port goes away then.</summary>
		bool IsPresent { get; }
		bool IsOpen { get; }
		/// <summary>Opens the connection; throws an <see cref="System.IO.IOException"/> when the board is not there.</summary>
		void Open();
		void Close();
		/// <summary>Sends one line to the board; throws an <see cref="System.IO.IOException"/> when the connection is not open.</summary>
		void WriteLine(string line);
		/// <summary>Text the board sent, in pieces that need not end at a line break.</summary>
		event Action<string>? Received;
	}
}
