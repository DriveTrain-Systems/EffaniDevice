using Serilog;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Linq;
using System.Text;

namespace Drivetrain.Robotics.Control
{
	public class DeviceControl : IDisposable
	{
		#region Constructors
		/// <param name="settingsProvider">
		/// Returns the settings currently in effect. It is called whenever the <see cref="Configuration"/> has to be (re)built, so
		/// settings that were changed, activated or reset in the meantime are picked up (see <see cref="ResetConfiguration"/>).
		/// </param>
		public DeviceControl(Func<Settings> settingsProvider)
		{
			_settingsProvider = settingsProvider;
		}
		#endregion

		#region Public methods
		/// <summary>
		/// Creates the serial port object and starts the thread that publishes data read from it. Safe to call more than once;
		/// it is also called by the first operation that needs the port, so calling it explicitly is only needed to start
		/// publishing <see cref="DataReceived"/> early. Does not open the port.
		/// </summary>
		public void Initialize()
		{
			lock (_initializeLock)
			{
				if (_initialized)
					return;

				var port = new SerialPort();
				port.BaudRate = BaudRate; // SetPortBaudRate(port.BaudRate);
                port.Parity = Parity.None; // SetPortParity(port.Parity);
				port.DataBits = 8; // SetPortDataBits(port.DataBits);
				port.StopBits = StopBits.One; // SetPortStopBits(port.StopBits);
				port.Handshake = Handshake.None; // SetPortHandshake(port.Handshake);
				// assert DTR/RTS the way terminal programs do: the board's USB serial treats a port without DTR as having no host
				// listening, and may then discard its replies (so the "ok" for a command never arrives)
				port.DtrEnable = true;
				port.RtsEnable = true;
				port.ReadTimeout = 500;
				port.WriteTimeout = 500;
				_serialPort = port;
				_publishThread = new Thread(InitializeReadData);
				_publishThread.Start();
				_initialized = true;
			}
		}
		/// <summary>Switches output port <paramref name="accDesignation"/> (M950 P) off; returns null, or the error if it could not be switched.</summary>
		public string? AccOff(int accDesignation)
		{
			var error = WriteWhenReady($"M42 P{accDesignation} S0");
			UpdateOutputs();
			return error;
		}
		/// <summary>Switches output port <paramref name="accDesignation"/> (M950 P) on; returns null, or the error if it could not be switched.</summary>
		public string? AccOn(int accDesignation)
		{
			var error = WriteWhenReady($"M42 P{accDesignation} S1");
			UpdateOutputs();
			return error;
		}
		/// <summary>
		/// Asks the board which output ports (M950 P) are on (state.gpOut[].pwm) and keeps the answer in <see cref="OutputStates"/>,
		/// raising <see cref="OutputsChanged"/> (also when the board could not be asked, so a switch shown on screen goes back to
		/// what was last known). Called after every output is switched.
		/// </summary>
		public void UpdateOutputs()
		{
			try
			{
				const string key = "state.gpOut[].pwm";
				var json = Transact($"M409 K\"{key}\"", waitForMoves: false)
					.FirstOrDefault(l => l.StartsWith("{\"key\"", StringComparison.Ordinal) && l.Contains($"\"{key}\""));
				if (json is not null)
				{
					using var document = System.Text.Json.JsonDocument.Parse(json);
					if (document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == System.Text.Json.JsonValueKind.Array)
					{
						var states = new Dictionary<int, bool>();
						var port = 0;
						foreach (var pwm in result.EnumerateArray())
						{
							if (pwm.ValueKind == System.Text.Json.JsonValueKind.Number)
								states[port] = pwm.GetDouble() > 0;
							port++;
						}
						OutputStates = states;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "Could not read the output states from the controller board");
			}
			OutputsChanged?.Invoke(this, EventArgs.Empty);
		}
		public void ApplyConfig()
		{
			// config.g waits for the expansion boards and WiFi, so it takes longer than most commands
			using (OverrideReadyTimeout(TimeoutFor(Settings.ApplyConfigTimeoutSeconds)))
				WriteWhenReady($"M98 P\"config.g\"");
		}
		/// <summary>
		/// Closes the serial port so another program (a terminal) can use it; Windows lets only one program have a COM port open.
		/// The next command reopens it. Returns the port's name, or null if it was not open. Call only while no command is running.
		/// </summary>
		public string? ClosePort()
		{
			if (!_initialized || !_serialPort.IsOpen)
				return null;
			var name = _serialPort.PortName;
			_serialPort.Close();
			return name;
		}
		public virtual void Dispose()
		{
			_disposing = true;
			if (_initialized)
				_serialPort.Close();
		}
		/// <summary>
		/// Discards the device configuration so that it is rebuilt from the current settings the next time it is needed.
		/// Call this only while nothing is running (no assay, empty process queue): anything holding axis specifications
		/// from the old configuration keeps using the old values.
		/// </summary>
		public void ResetConfiguration()
		{
			lock (_configurationLock)
			{
				_configuration = null;
				OnConfigurationReset();
			}
		}
		/// <summary>Called, with the configuration lock held, when the configuration is discarded; derived classes drop what they built from it.</summary>
		protected virtual void OnConfigurationReset() { }
		/// <summary>Asks a board for its diagnostics (M122) and returns what it replied; a failure is returned as text starting with "Error".</summary>
		public string GetBoardDiagnostics(int boardId)
		{
			return SendCommandForResponse($"M122 B{boardId}");
		}
		/// <summary>
		/// Sends a command and waits for the board to acknowledge it ("ok"), without waiting for any moves it started to finish.
		/// Anything the board replied is shown in the console messages. Throws if the board does not acknowledge it in time.
		/// </summary>
		public void SendCommand(string command)
		{
			try
			{
				Publish(Transact(command, waitForMoves: false));
			}
			catch (Exception ex)
			{
				ClosePortAfterError(ex);
				throw;
			}
		}
		/// <summary>
		/// Sends a command, waits for the board to finish it (including any moves it started), and returns what the board replied
		/// (one line per reply line, without the "ok" acknowledgements), or an empty string if it did not reply. A failure is
		/// returned as text starting with "Error".
		/// </summary>
		public string SendCommandForResponse(string command)
		{
			try
			{
				return String.Join(Environment.NewLine, Transact(command, waitForMoves: true));
			}
			catch (Exception ex)
			{
				Log.Error(ex, $"Error sending to the controller board: {command}");
				ClosePortAfterError(ex);
				return $"Error: {ex.Message}";
			}
		}
		public void Home(AxisSpec axisSpec)
		{
			WriteWhenReady($"G28 {axisSpec.Designation}");
			UpdatePositions();
		}
		public void HomeAll()
		{
			WriteWhenReady($"G28");
			UpdatePositions();
		}
		public void Move(List<MoveSpec> moveSpecs, decimal speed)
		{
			var moveCode = String.Join(' ', moveSpecs.Select(s => s.ToGode()));
			WriteWhenReady("G90");
			WriteWhenReady($"G1 {moveCode} F{speed}");
			UpdatePositions();
		}
		public void MoveAbsolute(List<MoveSpec> moveSpecs, decimal speed)
		{
			var moveCode = String.Join(' ', moveSpecs.Select(s => s.ToGode()));
			WriteWhenReady("G90");
			var formula = $"G1 {moveCode} F{speed}";
			WriteWhenReady(formula);
			UpdatePositions();
		}
		public void MoveAbsolute(AxisSpec axisSpec, decimal location, decimal? speed = null)
		{
			if (location < axisSpec.Minimum || location > axisSpec.Maximum)
				return;

			if (!speed.HasValue)
				speed = MoveSpeedFor(axisSpec);
			WriteWhenReady("G90");
			WriteWhenReady($"G1 {axisSpec.Designation}{location} F{speed}");
			UpdatePositions();
		}
		/// <summary>
		/// The speed a move of <paramref name="axisSpec"/> uses when it does not ask for one: the axis's Move Speed as the settings
		/// have it now, at most its Max Speed. The settings are read at the time of the move, so a change takes effect with the next
		/// move, not only once the configuration is rebuilt (when Utilities is opened or an assay starts).
		/// </summary>
		public int MoveSpeedFor(AxisSpec axisSpec)
		{
			var settings = _settingsProvider();
			(int move, int max) = axisSpec.Axis switch
			{
				Axis.TableX => (settings.TableXMoveSpeed, settings.TableXMaxSpeed),
				Axis.TableY => (settings.TableYMoveSpeed, settings.TableYMaxSpeed),
				Axis.Aspirate => (settings.AspirateMoveSpeed, settings.AspirateMaxSpeed),
				Axis.DnaReagent => (settings.DnaReagentMoveSpeed, settings.DnaReagentMaxSpeed),
				Axis.Koh => (settings.KohMoveSpeed, settings.KohMaxSpeed),
				Axis.Nadh => (settings.NadhMoveSpeed, settings.NadhMaxSpeed),
				Axis.Basal => (settings.BasalMoveSpeed, settings.BasalMaxSpeed),
				_ => (axisSpec.MoveSpeed, axisSpec.MaxSpeed)
			};
			if (max <= 0)
				max = axisSpec.MaxSpeed;
			return move > 0 ? Math.Min(move, max) : max;
		}
		public void MoveToStarting(AxisSpec axisSpec)
		{
			var speed = MoveSpeedFor(axisSpec);
			WriteWhenReady("G90");
			WriteWhenReady($"G1 {axisSpec.Designation}{axisSpec.StartingPosition} F{speed}");
			UpdatePositions();
		}
		public void MoveRelativeClockwiseArc(List<MoveSpec> moveSpecs, decimal speed)
		{
			var moveCode = String.Join(' ', moveSpecs.Select(s => s.ToGode()));
			WriteWhenReady("G91");
			var formula = $"G2 {moveCode} R4.5 F{speed}";
			WriteWhenReady(formula);
			UpdatePositions();
		}
		public void MoveRelativeCounterClockwiseArc(List<MoveSpec> moveSpecs, decimal speed)
		{
			var moveCode = String.Join(' ', moveSpecs.Select(s => s.ToGode()));
			WriteWhenReady("G91");
			var formula = $"G3 {moveCode} R4.5 F{speed}";
			WriteWhenReady(formula);
			UpdatePositions();
		}
		public void MovePercent(AxisSpec axisSpec, decimal percent, decimal? speed = null)
		{
			if (!speed.HasValue)
				speed = MoveSpeedFor(axisSpec);
			var location = (axisSpec.Maximum - axisSpec.Minimum) * (percent / 100);
			if (location < axisSpec.Minimum)
				location = axisSpec.Minimum;
			if (location > axisSpec.Maximum)
				location = axisSpec.Maximum;

			WriteWhenReady("G90");
			WriteWhenReady($"G1 {axisSpec.Designation}{location} F{speed}");
			UpdatePositions();
		}
		// Moves are sent as G1, not G0: G1 always runs at the F speed it is given (capped by M203), whereas the firmware can run
		// G0 as a rapid move at the axis's maximum speed and ignore F.
		public void MoveRelative(AxisSpec axisSpec, decimal distance, decimal? speed = null)
		{
			if (!speed.HasValue)
				speed = MoveSpeedFor(axisSpec);

			WriteWhenReady("G91");
			WriteWhenReady($"G1 {axisSpec.Designation}{distance} F{speed}");
			UpdatePositions();
		}
		/// <summary>
		/// Asks the board where every axis is (M114) and keeps the answer in <see cref="Positions"/>, raising
		/// <see cref="PositionsChanged"/>. Called after every move and home. A failure is logged and leaves the last positions.
		/// </summary>
		public void UpdatePositions()
		{
			try
			{
				// "X:12.000 Y:0.000 Z:0.000 V:27.000 ... E:0.000 Count 0 0 ... Machine ... Bed comp 0.000": the user positions come first
				var line = Transact("M114", waitForMoves: false).FirstOrDefault(l => l.StartsWith("X:", StringComparison.Ordinal));
				if (line is null)
					return;
				var userPart = line.Split(" Count", 2)[0];
				var positions = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
				foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(userPart, @"([A-Za-z]):\s*(-?\d+(?:\.\d+)?)"))
				{
					if (m.Groups[1].Value != "E" && decimal.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var position))
						positions[m.Groups[1].Value] = position;
				}
				Positions = positions;
				Homed = ReadHomed();
				PositionsChanged?.Invoke(this, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "Could not read the axis positions from the controller board");
			}
		}
		/// <summary>Which axes the board has homed, by axis letter, from the object model (move.axes[].letter and .homed).</summary>
		private IReadOnlyDictionary<string, bool> ReadHomed()
		{
			static List<System.Text.Json.JsonElement> Array(List<string> reply, string key)
			{
				var json = reply.FirstOrDefault(l => l.StartsWith("{\"key\"", StringComparison.Ordinal) && l.Contains($"\"{key}\""));
				if (json is null)
					return new();
				using var document = System.Text.Json.JsonDocument.Parse(json);
				return document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == System.Text.Json.JsonValueKind.Array
					? result.EnumerateArray().Select(e => e.Clone()).ToList()
					: new();
			}
			const string lettersKey = "move.axes[].letter", homedKey = "move.axes[].homed";
			var letters = Array(Transact($"M409 K\"{lettersKey}\"", waitForMoves: false), lettersKey);
			var homed = Array(Transact($"M409 K\"{homedKey}\"", waitForMoves: false), homedKey);
			var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			for (var i = 0; i < Math.Min(letters.Count, homed.Count); i++)
			{
				if (letters[i].ValueKind == System.Text.Json.JsonValueKind.String && homed[i].ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
					states[letters[i].GetString()!] = homed[i].GetBoolean();
			}
			return states;
		}
		public void Pause(int miliseconds)
		{
			WriteWhenReady($"G4 P{miliseconds}");
		}
		/// <summary>
		/// Waits <paramref name="pause"/> milliseconds, then returns the latest reading of a sensor, as the board reports it, from the
		/// object model (M409 K"sensors.analog[n].lastReading"). A failure is returned as text starting with "Error".
		/// </summary>
		public string ReadSensor(int sensorDesignation, int pause)
		{
			if(pause > 0)
				Pause(pause);
			string response;
			try
			{
				var key = $"sensors.analog[{sensorDesignation}].lastReading";
				var reply = Transact($"M409 K\"{key}\"", waitForMoves: false);
				response = ParseObjectModelResult(reply, key);
			}
			catch (Exception ex)
			{
				Log.Error(ex, $"Error reading sensor {sensorDesignation}");
				ClosePortAfterError(ex);
				response = $"Error: {ex.Message}";
			}

			if(Settings.Debug)
			{
				if (sensorDesignation == Settings.DnaSensorNumber)
					return new Random().Next(Settings.DnaSensorReadLow, Settings.DnaSensorReadHigh).ToString();
				if (sensorDesignation == Settings.NadhSensorNumber)
					return new Random().Next(Settings.NadhSensorReadLow, Settings.NadhSensorReadHigh).ToString();
			}
			return response;
		}
		/// <summary>
		/// Uploads a local text file to the controller board over the serial connection (M28/M29). The file is written to a temporary
		/// name and only renamed over <paramref name="remotePath"/> once it has been received completely, so a failed upload never
		/// leaves a partial file in its place. Returns null on success, otherwise an error message.
		/// </summary>
		public string? SendFile(string localPath, string remotePath)
		{
			var tempPath = remotePath + ".tmp";
			var started = false;
			lock (_exchangeLock)
			{
				try
				{
					var lines = File.ReadAllLines(localPath).Where(l => !String.IsNullOrWhiteSpace(l)).ToList();
					OpenPortIfNeeded();
					var timeout = ReadyTimeout;

					// once M28 has started the upload, every line is written to the file instead of being run, so the end of M28's
					// reply cannot be marked with an echo: wait for its "Writing to file" line instead. The lines of the file get no
					// reply; the USB connection's own flow control keeps them from arriving faster than the board writes them.
					var opened = Exchange([$"M28 \"{tempPath}\""], DateTime.UtcNow + timeout, timeout,
						isEnd: l => l.StartsWith("Writing to file", StringComparison.OrdinalIgnoreCase) || IsBoardError(l));
					ThrowOnError(opened);
					started = true;
					var writeTimeout = _serialPort.WriteTimeout;
					_serialPort.WriteTimeout = (int)FileLineTimeout.TotalMilliseconds;
					try
					{
						foreach (var line in lines)
							_serialPort.WriteLine(line);
					}
					finally
					{
						_serialPort.WriteTimeout = writeTimeout;
					}
					started = false;
					ThrowOnError(Exchange(["M29"], DateTime.UtcNow + timeout, timeout));
					ThrowOnError(Exchange([$"M471 S\"{tempPath}\" T\"{remotePath}\" D1"], DateTime.UtcNow + timeout, timeout));
					return null;
				}
				catch (Exception ex)
				{
					Log.Error(ex, $"Error sending {localPath} to {remotePath}");
					if (started)
					{
						// end the upload so the board goes back to running commands rather than writing them to the file
						try { _serialPort.WriteLine("M29"); } catch { }
					}
					ClosePortAfterError(ex);
					return $"Error: {ex.Message}";
				}
			}
		}
		/// <summary>
		/// Restarts the controller board (M999) and waits for it to come back, so the commands after this one find it running the
		/// (new) config.g. Returns null once the board is back and has confirmed it restarted (its uptime started again), otherwise
		/// an error message.
		/// </summary>
		/// <remarks>
		/// The board restarts only once it has sent what it still has to say, so the port is kept open, with its replies being read,
		/// until the board drops off USB. Closing the port straight after M999 leaves the restart waiting indefinitely.
		/// </remarks>
		public string? Reboot()
		{
			lock (_exchangeLock)
			{
				string port;
				double? upTimeBefore;
				var sent = System.Diagnostics.Stopwatch.StartNew();
				try
				{
					OpenPortIfNeeded();
					port = _serialPort.PortName;
					upTimeBefore = ReadUpTime();
					ReadData.Enqueue("Restarting the controller board...");
					_serialPort.WriteLine("M999");
					sent.Restart();
				}
				catch (Exception ex)
				{
					Log.Error(ex, "Error restarting the controller board");
					ClosePortAfterError(ex);
					return $"Error: {ex.Message}";
				}

				// keep reading (DataReceived does) until the board's USB connection goes
				while (sent.Elapsed < RestartDropTimeout && IsPortPresent(port))
					Thread.Sleep(200);
				var dropped = !IsPortPresent(port);
				try { _serialPort.Close(); } catch (Exception ex) { Log.Debug(ex, "Error closing the port of a restarting board"); }

				// wait for the board to start again (config.g waits for expansion boards and WiFi, so it can take a while)
				var backTimeout = TimeoutFor(Settings.ApplyConfigTimeoutSeconds);
				Exception? lastError = null;
				while (sent.Elapsed < RestartDropTimeout + backTimeout)
				{
					Thread.Sleep(500);
					try
					{
						OpenPortIfNeeded();
						var upTime = ReadUpTime();
						// a board that did not restart has been up at least as long as before, plus the time since M999
						if (!dropped && upTime is not null && upTimeBefore is not null && upTime >= upTimeBefore + sent.Elapsed.TotalSeconds - 2)
							return $"Error: the controller board did not restart (M999) within {RestartDropTimeout.TotalSeconds:0} seconds.";
						ReadData.Enqueue("The controller board has restarted.");
						UpdatePositions(); // a restarted board has no axes homed
						UpdateOutputs(); // ... and every output off
						return null;
					}
					catch (Exception ex)
					{
						lastError = ex; // still starting up
						try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { }
					}
				}
				return $"Error: the controller board did not come back within {backTimeout.TotalSeconds:0} seconds of restarting" +
					(lastError is null ? "." : $" ({lastError.Message})");
			}
		}
		/// <summary>The board's uptime in seconds, or null if it could not be read. Call with <see cref="_exchangeLock"/> held.</summary>
		private double? ReadUpTime()
		{
			const string key = "state.upTime";
			var reply = Exchange([$"M409 K\"{key}\""], DateTime.UtcNow + ReadyTimeout, ReadyTimeout);
			var value = ParseObjectModelResult(reply, key);
			return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
				? seconds : null;
		}
		private static bool IsPortPresent(string name) =>
			SerialPorts().Any(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
		/// <summary>
		/// Chooses the serial port to talk to: the port named by <see cref="Settings.ComPortNumber"/>.
		/// If the choice changes, an open port is closed so the next command opens the new one.
		/// </summary>
		public void SelectPort(Settings settings)
		{
			Initialize();
			if (DetectPort)
				return; // the port is found by asking each one for the firmware version instead (see FindBoardPort)
			var name = $"COM{settings.ComPortNumber}";

			if (_serialPort.PortName == name)
				return;
			if (_serialPort.IsOpen)
				_serialPort.Close();
			_serialPort.PortName = name;
		}
		public void SetTrayTemperature(decimal temperature)
		{
			WriteWhenReady($"M140 S{temperature}");
		}
		/// <summary>Turns the controller's WiFi network interface on (M552 S1) or off (M552 S0).</summary>
		public void SetWifiEnabled(bool enabled)
		{
			WriteWhenReady($"M552 S{(enabled ? 1 : 0)}");
		}
		public void SetWifi(string ssid, string password)
		{
			WriteWhenReady($"M552 S0");
			// a double quote inside a G-code string is written as two double quotes
			WriteWhenReady($"M587 S\"{(ssid ?? "").Replace("\"", "\"\"")}\" P\"{(password ?? "").Replace("\"", "\"\"")}\"");
			WriteWhenReady($"M552 S1");
		}
		#endregion

		#region Private methods
		/// <summary>
		/// Sends one command (and, if <paramref name="waitForMoves"/>, an M400 after it, which the board finishes only once every
		/// queued move has) and returns the lines the board replied. The board must finish within the timeout in force for this
		/// thread (see <see cref="OverrideReadyTimeout"/>), which is what <see cref="ReadyTimeRemaining"/> counts down. Throws
		/// <see cref="BoardNotReadyException"/> if it does not, and the I/O exception if the board cannot be reached.
		/// </summary>
		private List<string> Transact(string command, bool waitForMoves)
		{
			lock (_exchangeLock)
			{
				OpenPortIfNeeded();
				var timeout = _timeoutOverride.Value ?? ReadyTimeout;
				var deadline = DateTime.UtcNow + timeout;
				Volatile.Write(ref _readyDeadlineTicks, deadline.Ticks);
				try
				{
					var reply = Exchange(waitForMoves ? [command, "M400"] : [command], deadline, timeout);
					// the board can send object model data of its own accord; keep it only when it was asked for
					if (!IsObjectModelQuery(command))
						reply.RemoveAll(IsObjectModelData);
					return reply;
				}
				finally
				{
					Volatile.Write(ref _readyDeadlineTicks, 0);
				}
			}
		}
		/// <summary>
		/// Writes <paramref name="lines"/>, then (unless <paramref name="isEnd"/> is given) <c>echo "marker"</c> with a marker unique
		/// to this exchange, and returns the lines the board replied before the marker. RepRapFirmware runs a connection's commands in
		/// order and sends no "ok", so the echoed marker is how the end of the reply is recognised, and it arrives only once the
		/// commands before it have finished. Call with <see cref="_exchangeLock"/> held, so only one exchange is ever under way.
		/// </summary>
		/// <param name="isEnd">Recognises the reply's last line instead of a marker (for M28, after which an echo would go into the file).</param>
		private List<string> Exchange(IReadOnlyList<string> lines, DateTime deadline, TimeSpan timeout, Func<string, bool>? isEnd = null)
		{
			var marker = isEnd is null ? $"{STR_markerPrefix}{++_markerCount}" : null;
			var pending = new PendingReply(marker, isEnd);
			bool behind;
			lock (_pendingLock)
			{
				_pending = pending;
				behind = _staleMarkers.Count > 0;
			}
			try
			{
				foreach (var line in lines)
					_serialPort.WriteLine(line);
				if (marker is not null)
					_serialPort.WriteLine($"echo \"{marker}\"");
			}
			catch
			{
				lock (_pendingLock)
					_pending = null;
				throw;
			}

			var wait = deadline - DateTime.UtcNow;
			if (pending.Done.Wait(wait > TimeSpan.Zero ? wait : TimeSpan.Zero))
				return pending.Lines;

			lock (_pendingLock)
			{
				if (pending.Done.IsSet) // the end of the reply arrived just as the wait ended
					return pending.Lines;
				_pending = null;
				// the board may still finish these commands later: what it sends before this marker then belongs to them
				if (marker is not null)
					_staleMarkers.Enqueue(marker);
			}
			throw new BoardNotReadyException(behind
				? $"The controller board did not finish \"{lines[0]}\" within {timeout.TotalSeconds:0} seconds; it was still busy with an earlier command that ran past its timeout."
				: $"The controller board did not finish \"{lines[0]}\" within {timeout.TotalSeconds:0} seconds.");
		}
		/// <summary>
		/// Handles one complete line from the board. In PanelDue mode (M575 S0/S1) the board wraps each reply in JSON,
		/// {"seq":n,"resp":"text"}: the text is unwrapped and handled line by line, the same as in raw mode.
		/// </summary>
		private void HandleLine(string line)
		{
			if (line.StartsWith("{\"seq\"", StringComparison.Ordinal))
			{
				try
				{
					using var document = System.Text.Json.JsonDocument.Parse(line);
					if (document.RootElement.TryGetProperty("resp", out var resp) && resp.ValueKind == System.Text.Json.JsonValueKind.String)
					{
						foreach (var text in (resp.GetString() ?? "").Split('\n'))
						{
							var trimmed = text.Trim();
							if (trimmed.Length > 0)
								HandleText(trimmed);
						}
						return;
					}
				}
				catch (System.Text.Json.JsonException)
				{
					// not a complete reply after all: handle it as it is
				}
			}
			HandleText(line);
		}
		/// <summary>Handles one line of reply text: part of the reply under way, its end, left over from an earlier one, or a message of its own.</summary>
		private void HandleText(string line)
		{
			if (line == "ok")
				return; // only sent in Marlin compatibility mode (M555 P2), and not needed
			lock (_pendingLock)
			{
				var pending = _pending;
				if (pending is not null && (pending.Marker is not null ? line == pending.Marker : pending.IsEnd!(line)))
				{
					if (pending.IsEnd is not null)
						pending.Lines.Add(line);
					// a marker is only reached once everything before it has been sent, so an earlier marker that never came is lost
					_staleMarkers.Clear();
					_pending = null;
					pending.Done.Set();
					return;
				}
				if (_staleMarkers.Count > 0)
				{
					// still the reply to a command that ran past its timeout: show it, but it is not part of the reply under way
					if (_staleMarkers.Contains(line))
					{
						while (_staleMarkers.Dequeue() != line) { }
						return;
					}
					PublishLine(line);
					return;
				}
				if (pending is not null)
				{
					pending.Lines.Add(line);
					return;
				}
			}
			if (line.StartsWith(STR_markerPrefix, StringComparison.Ordinal))
				return; // the marker of an exchange that has been given up on
			PublishLine(line); // something the board said of its own accord
		}
		/// <summary>Shows lines the board replied in the console messages; an error the board reported is logged too.</summary>
		private void Publish(IEnumerable<string> lines)
		{
			foreach (var line in lines)
				PublishLine(line);
		}
		private void PublishLine(string line)
		{
			if (IsObjectModelData(line))
				return; // machine-readable status, not a message for people
			if (IsBoardError(line))
				Log.Warning("The controller board reported: {Line}", line);
			ReadData.Enqueue(line);
		}
		private static bool IsBoardError(string line) => line.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
		/// <summary>True for object model data, {"key":...,"flags":...,"result":...}, which M409 returns and the board can also send of its own accord.</summary>
		private static bool IsObjectModelData(string line) => line.StartsWith("{\"key\"", StringComparison.Ordinal);
		private static bool IsObjectModelQuery(string command)
		{
			var code = command.TrimStart();
			return code.StartsWith("M409", StringComparison.OrdinalIgnoreCase) || code.StartsWith("M408", StringComparison.OrdinalIgnoreCase);
		}
		private static void ThrowOnError(List<string> reply)
		{
			var error = reply.FirstOrDefault(IsBoardError);
			if (error is not null)
				throw new IOException($"The controller board reported: {error}");
		}
		/// <summary>Reads the value of <paramref name="key"/> from an object model reply (M409): {"key":"...","flags":"","result":value}.</summary>
		private static string ParseObjectModelResult(List<string> reply, string key)
		{
			var error = reply.FirstOrDefault(IsBoardError);
			if (error is not null)
				return error;
			foreach (var line in reply.Where(IsObjectModelData))
			{
				using var document = System.Text.Json.JsonDocument.Parse(line);
				var root = document.RootElement;
				if (!root.TryGetProperty("key", out var replyKey) || replyKey.GetString() != key)
					continue; // object model data the board sent of its own accord
				if (!root.TryGetProperty("result", out var result) || result.ValueKind == System.Text.Json.JsonValueKind.Null)
					return "Error: the controller board has no such value (is the sensor configured?)";
				return result.ValueKind == System.Text.Json.JsonValueKind.String ? result.GetString() ?? "" : result.GetRawText();
			}
			return "Error: the controller board did not return the value asked for" + (reply.Count > 0 ? $" (it replied: {String.Join(" ", reply)})" : ".");
		}
		private void InitializeReadData()
		{
			_serialPort.DataReceived += SerialPort_DataReceived;
			PublishReadData();
		}
		private void PublishReadData()
		{
			while(!_disposing)
			{
				while(ReadData.TryDequeue(out var message))
				{
					DataReceived?.Invoke(this, new DataArgs<string>(message));
				}
				Task.Delay(500).Wait();
			}
		}
		private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
		{
			string data;
			try
			{
				data = _serialPort.ReadExisting();
			}
			catch (Exception ex)
			{
				// the port was closed, or the board's USB connection dropped (it reset): an exception escaping this
				// event handler would end the program, and the next command reopens the port anyway
				Log.Warning(ex, "Error reading from the serial port");
				return;
			}
			if(!String.IsNullOrEmpty(data))
				Receive(data);
		}
		/// <summary>Takes text read from the board, splits it into complete lines, and handles each one.</summary>
		private void Receive(string data)
		{
			// data arrives in pieces that need not end at a line break: keep any unfinished line until the rest of it arrives
			var lines = new List<string>();
			lock (_receiveBuffer)
			{
				_receiveBuffer.Append(data);
				var text = _receiveBuffer.ToString();
				var end = text.LastIndexOf('\n');
				if (end < 0)
					return;
				_receiveBuffer.Remove(0, end + 1);
				foreach (var raw in text.Substring(0, end).Split('\n'))
				{
					var line = raw.Trim();
					if (line.Length > 0)
						lines.Add(line);
				}
			}
			foreach (var line in lines)
				HandleLine(line);
		}
		private void OpenPortIfNeeded()
		{
			// unless the port is detected (DetectPort), follow the COM Port Number setting as it is now (the active settings, not the
			// ones the configuration was built from), so a change takes effect with the next command
			SelectPort(_settingsProvider());
			if (_serialPort.IsOpen)
				return;
			if (!DetectPort)
			{
				OpenFresh(_serialPort.Open);
				return;
			}

			var name = FindBoardPort();
			if (!String.Equals(_serialPort.PortName, name, StringComparison.OrdinalIgnoreCase))
				_serialPort.PortName = name;
			OpenFresh(OpenAfterProbe);
			if (!String.Equals(_announcedPort, name, StringComparison.OrdinalIgnoreCase))
			{
				_announcedPort = name;
				Log.Information("Connected to the controller board on {Port}", name);
				ReadData.Enqueue($"Connected to the controller board on {name}.");
			}
		}
		/// <summary>
		/// Finds the COM port the controller board is on by sending M115 (report firmware version) to each port and choosing the
		/// first that replies with a firmware name. The port found last time is tried first, so reconnecting after the board resets
		/// (when Windows may give it a different number) is quick. Throws if no port answers.
		/// </summary>
		private string FindBoardPort()
		{
			var ports = SerialPorts();
			var names = ports
				.OrderBy(p => String.Equals(p.Name, _detectedPort, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
				.ThenBy(p => p.DeviceId.StartsWith(STR_duetUsbId, StringComparison.OrdinalIgnoreCase) ? 0 : 1) // a Duet (by USB vendor)
				.ThenBy(p => p.DeviceId, StringComparer.OrdinalIgnoreCase) // ... and its first interface (MI_00) before the others
				.ThenBy(p => PortNumber(p.Name))
				.Select(p => p.Name)
				.ToList();
			if (names.Count == 0)
				throw new IOException("No serial COM ports were found. Is the controller board's USB cable connected and the board powered?");

			var tried = new List<string>();
			foreach (var name in names)
			{
				var problem = ProbePort(name);
				if (problem is null)
				{
					_detectedPort = name;
					return name;
				}
				tried.Add($"{name}: {problem}");
			}
			throw new IOException($"No controller board answered M115 on any COM port ({String.Join("; ", tried)}).");
		}
		/// <summary>
		/// The COM ports of serial devices that are present: devices in Windows' Ports class (USB serial, built-in serial ports) that
		/// have a COM port. Bluetooth serial links, parallel (LPT) ports, and COM numbers left behind by devices that are no longer
		/// plugged in are left out. If Windows cannot be asked, every COM port name is returned.
		/// </summary>
		private static List<(string Name, string DeviceId)> SerialPorts()
		{
			static List<(string Name, string DeviceId)> AllPortNames() =>
				SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).Select(n => (n, "")).ToList();
			if (!OperatingSystem.IsWindows())
				return AllPortNames();
			try
			{
				var ports = new List<(string Name, string DeviceId)>();
				using var searcher = new System.Management.ManagementObjectSearcher(
					$"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE ClassGuid = '{STR_portsClassGuid}'");
				foreach (var device in searcher.Get())
				{
					using (device)
					{
						var deviceName = device["Name"] as string ?? "";
						var deviceId = device["PNPDeviceID"] as string ?? "";
						var match = System.Text.RegularExpressions.Regex.Match(deviceName, @"\((COM\d+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
						if (!match.Success)
							continue; // a parallel (LPT) port
						if (deviceId.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase))
						{
							Log.Debug("Not probing {Port} ({Device}): it is a Bluetooth link", match.Groups[1].Value, deviceName);
							continue;
						}
						ports.Add((match.Groups[1].Value.ToUpperInvariant(), deviceId));
					}
				}
				return ports.DistinctBy(p => p.Name).ToList();
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "Could not ask Windows which COM ports are serial devices; probing every COM port");
				return AllPortNames();
			}
		}
		private static int PortNumber(string name) =>
			int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out var number) ? number : int.MaxValue;
		/// <summary>
		/// Opens <paramref name="name"/> on its own, sends M115 and waits briefly for a reply naming the firmware. Returns null if the
		/// board answered, otherwise why the port was rejected. Gives up on a port that takes too long to open (Bluetooth ports can).
		/// </summary>
		private static string? ProbePort(string name)
		{
			var abandoned = 0;
			var probe = Task.Run<string?>(() =>
			{
				var port = new SerialPort(name, BaudRate, Parity.None, 8, StopBits.One)
				{
					Handshake = Handshake.None,
					DtrEnable = true,
					RtsEnable = true,
					WriteTimeout = 500,
				};
				try
				{
					port.Open();
					if (Volatile.Read(ref abandoned) != 0)
						return "timed out";
					port.DiscardInBuffer();
					port.WriteLine("M115");
					var reply = new StringBuilder();
					var deadline = DateTime.UtcNow + ProbeReplyTimeout;
					while (DateTime.UtcNow < deadline && Volatile.Read(ref abandoned) == 0)
					{
						reply.Append(port.ReadExisting());
						if (reply.ToString().Contains("FIRMWARE_NAME", StringComparison.OrdinalIgnoreCase))
							return null;
						Thread.Sleep(50);
					}
					return reply.Length == 0 ? "no reply" : "replied, but not as a controller board";
				}
				catch (UnauthorizedAccessException)
				{
					return "in use by another program";
				}
				catch (Exception ex)
				{
					return ex.Message;
				}
				finally
				{
					try { port.Dispose(); } catch { }
				}
			});
			if (probe.Wait(ProbeTimeout))
				return probe.Result;
			Interlocked.Exchange(ref abandoned, 1);
			return "timed out";
		}
		/// <summary>
		/// Opens the port with nothing left over from an earlier connection: no half-received line, and no reply awaited or overdue.
		/// </summary>
		private void OpenFresh(Action open)
		{
			lock (_receiveBuffer)
				_receiveBuffer.Clear();
			lock (_pendingLock)
			{
				_pending = null;
				_staleMarkers.Clear();
			}
			open();
		}
		/// <summary>Opens the port the probe has just closed; Windows can take a moment to let it be opened again.</summary>
		private void OpenAfterProbe()
		{
			for (var attempt = 1; ; attempt++)
			{
				try
				{
					_serialPort.Open();
					return;
				}
				catch (UnauthorizedAccessException) when (attempt < 5)
				{
					Thread.Sleep(200);
				}
			}
		}
		/// <summary>
		/// After a failure talking to the board, closes the serial port so the next command opens it afresh. When the board resets
		/// (a crash, M999, an emergency stop) its USB serial device disappears and comes back, and the old handle never works again,
		/// so without this every later command would fail until the program was restarted. A board that is merely slow to report
		/// ready (<see cref="BoardNotReadyException"/>) is still connected, so the port is left open.
		/// </summary>
		private void ClosePortAfterError(Exception ex)
		{
			if (ex is BoardNotReadyException || _serialPort is null)
				return;
			try
			{
				if (_serialPort.IsOpen)
					_serialPort.Close();
			}
			catch (Exception closeEx)
			{
				Log.Warning(closeEx, "Error closing the serial port after a failure");
			}
		}
		/// <summary>
		/// Sends a command and waits for the board to finish it, including any moves it started; anything the board replied is shown
		/// in the console messages. Returns null on success, otherwise the error (the board's own error line, or why it could not be
		/// reached or did not finish in time).
		/// </summary>
		private string? WriteWhenReady(string data)
		{
			try
			{
				var reply = Transact(data, waitForMoves: true);
				Publish(reply);
				return reply.FirstOrDefault(IsBoardError);
			}
			catch (Exception ex)
			{
				Log.Error(ex, $"Error sending to the controller board: {data}");
				ClosePortAfterError(ex);
				return $"Error: {ex.Message}";
			}
		}
		#endregion

		#region Properties
		/// <summary>The device configuration, created from the current settings the first time it is needed (and again after <see cref="ResetConfiguration"/>).</summary>
		public DeviceConfiguration Configuration
		{
			get
			{
				lock (_configurationLock)
					return _configuration ??= new DeviceConfiguration(_settingsProvider());
			}
		}

		/// <summary>The settings the configuration was built from; the current settings if no configuration has been built yet.</summary>
		public Settings Settings
		{
			get
			{
				lock (_configurationLock)
					return _configuration?.Settings ?? _settingsProvider();
			}
		}
		/// <summary>How long to wait for the controller board to report that it has finished a command before giving up on it (from the settings).</summary>
		public TimeSpan ReadyTimeout => TimeSpan.FromSeconds(Math.Max(1, Settings.BoardReadyTimeoutSeconds));
		/// <summary>How long is left before the wait for the board to report ready is given up on, or null when nothing is being waited for.</summary>
		public TimeSpan? ReadyTimeRemaining
		{
			get
			{
				var ticks = Volatile.Read(ref _readyDeadlineTicks);
				if (ticks == 0)
					return null;
				var remaining = new DateTime(ticks, DateTimeKind.Utc) - DateTime.UtcNow;
				return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
			}
		}
		/// <summary>The timeout for a kind of command that can take a long time: the given number of seconds, but never less than <see cref="ReadyTimeout"/>.</summary>
		public TimeSpan TimeoutFor(int seconds)
		{
			var specific = TimeSpan.FromSeconds(Math.Max(1, seconds));
			var general = ReadyTimeout;
			return specific > general ? specific : general;
		}
		/// <summary>
		/// Makes the commands this thread sends wait up to <paramref name="timeout"/> for the board (instead of <see cref="ReadyTimeout"/>)
		/// until the returned scope is disposed. A null timeout leaves the setting in force.
		/// </summary>
		public IDisposable OverrideReadyTimeout(TimeSpan? timeout)
		{
			var previous = _timeoutOverride.Value;
			_timeoutOverride.Value = timeout ?? previous;
			return new TimeoutScope(() => _timeoutOverride.Value = previous);
		}
		/// <summary>The board is connected but did not report that it had finished a command in time.</summary>
		public sealed class BoardNotReadyException(string message) : TimeoutException(message);
		private sealed class TimeoutScope(Action restore) : IDisposable
		{
			public void Dispose() => restore();
		}
		public ConcurrentQueue<string> ReadData { get; set; } = new();
		/// <summary>Where each axis was after the last move or home, by axis letter (empty until the first one).</summary>
		public IReadOnlyDictionary<string, decimal> Positions { get; private set; } = new Dictionary<string, decimal>();
		/// <summary>Which axes were homed after the last move or home, by axis letter (empty until the first one): an axis that is not
		/// homed cannot be moved, except to home it.</summary>
		public IReadOnlyDictionary<string, bool> Homed { get; private set; } = new Dictionary<string, bool>();
		/// <summary>Which output ports (M950 P) were on when last read, by port number (empty until the first read).</summary>
		public IReadOnlyDictionary<int, bool> OutputStates { get; private set; } = new Dictionary<int, bool>();
		/// <summary>
		/// True (for now) to find the board's COM port by sending M115 to each port; the COM Port Number setting is then ignored.
		/// False uses the setting.
		/// </summary>
		public bool DetectPort { get; set; } = true;
		#endregion

		#region Events
		public event EventHandler<DataArgs<string>>? DataReceived;
		/// <summary>Raised, on the thread that sent the move, when <see cref="Positions"/> has been read again.</summary>
		public event EventHandler? PositionsChanged;
		/// <summary>Raised, on the thread that switched an output, when <see cref="OutputStates"/> has been read again.</summary>
		public event EventHandler? OutputsChanged;
		#endregion

		#region Fields
		readonly Func<Settings> _settingsProvider;
		readonly object _configurationLock = new();
		DeviceConfiguration? _configuration;
		readonly object _initializeLock = new();
		bool _initialized;
		Thread? _publishThread;
		SerialPort _serialPort = null!; // created by Initialize()
		bool _disposing;
		long _readyDeadlineTicks;
		readonly ThreadLocal<TimeSpan?> _timeoutOverride = new();

		/// <summary>Held while commands are being sent and their reply awaited, so only one exchange is ever under way.</summary>
		readonly object _exchangeLock = new();
		/// <summary>Guards <see cref="_pending"/>, which the serial port's receive thread completes.</summary>
		readonly object _pendingLock = new();
		PendingReply? _pending;
		/// <summary>Text received after the last complete line.</summary>
		readonly StringBuilder _receiveBuffer = new();
		/// <summary>Markers of exchanges given up on (oldest first): what the board sends before each still belongs to that exchange.</summary>
		readonly Queue<string> _staleMarkers = new();
		int _markerCount;

		/// <summary>Commands that have been sent and whose reply is being collected.</summary>
		private sealed class PendingReply(string? marker, Func<string, bool>? isEnd)
		{
			public List<string> Lines { get; } = new();
			public ManualResetEventSlim Done { get; } = new(false);
			/// <summary>The echoed line that marks the end of the reply, or null when <see cref="IsEnd"/> recognises its last line.</summary>
			public string? Marker { get; } = marker;
			public Func<string, bool>? IsEnd { get; } = isEnd;
		}

		string? _detectedPort;
		string? _announcedPort;

		private const int BaudRate = 115200;
		/// <summary>Windows' device class for COM and LPT ports.</summary>
		private const string STR_portsClassGuid = "{4d36e978-e325-11ce-bfc1-08002be10318}";
		/// <summary>The start of a Duet board's USB device ID (USB vendor 1D50).</summary>
		private const string STR_duetUsbId = @"USB\VID_1D50&";
		private static readonly TimeSpan ProbeReplyTimeout = TimeSpan.FromSeconds(1.5);
		private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);
		/// <summary>How long after M999 the board has to drop off USB (it restarts once it has sent its pending replies).</summary>
		private static readonly TimeSpan RestartDropTimeout = TimeSpan.FromSeconds(10);
		/// <summary>The start of the markers echoed to recognise the end of each reply.</summary>
		private const string STR_markerPrefix = "effani-";
		/// <summary>How long writing each line of a file being uploaded may take (the board's USB flow control can hold it up).</summary>
		private static readonly TimeSpan FileLineTimeout = TimeSpan.FromSeconds(5);
		#endregion
	}
}
