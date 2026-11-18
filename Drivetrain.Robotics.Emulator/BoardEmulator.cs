using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Drivetrain.Robotics.Emulator
{
	/// <summary>One axis of the emulated board.</summary>
	public sealed class EmulatedAxis(string letter)
	{
		public string Letter { get; } = letter;
		public double Minimum { get; internal set; }
		public double Maximum { get; internal set; } = 200;
		/// <summary>mm/min (M203).</summary>
		public double MaxSpeed { get; internal set; } = 6000;
		public double StepsPerMm { get; internal set; } = 80;
		public string Driver { get; internal set; } = "";
		/// <summary>Where the endstop is: true at the high end of the travel, false at the low end, null when none is configured (M574).</summary>
		public bool? EndstopAtHighEnd { get; internal set; }
		public bool Homed { get; internal set; }
		/// <summary>Where the board takes the axis to be.</summary>
		public double Position { get; internal set; }
		/// <summary>Where the axis really is on its travel. The board only learns this by homing; it is kept when the board restarts.</summary>
		public double Physical { get; internal set; } = double.NaN;
	}

	/// <summary>What the emulated board looks like at one moment, for showing on screen.</summary>
	public sealed record EmulatorSnapshot(
		IReadOnlyList<(string Letter, double Position, double Physical, bool Homed, double Minimum, double Maximum)> Axes,
		IReadOnlyList<(int Port, string Pin, bool On)> Outputs,
		IReadOnlyList<(int Number, string Name, double Reading)> Sensors,
		bool IsPresent,
		bool IsHalted,
		string Activity,
		double UpTime);

	/// <summary>
	/// Stands in for the controller board (a Duet 3 Mini 5+ running RepRapFirmware) when none is connected. It takes the lines of
	/// G-code the program sends and answers the way the board does, as far as the program relies on it: it runs config.g and the
	/// homing files from a folder that stands for the board's SD card, keeps where each axis is and whether it is homed, takes as
	/// long over a move as the move would take, switches outputs, gives sensor readings, answers object model queries (M409),
	/// receives uploaded files (M28/M29/M471) and restarts (M999). Like the board, it runs one command at a time, in the order
	/// they arrive. It moves nothing: it is for trying the program, not for proving what the hardware will do.
	/// </summary>
	public sealed class BoardEmulator : IDisposable
	{
		/// <param name="sdFolder">The folder that stands for the board's SD card; <c>sys\config.g</c> in it is run when the board starts.</param>
		public BoardEmulator(string sdFolder)
		{
			_sdFolder = Path.GetFullPath(sdFolder);
			Directory.CreateDirectory(Path.Combine(_sdFolder, "sys"));
			// the board has "been on for a while" when the program starts: its first start takes no time
			_instant = true;
			PowerUp();
			_instant = false;
			_present = true;
			_worker = new Thread(Run) { IsBackground = true, Name = "Board emulator" };
			_worker.Start();
		}

		#region For the program
		/// <summary>
		/// How many times faster than real time the emulated board works: 1 takes as long over a move, a dwell or a restart as the
		/// board would, 10 a tenth of that. Timeouts in the program are not scaled, so 1 is the honest test of them.
		/// </summary>
		public double TimeScale { get; set; } = 1;
		/// <summary>False while the board is restarting: its USB connection is gone until it has started again.</summary>
		public bool IsPresent => _present;
		/// <summary>Takes text from the program: complete lines are queued and run in order.</summary>
		public void Receive(string text)
		{
			List<string> lines = new();
			lock (_inputBuffer)
			{
				_inputBuffer.Append(text);
				var all = _inputBuffer.ToString();
				var end = all.LastIndexOf('\n');
				if (end < 0)
					return;
				_inputBuffer.Remove(0, end + 1);
				lines.AddRange(all.Substring(0, end).Split('\n').Select(l => l.TrimEnd('\r')));
			}
			foreach (var line in lines)
			{
				if (_present)
					_input.Add(line);
			}
		}
		/// <summary>Text the board sends back (whole lines, each ending in a line break).</summary>
		public event Action<string>? Sent;
		/// <summary>Every line in either direction, for a log: the line, and whether the program sent it (true) or the board did.</summary>
		public event Action<string, bool>? Traffic;
		/// <summary>Sets what a sensor reads (M308 S number); readings vary a little around it. Until set, a sensor reads about 500.</summary>
		public void SetSensorReading(int number, double reading)
		{
			lock (_state)
				_sensorLevels[number] = reading;
		}
		/// <summary>Puts an axis somewhere on its travel without the board knowing, as if it had been moved by hand with the power off.</summary>
		public void SetPhysicalPosition(string letter, double position)
		{
			lock (_state)
			{
				if (FindAxis(letter) is EmulatedAxis axis)
					axis.Physical = Math.Clamp(position, axis.Minimum, axis.Maximum);
			}
		}
		public EmulatorSnapshot Snapshot()
		{
			lock (_state)
			{
				return new EmulatorSnapshot(
					_axes.Select(a => (a.Letter, a.Position, PhysicalOf(a), a.Homed, a.Minimum, a.Maximum)).ToList(),
					_outputs.OrderBy(o => o.Key).Select(o => (o.Key, o.Value.Pin, o.Value.Pwm > 0)).ToList(),
					_sensors.OrderBy(s => s.Key).Select(s => (s.Key, s.Value, SensorLevel(s.Key))).ToList(),
					_present,
					_halted,
					_activity,
					UpTime);
			}
		}
		public void Dispose()
		{
			_disposed = true;
			_input.CompleteAdding();
			_wake.Set();
		}
		#endregion

		#region The board's main loop
		private void Run()
		{
			try
			{
				foreach (var line in _input.GetConsumingEnumerable())
				{
					if (_disposed)
						return;
					Traffic?.Invoke(line, true);
					try
					{
						HostLine(line);
					}
					catch (ResetException)
					{
						Restart();
					}
					catch (Exception ex)
					{
						Send($"Error: {ex.Message}");
					}
					SetActivity("idle");
				}
			}
			catch (Exception) when (_disposed)
			{
			}
		}
		/// <summary>The board restarts (M999): it drops off USB, forgets everything but where its axes really are, and runs config.g again.</summary>
		private void Restart()
		{
			_present = false;
			SetActivity("restarting");
			while (_input.TryTake(out _)) { } // what was still to come is lost
			lock (_inputBuffer)
				_inputBuffer.Clear();
			Wait(TimeSpan.FromSeconds(1.5));
			PowerUp();
			while (_input.TryTake(out _)) { }
			_present = true;
			SetActivity("idle");
		}
		private void PowerUp()
		{
			lock (_state)
			{
				var before = _axes.ToDictionary(a => a.Letter, a => a.Physical);
				_axes.Clear();
				foreach (var letter in new[] { "X", "Y", "Z" })
					_axes.Add(new EmulatedAxis(letter) { Physical = before.TryGetValue(letter, out var p) ? p : double.NaN });
				_remembered = before;
				_outputs.Clear();
				_sensors.Clear();
				_relative = false;
				_feed = 3000;
				_halted = false;
				_allowUnhomedMoves = false;
				_upload = null;
				_wifiOn = false;
				_started = DateTime.UtcNow;
			}
			SetActivity("running config.g");
			try
			{
				RunMacro("config.g", 0, required: false);
			}
			catch (ResetException)
			{
				// a restart asked for by config.g itself would never end
			}
			catch (Exception ex)
			{
				Send($"Error in start-up file config.g: {ex.Message}");
			}
		}
		private void Send(string line)
		{
			Traffic?.Invoke(line, false);
			if (_present)
				Sent?.Invoke(line + "\n");
		}
		private void SetActivity(string activity)
		{
			lock (_state)
				_activity = activity;
		}
		/// <summary>Lets time pass on the board: scaled by <see cref="TimeScale"/>, none during the first start, cut short when the emulator is disposed.</summary>
		private void Wait(TimeSpan time)
		{
			if (_instant || _disposed || time <= TimeSpan.Zero)
				return;
			var scaled = TimeSpan.FromTicks((long)(time.Ticks / Math.Max(0.01, TimeScale)));
			if (scaled > TimeSpan.Zero)
				_wake.Wait(scaled);
		}
		private double UpTime => (DateTime.UtcNow - _started).TotalSeconds;
		private sealed class ResetException : Exception;
		#endregion

		#region Lines and files
		/// <summary>A line from the program: part of a file being uploaded, or a line of G-code.</summary>
		private void HostLine(string line)
		{
			if (_upload is not null)
			{
				// everything up to M29 goes into the file, not to the board
				if (Regex.IsMatch(line, @"^\s*(N\d+\s+)?M29\b", RegexOptions.IgnoreCase))
				{
					var (path, text) = _upload.Value;
					_upload = null;
					Directory.CreateDirectory(Path.GetDirectoryName(path)!);
					File.WriteAllText(path, text.ToString());
					Send("Done saving file.");
				}
				else
				{
					_upload.Value.Text.AppendLine(line);
				}
				return;
			}
			RunLine(line.Trim());
		}
		/// <summary>Runs a file from the SD card's sys folder, with RepRapFirmware's <c>if</c> / <c>elif</c> / <c>else</c> blocks, which go by indentation.</summary>
		private void RunMacro(string name, int depth, bool required = true)
		{
			if (depth > 8)
				throw new InvalidOperationException($"macro files are nested too deeply at {name}");
			var path = SdPath(name, "sys");
			if (!File.Exists(path))
			{
				if (required)
					throw new InvalidOperationException($"Macro file {Path.GetFileName(path)} not found");
				return;
			}
			int? skipDeeperThan = null;
			var taken = new Dictionary<int, bool>(); // by indentation: whether a branch of the if at that depth has run
			foreach (var raw in File.ReadAllLines(path))
			{
				var text = StripComment(raw);
				if (text.Trim().Length == 0)
					continue;
				var indent = text.Length - text.TrimStart().Length;
				text = text.Trim();
				if (skipDeeperThan is int skip)
				{
					if (indent > skip)
						continue;
					skipDeeperThan = null;
				}
				if (text.StartsWith("if ", StringComparison.Ordinal))
				{
					var met = Condition(text.Substring(3));
					taken[indent] = met;
					if (!met)
						skipDeeperThan = indent;
				}
				else if (text.StartsWith("elif ", StringComparison.Ordinal))
				{
					var met = !(taken.TryGetValue(indent, out var done) && done) && Condition(text.Substring(5));
					if (met)
						taken[indent] = true;
					else
						skipDeeperThan = indent;
				}
				else if (text == "else")
				{
					if (taken.TryGetValue(indent, out var done) && done)
						skipDeeperThan = indent;
				}
				else if (Regex.IsMatch(text, @"^(while|var|global|set|break|continue|abort)\b"))
				{
					Send($"Error: the board emulator does not run '{text.Split(' ')[0]}' in {Path.GetFileName(path)}");
					skipDeeperThan = indent;
				}
				else
				{
					RunLine(text, depth);
				}
			}
		}
		private bool Condition(string text)
		{
			try
			{
				return Expression.Truth(Expression.Evaluate(text, ObjectModelValue));
			}
			catch (FormatException ex)
			{
				throw new InvalidOperationException($"in 'if {text.Trim()}': {ex.Message}");
			}
		}
		private static string StripComment(string line)
		{
			var quoted = false;
			for (var i = 0; i < line.Length; i++)
			{
				if (line[i] == '"')
					quoted = !quoted;
				else if (line[i] == ';' && !quoted)
					return line.Substring(0, i);
			}
			return line;
		}
		/// <summary>Where a file named in G-code ("0:/sys/config.g", "config.g") is in the SD card folder; never outside it.</summary>
		private string SdPath(string name, string defaultFolder)
		{
			var relative = name.Trim().Trim('"').Replace('\\', '/');
			if (relative.Length > 1 && relative[1] == ':')
				relative = relative.Substring(2);
			var rooted = relative.StartsWith('/');
			relative = relative.TrimStart('/');
			var path = Path.GetFullPath(Path.Combine(_sdFolder, rooted || relative.Contains('/') ? relative : Path.Combine(defaultFolder, relative)));
			if (!path.StartsWith(_sdFolder, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException($"{name} is not on the SD card");
			return path;
		}
		/// <summary>One line of G-code: a meta command (echo), or one or more G and M commands.</summary>
		private void RunLine(string line, int depth = 0)
		{
			line = StripComment(line).Trim();
			line = Regex.Replace(line, @"^N\d+\s+", ""); // a line number
			line = Regex.Replace(line, @"\*\d+\s*$", ""); // a checksum
			if (line.Length == 0)
				return;
			if (line.StartsWith("echo", StringComparison.Ordinal) && (line.Length == 4 || char.IsWhiteSpace(line[4])))
			{
				Echo(line.Substring(4));
				return;
			}
			foreach (var command in SplitCommands(line))
				Execute(command, depth);
		}
		private void Echo(string arguments)
		{
			var parts = new List<string>();
			foreach (var argument in SplitOutsideQuotes(arguments, ','))
			{
				try
				{
					parts.Add(Expression.Text(Expression.Evaluate(argument, ObjectModelValue)));
				}
				catch (FormatException ex)
				{
					throw new InvalidOperationException($"in 'echo{arguments}': {ex.Message}");
				}
			}
			Send(string.Join(" ", parts));
		}
		private static IEnumerable<string> SplitOutsideQuotes(string text, char separator)
		{
			var start = 0;
			var quoted = false;
			for (var i = 0; i < text.Length; i++)
			{
				if (text[i] == '"')
					quoted = !quoted;
				else if (text[i] == separator && !quoted)
				{
					yield return text.Substring(start, i - start);
					start = i + 1;
				}
			}
			yield return text.Substring(start);
		}
		/// <summary>Several commands may share a line ("G91 G1 Z-5"), except after a command whose text runs to the end of the line.</summary>
		private static List<string> SplitCommands(string line)
		{
			var commands = new List<string>();
			var start = 0;
			var quoted = false;
			for (var i = 1; i < line.Length; i++)
			{
				if (line[i] == '"')
					quoted = !quoted;
				if (quoted || !char.IsWhiteSpace(line[i - 1]) || line[i] is not ('G' or 'M') || i + 1 >= line.Length || !char.IsDigit(line[i + 1]))
					continue;
				var first = Regex.Match(line.Substring(start), @"^[GM]\d+").Value;
				if (first is "M28" or "M30" or "M98" or "M117" or "M471" or "M550" or "M587" or "M118")
					break;
				commands.Add(line.Substring(start, i - start).Trim());
				start = i;
			}
			commands.Add(line.Substring(start).Trim());
			return commands;
		}
		#endregion

		#region Commands
		private sealed class Arguments
		{
			public Arguments(string text, Func<string, object?> objectModel)
			{
				foreach (Match m in Regex.Matches(text, @"([A-Za-z])\s*(""(?:[^""]|"""")*""|\{[^}]*\}|[^\s""]*)"))
				{
					var value = m.Groups[2].Value;
					if (value.StartsWith('{'))
						value = Expression.Text(Expression.Evaluate(value, objectModel));
					_values[char.ToUpperInvariant(m.Groups[1].Value[0])] = value;
				}
				Rest = text.Trim();
			}
			private readonly Dictionary<char, string> _values = new();
			/// <summary>Everything after the command's code.</summary>
			public string Rest { get; }
			public bool Has(char letter) => _values.ContainsKey(letter);
			public string? Text(char letter) => _values.TryGetValue(letter, out var value) ? value.Trim('"').Replace("\"\"", "\"") : null;
			public double? Number(char letter) =>
				_values.TryGetValue(letter, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
			public IEnumerable<char> Letters => _values.Keys;
		}
		private void Execute(string command, int depth)
		{
			var match = Regex.Match(command, @"^([GMT])(\d+(?:\.\d+)?)\s*(.*)$", RegexOptions.Singleline);
			if (!match.Success)
			{
				Send($"Error: Bad command: {command}");
				return;
			}
			var code = match.Groups[1].Value + match.Groups[2].Value;
			if (_halted && code != "M999")
			{
				Send($"Error: {code}: the board has been stopped (M112); it runs nothing until it is restarted (M999)");
				return;
			}
			Arguments args;
			try
			{
				args = new Arguments(match.Groups[3].Value, ObjectModelValue);
			}
			catch (FormatException ex)
			{
				Send($"Error: {code}: {ex.Message}");
				return;
			}
			SetActivity(command);
			try
			{
				switch (code)
				{
					case "G0" or "G1": Move(code, args, arc: false); break;
					case "G2" or "G3": Move(code, args, arc: true); break;
					case "G4": Wait(TimeSpan.FromMilliseconds(args.Number('P') ?? (args.Number('S') ?? 0) * 1000)); break;
					case "G28": Home(args, depth); break;
					case "G90": _relative = false; break;
					case "G91": _relative = true; break;
					case "M17" or "M18" or "M82" or "M83" or "M84" or "M106" or "M107" or "M117" or "M118" or "M140" or "M201" or "M204"
						or "M350" or "M400" or "M555" or "M566" or "M569" or "M575" or "M586" or "M595" or "M669" or "M906" or "M913":
						break; // taken in, with nothing to emulate
					case "M28": BeginUpload(args); break;
					case "M29": break; // no upload under way
					case "M30": File.Delete(SdPath(args.Rest, "gcodes")); break;
					case "M42": SwitchOutput(args); break;
					case "M92": ForEachAxis(args, (axis, value) => axis.StepsPerMm = value); break;
					case "M98": RunMacro(args.Text('P') ?? throw new InvalidOperationException("M98: missing P parameter"), depth + 1); break;
					case "M112": _halted = true; Send("Emergency stop: the board runs nothing until it is restarted (M999)"); break;
					case "M114": ReportPositions(); break;
					case "M115": Send($"FIRMWARE_NAME: RepRapFirmware for Duet 3 Mini 5+ FIRMWARE_VERSION: {FirmwareVersion} ELECTRONICS: Duet 3 Mini5plus WiFi (board emulator) FIRMWARE_DATE: 2026-01-01"); break;
					case "M122": Diagnostics(args); break;
					case "M203": ForEachAxis(args, (axis, value) => axis.MaxSpeed = value); break;
					case "M208": SetLimits(args); break;
					case "M308": ConfigureSensor(args); break;
					case "M408": Send($"{{\"status\":\"I\",\"upTime\":{UpTime.ToString("0", CultureInfo.InvariantCulture)}}}"); break;
					case "M409": ObjectModelQuery(args); break;
					case "M471": Rename(args); break;
					case "M550": break;
					case "M552": Wifi(args); break;
					case "M564": if (args.Number('H') is double h) _allowUnhomedMoves = h == 0; break;
					case "M574": SetEndstops(args); break;
					case "M584": MapAxes(args); break;
					case "M587": _wifiSsid = args.Text('S'); break;
					case "M950": ConfigureOutput(args); break;
					case "M999": throw new ResetException();
					default: Send($"Error: {code}: Command is not supported"); break;
				}
			}
			catch (ResetException)
			{
				throw;
			}
			catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException or UnauthorizedAccessException)
			{
				Send($"Error: {(ex.Message.StartsWith(code, StringComparison.Ordinal) ? "" : code + ": ")}{ex.Message}");
			}
		}
		private EmulatedAxis? FindAxis(string letter) => _axes.FirstOrDefault(a => string.Equals(a.Letter, letter, StringComparison.OrdinalIgnoreCase));
		private void ForEachAxis(Arguments args, Action<EmulatedAxis, double> apply)
		{
			lock (_state)
			{
				foreach (var axis in _axes)
				{
					if (args.Number(axis.Letter[0]) is double value)
						apply(axis, value);
				}
			}
		}
		/// <summary>M584 X1.0 Y1.2 V0.4 ...: an axis letter that is not there yet becomes a new axis, in the order given.</summary>
		private void MapAxes(Arguments args)
		{
			lock (_state)
			{
				foreach (var letter in args.Letters.Where(l => l != 'E' && l != 'P' && l != 'R' && l != 'S'))
				{
					var axis = FindAxis(letter.ToString());
					if (axis is null)
					{
						axis = new EmulatedAxis(letter.ToString()) { Physical = _remembered.TryGetValue(letter.ToString(), out var p) ? p : double.NaN };
						_axes.Add(axis);
					}
					axis.Driver = args.Text(letter) ?? "";
				}
			}
		}
		/// <summary>M208 X0:240 (both limits), or M208 S1 X0 (minimum) / M208 S0 X240 (maximum).</summary>
		private void SetLimits(Arguments args)
		{
			lock (_state)
			{
				foreach (var axis in _axes)
				{
					var text = args.Text(axis.Letter[0]);
					if (text is null)
						continue;
					var parts = text.Split(':');
					if (parts.Length == 2 && TryNumber(parts[0], out var low) && TryNumber(parts[1], out var high))
					{
						axis.Minimum = low;
						axis.Maximum = high;
					}
					else if (TryNumber(text, out var value))
					{
						if (args.Number('S') == 1)
							axis.Minimum = value;
						else
							axis.Maximum = value;
					}
				}
			}
		}
		private static bool TryNumber(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		/// <summary>M574 X1 P"pin" S1: 1 is an endstop at the low end, 2 at the high end, 0 none.</summary>
		private void SetEndstops(Arguments args) =>
			ForEachAxis(args, (axis, where) => axis.EndstopAtHighEnd = where == 2 ? true : where == 1 ? false : null);
		private void ConfigureOutput(Arguments args)
		{
			if (args.Number('P') is not double port)
				return; // a fan, heater or servo: nothing the program uses
			lock (_state)
				_outputs[(int)port] = (args.Text('C') ?? "", 0);
		}
		private void SwitchOutput(Arguments args)
		{
			var port = (int)(args.Number('P') ?? -1);
			lock (_state)
			{
				if (!_outputs.TryGetValue(port, out var output))
					throw new InvalidOperationException($"M42: Invalid gpio port {port}");
				_outputs[port] = (output.Pin, Math.Clamp(args.Number('S') ?? 0, 0, 1));
			}
		}
		private void ConfigureSensor(Arguments args)
		{
			if (args.Number('S') is not double number)
				throw new InvalidOperationException("M308: missing S parameter");
			lock (_state)
				_sensors[(int)number] = args.Text('A') ?? $"Sensor {number:0}";
		}
		private double SensorLevel(int number) => _sensorLevels.TryGetValue(number, out var level) ? level : 500;
		private void Wifi(Arguments args)
		{
			if (args.Number('S') is not double state)
			{
				Send(_wifiOn ? "WiFi module is connected to access point " + (_wifiSsid ?? "(none)") + ", IP address 192.168.1.50" : "WiFi module is disabled");
				return;
			}
			_wifiOn = state > 0;
			if (!_wifiOn)
				Send("WiFi module stopped");
			else if (string.IsNullOrEmpty(_wifiSsid))
				Send("WiFi module started; no access point has been set (M587)");
			else
				Send($"WiFi module is connected to access point {_wifiSsid}, IP address 192.168.1.50");
		}
		private void BeginUpload(Arguments args)
		{
			var path = SdPath(args.Rest, "gcodes");
			_upload = (path, new StringBuilder());
			Send($"Writing to file: {args.Rest.Trim().Trim('"')}");
		}
		/// <summary>M471 S"from" T"to" D1: renames a file; D1 replaces a file already there.</summary>
		private void Rename(Arguments args)
		{
			var from = SdPath(args.Text('S') ?? throw new InvalidOperationException("M471: missing S parameter"), "sys");
			var to = SdPath(args.Text('T') ?? throw new InvalidOperationException("M471: missing T parameter"), "sys");
			if (!File.Exists(from))
				throw new InvalidOperationException($"M471: Failed to rename file or directory {args.Text('S')}: it does not exist");
			if (File.Exists(to) && args.Number('D') != 1)
				throw new InvalidOperationException($"M471: Failed to rename file or directory {args.Text('S')}: {args.Text('T')} already exists");
			File.Move(from, to, overwrite: true);
		}
		private void ReportPositions()
		{
			string report;
			lock (_state)
			{
				static string Mm(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
				report = string.Join(" ", _axes.Select(a => $"{a.Letter}:{Mm(a.Position)}")) + " E:0.000 Count "
					+ string.Join(" ", _axes.Select(a => Math.Round(a.Position * a.StepsPerMm).ToString("0", CultureInfo.InvariantCulture)))
					+ " Machine " + string.Join(" ", _axes.Select(a => Mm(a.Position))) + " Bed comp 0.000";
			}
			Send(report);
		}
		private void Diagnostics(Arguments args)
		{
			var board = (int)(args.Number('B') ?? 0);
			if (board > 0)
			{
				Send($"Diagnostics for board {board}:");
				Send($"Duet EXP1XD firmware version {FirmwareVersion} (board emulator)");
				Send("Never used RAM 12345, free system stack 180 words");
				Send("VIN voltage: min 23.9, current 24.0, max 24.1");
				Send("CAN messages queued 0, send timeouts 0, received 0, lost 0");
				return;
			}
			Send("=== Diagnostics ===");
			Send($"RepRapFirmware for Duet 3 Mini 5+ version {FirmwareVersion} (board emulator) running on Duet 3 Mini5plus WiFi (standalone mode)");
			Send("Board ID: EMULATOR-0000-0000");
			Send($"=== Platform ===");
			Send($"Last reset {TimeSpan.FromSeconds(UpTime):hh\\:mm\\:ss} ago, cause: software");
			Send("=== Move ===");
			lock (_state)
				Send("Axes homed: " + string.Join(" ", _axes.Select(a => $"{a.Letter}:{(a.Homed ? "yes" : "no")}")));
			Send("=== Network ===");
			Send(_wifiOn ? "WiFi module is connected" : "WiFi module is disabled");
		}
		#endregion

		#region Movement
		/// <summary>The real position of an axis, settled the first time it is needed: a little way off its endstop.</summary>
		private static double PhysicalOf(EmulatedAxis axis)
		{
			if (double.IsNaN(axis.Physical))
				axis.Physical = axis.EndstopAtHighEnd == true ? Math.Max(axis.Minimum, axis.Maximum - 10) : Math.Min(axis.Maximum, axis.Minimum + 10);
			return axis.Physical;
		}
		/// <summary>G28: runs homeall.g, or the homing file of each axis named. The axes to be homed count as not homed from the start.</summary>
		private void Home(Arguments args, int depth)
		{
			List<EmulatedAxis> named;
			lock (_state)
			{
				named = _axes.Where(a => args.Has(a.Letter[0])).ToList();
				foreach (var axis in named.Count == 0 ? _axes : named)
					axis.Homed = false;
			}
			_homing++;
			try
			{
				if (named.Count == 0)
					RunMacro("homeall.g", depth + 1);
				else
				{
					foreach (var axis in named)
						RunMacro($"home{axis.Letter.ToLowerInvariant()}.g", depth + 1);
				}
			}
			finally
			{
				_homing--;
				_relative = false; // homing files end in absolute positioning; the board restores the mode it had, which the program always sets anyway
			}
			List<string> failed;
			lock (_state)
				failed = (named.Count == 0 ? _axes : named).Where(a => !a.Homed).Select(a => a.Letter).ToList();
			if (failed.Count > 0)
				Send($"Error: G28: Homing failed: {string.Join(", ", failed)} not homed");
		}
		/// <summary>
		/// G0/G1 (and G2/G3, which are run as a straight move to the end point). H1 is a homing move, which stops at the endstop and
		/// makes the axis homed; H2 moves an axis whether it is homed or not and ignores the limits; any other move needs the axis
		/// homed and stays inside the limits. The move takes as long as it would at the speed asked for.
		/// </summary>
		private void Move(string code, Arguments args, bool arc)
		{
			if (args.Number('F') is double f && f > 0)
				_feed = f;
			var mode = (int)(args.Number('H') ?? args.Number('S') ?? 0);
			var steps = new List<(EmulatedAxis Axis, double From, double To, double PhysicalFrom, double PhysicalTo, bool Homes)>();
			lock (_state)
			{
				foreach (var axis in _axes)
				{
					if (args.Number(axis.Letter[0]) is not double value)
						continue;
					var from = axis.Position;
					var to = _relative ? from + value : value;
					var physical = PhysicalOf(axis);
					if (mode == 1)
					{
						// a homing move: toward the endstop until it triggers, or for the distance given if that comes first
						if (axis.EndstopAtHighEnd is not bool high)
							throw new InvalidOperationException($"{code}: no endstop is configured for axis {axis.Letter} (M574)");
						var distance = _relative ? value : value - from;
						var trigger = high ? axis.Maximum : axis.Minimum;
						var toTrigger = trigger - physical;
						var reaches = distance != 0 && Math.Sign(distance) == (high ? 1 : -1) && Math.Abs(distance) >= Math.Abs(toTrigger) - 1e-9;
						var travelled = reaches ? toTrigger : distance;
						var end = Math.Clamp(physical + travelled, axis.Minimum, axis.Maximum);
						steps.Add((axis, from, reaches ? trigger : from + (end - physical), physical, end, reaches));
						continue;
					}
					if (mode != 2)
					{
						if (!axis.Homed && !_allowUnhomedMoves)
							throw new InvalidOperationException($"{code}: insufficient axes homed");
						to = Math.Clamp(to, axis.Minimum, axis.Maximum); // the board keeps a move inside the axis limits
					}
					// the board's idea of the position and the real one move together; the real one stops at the ends of the travel
					steps.Add((axis, from, to, physical, Math.Clamp(physical + (to - from), axis.Minimum, axis.Maximum), false));
				}
			}
			if (steps.Count == 0)
				return;

			// the speed is along the line of the move, and no axis goes faster than its maximum
			var length = Math.Sqrt(steps.Sum(s => Math.Pow(s.PhysicalTo - s.PhysicalFrom, 2)));
			var minutes = length / Math.Max(1, _feed);
			foreach (var s in steps)
				minutes = Math.Max(minutes, Math.Abs(s.PhysicalTo - s.PhysicalFrom) / Math.Max(1, s.Axis.MaxSpeed));
			var duration = TimeSpan.FromMinutes(minutes);
			var slices = _instant ? 0 : (int)Math.Min(2000, duration.TotalMilliseconds / Math.Max(0.01, TimeScale) / 50);
			for (var slice = 1; slice <= slices && !_disposed; slice++)
			{
				Wait(TimeSpan.FromTicks(duration.Ticks / (slices + 1)));
				var part = (double)slice / (slices + 1);
				lock (_state)
				{
					foreach (var s in steps)
					{
						s.Axis.Position = s.From + ((s.To - s.From) * part);
						s.Axis.Physical = s.PhysicalFrom + ((s.PhysicalTo - s.PhysicalFrom) * part);
					}
				}
			}
			Wait(TimeSpan.FromTicks(duration.Ticks / (slices + 1)));
			lock (_state)
			{
				foreach (var s in steps)
				{
					s.Axis.Position = Math.Round(s.To, 4);
					s.Axis.Physical = s.PhysicalTo;
					if (s.Homes)
						s.Axis.Homed = true;
				}
			}
		}
		#endregion

		#region Object model
		/// <summary>M409 K"key": replies {"key":"...","flags":"","result":value}; a key with [] gives the value for every item of the list.</summary>
		private void ObjectModelQuery(Arguments args)
		{
			var key = args.Text('K') ?? "";
			object? result;
			try
			{
				result = ObjectModelValue(key);
			}
			catch (FormatException)
			{
				result = null;
			}
			var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?> { ["key"] = key, ["flags"] = args.Text('F') ?? "", ["result"] = result });
			Send(json);
		}
		private object? ObjectModelValue(string path)
		{
			Dictionary<string, object?> root;
			lock (_state)
			{
				static double Mm(double value) => Math.Round(value, 3);
				var highestOutput = _outputs.Count == 0 ? -1 : _outputs.Keys.Max();
				var highestSensor = _sensors.Count == 0 ? -1 : _sensors.Keys.Max();
				root = new Dictionary<string, object?>
				{
					["boards"] = new List<object?> { new Dictionary<string, object?> { ["name"] = "Duet 3 Mini 5+ (board emulator)", ["firmwareVersion"] = FirmwareVersion } },
					["move"] = new Dictionary<string, object?>
					{
						["axes"] = _axes.Select(a => (object?)new Dictionary<string, object?>
						{
							["letter"] = a.Letter,
							["homed"] = a.Homed,
							["machinePosition"] = Mm(a.Position),
							["userPosition"] = Mm(a.Position),
							["min"] = a.Minimum,
							["max"] = a.Maximum,
							["speed"] = a.MaxSpeed,
							["stepsPerMm"] = a.StepsPerMm,
						}).ToList(),
					},
					["network"] = new Dictionary<string, object?> { ["name"] = "Effani" },
					["sensors"] = new Dictionary<string, object?>
					{
						["analog"] = Enumerable.Range(0, highestSensor + 1).Select(n => _sensors.TryGetValue(n, out var name)
							? (object?)new Dictionary<string, object?> { ["name"] = name, ["type"] = "linearanalog", ["lastReading"] = Math.Round(SensorLevel(n) + ((_random.NextDouble() - 0.5) * 20), 1) }
							: null).ToList(),
					},
					["state"] = new Dictionary<string, object?>
					{
						["gpOut"] = Enumerable.Range(0, highestOutput + 1).Select(n => _outputs.TryGetValue(n, out var output)
							? (object?)new Dictionary<string, object?> { ["pwm"] = output.Pwm }
							: null).ToList(),
						["status"] = _halted ? "halted" : "idle",
						["upTime"] = Math.Round(UpTime),
					},
				};
			}
			return Resolve(root, path.Split('.', StringSplitOptions.RemoveEmptyEntries), 0);
		}
		private static object? Resolve(object? node, string[] segments, int at)
		{
			if (at >= segments.Length || node is null)
				return node;
			var match = Regex.Match(segments[at], @"^(\w+)(?:\[(\d*)\])?$");
			if (!match.Success)
				throw new FormatException($"'{segments[at]}' is not part of an object model name");
			if (node is not Dictionary<string, object?> table || !table.TryGetValue(match.Groups[1].Value, out var value))
				throw new FormatException($"the board has no value '{string.Join(".", segments)}'");
			if (!match.Groups[2].Success)
				return Resolve(value, segments, at + 1);
			if (value is not List<object?> list)
				throw new FormatException($"'{match.Groups[1].Value}' is not a list");
			if (match.Groups[2].Value.Length == 0)
				return list.Select(item => Resolve(item, segments, at + 1)).ToList();
			var index = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
			if (index >= list.Count)
				throw new FormatException($"'{match.Groups[1].Value}' has no item {index}");
			return Resolve(list[index], segments, at + 1);
		}
		#endregion

		#region Fields
		public const string FirmwareVersion = "3.7.0";
		private readonly string _sdFolder;
		private readonly Thread _worker;
		private readonly BlockingCollection<string> _input = new();
		private readonly StringBuilder _inputBuffer = new();
		private readonly ManualResetEventSlim _wake = new(false);
		private readonly Random _random = new();
		private volatile bool _present;
		private volatile bool _disposed;
		private bool _instant;

		/// <summary>Guards everything below, which the worker changes and a snapshot reads.</summary>
		private readonly object _state = new();
		private readonly List<EmulatedAxis> _axes = new();
		private Dictionary<string, double> _remembered = new();
		private readonly Dictionary<int, (string Pin, double Pwm)> _outputs = new();
		private readonly Dictionary<int, string> _sensors = new();
		private readonly Dictionary<int, double> _sensorLevels = new();
		private string _activity = "starting";
		private DateTime _started = DateTime.UtcNow;
		private bool _relative;
		private double _feed = 3000;
		private bool _halted;
		private bool _allowUnhomedMoves;
		private int _homing;
		private bool _wifiOn;
		private string? _wifiSsid;
		private (string Path, StringBuilder Text)? _upload;
		#endregion
	}
}
