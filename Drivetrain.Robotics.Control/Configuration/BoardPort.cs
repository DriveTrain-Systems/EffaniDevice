namespace Drivetrain.Robotics.Control
{
	/// <summary>What a board port is: each kind has its own numbering and its own pin names in the config.</summary>
	public enum PortKind
	{
		/// <summary>A stepper motor driver ("1.2": M569, M584).</summary>
		Driver,
		/// <summary>A digital input, such as an endstop ("io4.in": M574).</summary>
		Input,
		/// <summary>A temperature (thermistor) input, which the analog sensors are read from ("temp0": M308).</summary>
		Temp,
		/// <summary>An output, such as a UV lamp switch ("out5": M950).</summary>
		Output,
		/// <summary>The output side of an IO connector ("io2.out": M950); the same connector's input side is <see cref="Input"/> ("io2.in").</summary>
		IoOutput,
		/// <summary>
		/// A board's laser output ("laser": M950). Each board has one, so it is named by its board alone and the port number is
		/// not used. On the Duet 3 Mini 5+ and its expansion boards it is the same pin as out6.
		/// </summary>
		Laser
	}

	/// <summary>
	/// A connection point on a controller board: which board (0 is the main board; expansion boards are addressed by their CAN
	/// board number), which port, and what kind of port it is, which decides how it is named in config.g.
	/// </summary>
	public readonly record struct BoardPort(int Board, int Port, PortKind Kind)
	{
		public static BoardPort Driver(int board, int port) => new(board, port, PortKind.Driver);
		public static BoardPort Input(int board, int port) => new(board, port, PortKind.Input);
		public static BoardPort Temp(int board, int port) => new(board, port, PortKind.Temp);
		public static BoardPort Output(int board, int port) => new(board, port, PortKind.Output);
		public static BoardPort IoOutput(int board, int port) => new(board, port, PortKind.IoOutput);
		public static BoardPort Laser(int board) => new(board, LaserOutputNumber, PortKind.Laser);
		/// <summary>An output that may be wired to an out port or to the board's laser output (the port number is not used then).</summary>
		public static BoardPort OutputOrLaser(PortKind kind, int board, int port) => kind switch
		{
			PortKind.Laser => Laser(board),
			PortKind.IoOutput => IoOutput(board, port),
			_ => Output(board, port)
		};

		/// <summary>
		/// The port's name in config.g: a driver "1.2" (always with its board), an input "io4.in", a temperature input "temp0" or an
		/// output "out5" or a laser output "laser", the last four prefixed with the board on an expansion board ("1.io4.in").
		/// </summary>
		public string Designation => Kind switch
		{
			PortKind.Driver => $"{Board}.{Port}",
			PortKind.Input => $"{BoardPrefix}io{Port}.in",
			PortKind.Temp => $"{BoardPrefix}temp{Port}",
			PortKind.Output => $"{BoardPrefix}out{Port}",
			PortKind.IoOutput => $"{BoardPrefix}io{Port}.out",
			PortKind.Laser => $"{BoardPrefix}laser",
			_ => throw new InvalidOperationException($"Unknown port kind {Kind}")
		};

		/// <summary>
		/// The physical pin, for spotting two ports that are really the same pin under different names: a laser port is the same
		/// pin as its board's out6.
		/// </summary>
		public string Pin => Kind == PortKind.Laser ? Output(Board, LaserOutputNumber).Designation : Designation;

		/// <summary>Throws unless this is a port of the given kind (a driver where a driver is needed, and so on).</summary>
		public BoardPort Expect(PortKind kind, string what) =>
			Kind == kind ? this : throw new ArgumentException($"{what} must be connected to a {kind.ToString().ToLowerInvariant()} port, not a {Kind.ToString().ToLowerInvariant()} port ({Designation}).");

		public override string ToString() => Designation;

		/// <summary>The output the laser pin also is (Duet 3 Mini 5+ and its expansion boards: "(out6,laser,vfd)").</summary>
		public const int LaserOutputNumber = 6;

		private string? BoardPrefix => Board > 0 ? $"{Board}." : null;
	}
}
