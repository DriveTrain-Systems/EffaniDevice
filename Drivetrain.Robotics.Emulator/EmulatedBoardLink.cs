using Drivetrain.Robotics.Control;

namespace Drivetrain.Robotics.Emulator
{
	/// <summary>
	/// Connects a <see cref="DeviceControl"/> to a <see cref="BoardEmulator"/> in place of a COM port. Like a board's USB
	/// connection, it cannot be opened, and stops being open, while the board is restarting.
	/// </summary>
	public sealed class EmulatedBoardLink : IBoardLink
	{
		public EmulatedBoardLink(BoardEmulator emulator)
		{
			_emulator = emulator;
			emulator.Sent += text =>
			{
				if (_open)
					Received?.Invoke(text);
			};
		}
		/// <summary>
		/// Creates an emulated board and has <paramref name="device"/> talk to it instead of a COM port. The board's SD card is
		/// <paramref name="sdFolder"/>; when it has no config.g yet, the config and homing files are generated into it from the
		/// settings as they are now, so the emulated board starts out set up the way a board sent that config would be.
		/// </summary>
		public static BoardEmulator Connect(DeviceControl device, Func<Settings> settings, string sdFolder)
		{
			if (!File.Exists(Path.Combine(sdFolder, "sys", "config.g")))
				new DeviceConfiguration(settings()).GenerateConfigFiles(sdFolder);
			var emulator = new BoardEmulator(sdFolder);
			device.UseLink(new EmulatedBoardLink(emulator));
			return emulator;
		}
		public string Name => "the board emulator";
		public bool IsPresent => _emulator.IsPresent;
		public bool IsOpen => _open && _emulator.IsPresent;
		public void Open()
		{
			if (!_emulator.IsPresent)
				throw new IOException("The board emulator is restarting.");
			_open = true;
		}
		public void Close() => _open = false;
		public void WriteLine(string line)
		{
			if (!IsOpen)
				throw new IOException("The board emulator is not connected (it may be restarting).");
			_emulator.Receive(line + "\n");
		}
		public event Action<string>? Received;

		private readonly BoardEmulator _emulator;
		private volatile bool _open;
	}
}
