using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Drivetrain.Robotics.Control
{
	public class DeviceConfiguration : ConfigurationSection
	{
		public DeviceConfiguration(Settings settings)
		{
			Settings = settings;
			DeviceSpecification = CreateDeviceSpec();
			AddPlates(DeviceSpecification);
		}
		/// <summary>
		/// Points the configuration at <paramref name="settings"/> (the active settings object is replaced when a settings version is
		/// activated or reset) and rebuilds the device specification, which is otherwise only a snapshot taken at construction.
		/// </summary>
		public void Refresh(Settings settings)
		{
			Settings = settings;
			var spec = CreateDeviceSpec();
			AddPlates(spec);
			DeviceSpecification = spec;
		}
		/// <summary>
		/// Checks that every well of every tray can be brought to an item at <paramref name="location"/> (a fluid port, the
		/// aspiration needle or a sensor). The trays are taken to fill the carrier as the settings lay them out: the first tray at
		/// the lower left, the others one tray offset on from it across and up, for the full number of trays across and up. A well
		/// is brought to the item by moving the table to the item's location less the well's, which must lie on the table's travel.
		/// Returns null when every well can be reached, otherwise a sentence saying which values would work and why this one does not.
		/// </summary>
		public static string? GetWellReachCaution(Settings settings, Coordinate location)
		{
			var across = Math.Max(1, settings.HorizontalTrayCount);
			var up = Math.Max(1, settings.VerticalTrayCount);
			// the wells nearest to and farthest from the table's origin, along each axis
			static (decimal Nearest, decimal Farthest) Extent(decimal first, decimal trayOffset, int trays, decimal wellOffset, int wells)
			{
				var ends = new[] { first, first + trayOffset * (trays - 1), first + wellOffset * (wells - 1), first + trayOffset * (trays - 1) + wellOffset * (wells - 1) };
				return (ends.Min(), ends.Max());
			}
			var x = Extent(settings.FirstWellLocation.X, settings.PlateOffset.X, across, settings.WellOffset.X, Math.Max(1, settings.HorizontalWellCount));
			var y = Extent(settings.FirstWellLocation.Y, settings.PlateOffset.Y, up, settings.WellOffset.Y, Math.Max(1, settings.VerticalWellCount));

			static string Number(decimal value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
			static string? Check(string axis, decimal value, (decimal Nearest, decimal Farthest) wells, int travel)
			{
				// the table goes to value - well: at least 0 for the farthest well, at most the travel for the nearest
				var least = wells.Farthest;
				var most = wells.Nearest + travel;
				if (least > most)
					return $"No {axis} can work: the wells span {Number(wells.Farthest - wells.Nearest)} mm along {axis}, more than the table's travel of {travel} mm.";
				if (value < least)
					return $"{axis} must be {Number(least)} to {Number(most)} (it is {Number(value)}): the wells farthest along {axis} would need the table at {axis}{Number(value - wells.Farthest)}, below 0.";
				if (value > most)
					return $"{axis} must be {Number(least)} to {Number(most)} (it is {Number(value)}): the wells nearest along {axis} would need the table at {axis}{Number(value - wells.Nearest)}, past its travel of {travel} mm.";
				return null;
			}
			var problems = new[] { Check("X", location.X, x, settings.TableXTravelLength), Check("Y", location.Y, y, settings.TableYTravelLength) }.Where(p => p is not null).ToList();
			if (problems.Count == 0)
				return null;
			var trays = across * up;
			return $"Caution: cannot reach every well of {(trays == 1 ? "the tray" : $"the {trays} trays ({across} across, {up} up)")}. {String.Join(" ", problems)}";
		}
		/// <summary>
		/// Works the trays and their wells out again from the settings as they are now. The wells are otherwise only worked out when
		/// the configuration is built, while the locations of the axes and sensors are the settings' own and so always current:
		/// after First Well Location, an offset or a count was edited, the two were out of step until the configuration was rebuilt.
		/// </summary>
		public void RefreshPlates()
		{
			var fresh = new DeviceSpecification();
			AddPlates(fresh);
			DeviceSpecification.Plates = fresh.Plates;
		}
		// when set, generated files are collected here instead of being written to disk (see GetConfigSignature)
		private SortedDictionary<string, string>? _capture;
		private DeviceConfiguration(Settings settings, bool withoutPlates)
		{
			Settings = settings;
		}
		/// <summary>
		/// A fingerprint of every file <see cref="GenerateConfigFiles"/> would produce from <paramref name="settings"/>: it changes exactly when
		/// a setting that ends up in config.g or the homing files changes. Nothing is written to disk.
		/// </summary>
		public static string GetConfigSignature(Settings settings)
		{
			var configuration = new DeviceConfiguration(settings, withoutPlates: true) { _capture = new SortedDictionary<string, string>() };
			var spec = configuration.CreateDeviceSpec();
			configuration.WriteConfig("", spec);
			configuration.WriteHome("", spec);
			var text = string.Concat(configuration._capture.Select(f => $"{f.Key}\n{f.Value}\n"));
			return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
		}
		/// <summary>Full paths of the files written by the last <see cref="GenerateConfigFiles"/> call (config.g is last).</summary>
		public IReadOnlyList<string> GeneratedFiles => _generatedFiles.OrderBy(f => Path.GetFileName(f) == "config.g").ToList();
		private readonly List<string> _generatedFiles = new();
		public string GenerateConfigFiles(string drive)
		{
			try
			{
				var mainFolder = Path.Combine(drive, @"");
				var sysFolder = Path.Combine(mainFolder, @"sys");
				if(!Directory.Exists(sysFolder))
					Directory.CreateDirectory(sysFolder);
				// Build from the current settings: DeviceSpecification is only a snapshot taken when this object was created.
				var spec = CreateDeviceSpec();
				_generatedFiles.Clear();
				WriteConfig(sysFolder, spec);
				WriteHome(sysFolder, spec);
				var zipFilePath = Path.Combine(mainFolder, "config.zip");
				File.Delete(zipFilePath);
				ZipFile.CreateFromDirectory(sysFolder, zipFilePath);
				return zipFilePath;
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
		}
		/// <summary>
		/// Checks the configured board connections against each other and the installed hardware.
		/// Returns one message per problem found; an empty list means nothing suspicious was found.
		/// </summary>
		public List<string> GetConnectionWarnings()
		{
			var spec = CreateDeviceSpec();
			var warnings = new List<string>();
			var boardCount = Math.Max(1, Settings.ControllerBoardCount);
			var lastBoard = boardCount - 1;
			var boardNames = string.Join(" and ", Enumerable.Range(0, boardCount));

			void CheckBoard(string what, int board)
			{
				if (board < 0 || board > lastBoard)
					warnings.Add($"{what} is on board {board}, but only board{(boardCount == 1 ? "" : "s")} {boardNames} exist{(boardCount == 1 ? "s" : "")} ({boardCount} controller board{(boardCount == 1 ? "" : "s")} configured).");
			}
			void CheckDuplicates(string kind, IEnumerable<(string Label, string Name)> uses)
			{
				foreach (var group in uses.GroupBy(u => u.Name).Where(g => g.Count() > 1))
					warnings.Add($"{kind} {group.Key} is assigned to more than one item: {string.Join(", ", group.Select(g => g.Label))}.");
			}

			var axes = spec.Axes;
			foreach (var axis in axes)
			{
				CheckBoard($"{axis.Label} motor driver", axis.DriverBoardPort.Board);
				CheckBoard($"{axis.Label} endstop", axis.EndstopBoardPort.Board);
				if (axis.DriverBoardPort.Port < 0 || axis.DriverBoardPort.Port >= DriversPerBoard)
					warnings.Add($"{axis.Label} motor driver port {axis.DriverBoardPort.Port} does not exist; each board has drivers 0 to {DriversPerBoard - 1}.");
				if (axis.MoveSpeed > axis.MaxSpeed)
					warnings.Add($"{axis.Label} Move Speed ({axis.MoveSpeed} mm/min) is above its Max Speed ({axis.MaxSpeed} mm/min), so its moves will run at {axis.MaxSpeed} mm/min.");
			}
			CheckDuplicates("Motor driver", axes.Select(a => (a.Label, a.DriverBoardPort.Designation)));

			// the needle positions used when aspirating must lie within the axis travel
			var aspirate = axes.FirstOrDefault(a => a.Axis == Axis.Aspirate);
			if (aspirate != null)
			{
				var largestVolume = new[] { Settings.KohAspirationVolume, Settings.DnaAspirationVolume, Settings.TestMediaAspirationVolume }.Max();
				var platePosition = Settings.AspirateActuatorStartPosition + Settings.AspirateDistanceToPlate; // down is the positive direction: 0 is the top of the travel
				foreach (var (what, position) in new[]
				{
					("start position", Settings.AspirateActuatorStartPosition),
					("plate position (start + distance to tray)", platePosition),
					($"deepest position when aspirating {largestVolume} µl", platePosition + Math.Abs(Settings.AspirateTravelPerMicroLiter * largestVolume)),
				})
				{
					if (position < aspirate.Minimum || position > aspirate.Maximum)
						warnings.Add($"Aspirate needle {what} is {position:0.##} mm, which is outside the aspirate axis travel ({aspirate.Minimum} to {aspirate.Maximum} mm).");
				}
			}

			var sensors = spec.Sensors;
			foreach (var sensor in sensors)
				CheckBoard(sensor.Label, sensor.Board);
			var inputCount = Settings.InputPortsPerBoard;
			void CheckInputPort(string what, BoardPort port)
			{
				if (port.Port < 0 || port.Port >= inputCount)
					warnings.Add($"{what} is on {port.Designation}, but each board only has input ports 0 to {inputCount - 1} ({inputCount} input ports per board configured).");
			}
			foreach (var axis in axes)
				CheckInputPort($"{axis.Label} endstop", axis.EndstopBoardPort);

			CheckDuplicates("Input port", axes.Select(a => ($"{a.Label} endstop", a.EndstopBoardPort.Designation)));
			CheckDuplicates("Temperature input", sensors.Select(s => (s.Label, s.PortDesignation!)));

			var outputs = GetActuatorOutputs();
			foreach (var output in outputs)
				CheckBoard(output.Label, output.Port.Board);
			// compared by physical pin: a laser output is the same pin as its board's out6
			CheckDuplicates("Output pin", outputs.Select(o => (o.Label, o.Port.Pin)));

			return warnings;
		}
		/// <summary>Escapes text for use inside a G-code string literal (a double quote is written as two double quotes).</summary>
		private static string EscapeGCodeString(string? text) => (text ?? "").Replace("\"", "\"\"");
		/// <summary>Installed drivers per board (Duet 3 Mini 5+: drivers 0-4).</summary>
		private const int DriversPerBoard = 5;
		private List<(int Index, string Label, BoardPort Port)> GetActuatorOutputs() => new()
		{
			(0, "tray UV", BoardPort.Output(Settings.TrayUvBoardNumber, Settings.TrayUvPortNumber)),
			(1, "chamber UV", BoardPort.Output(Settings.ChamberUvBoardNumber, Settings.ChamberUvPortNumber)),
			(2, "NADH laser", BoardPort.OutputOrLaser(Settings.NadhLaserPortKind, Settings.NadhLaserBoardNumber, Settings.NadhLaserPortNumber)),
			(3, "DNA laser", BoardPort.OutputOrLaser(Settings.DnaLaserPortKind, Settings.DnaLaserBoardNumber, Settings.DnaLaserPortNumber)),
		};
		private void WriteConfig(string folder, DeviceSpecification spec)
		{
			var configTextBuilder = new StringBuilder();
			configTextBuilder.Append(@"
; General
G90 ; absolute coordinates
M83 ; relative extruder moves
M550 P""Effani"" ; set hostname
M575 P0 S2 ; USB: raw mode
M575 P1 S2

; Network
");
			// WiFi is only switched on when there is a network for the board to join: with no WiFi SSID in the settings it is switched
			// off, so the board neither waits for a network at startup nor keeps reporting failed network scans
			configTextBuilder.AppendLine(String.IsNullOrWhiteSpace(Settings.WifiSsid)
				? "M552 S0 ; WiFi off: no WiFi SSID is set"
				: "M552 S1 ; configure WiFi adapter");
			configTextBuilder.Append(@"M586 P0 S1 ; configure HTTP
M586 P1 S1 ; configure FTP
M586 P2 S1 ; configure Telnet

; Wait a moment for the CAN expansion boards to become available
G4 S4

; Smart Drivers
"
				);
			foreach (var x in spec.Axes)
			{
				configTextBuilder.AppendLine($"M569 P{x.DriverPort} S{(x.Driver.Direction == Direction.Forwards ? 1 : 0)} D{(x.Driver.Mode == DriverMode.StealthChop ? 3 : 2)} ; {x.Label} {x.Designation} ({(x.Driver.Mode == DriverMode.StealthChop ? "stealthChop" : "spreadCycle")})");
			}
			configTextBuilder.Append(@"

; Motor Idle Current Reduction
M906 I30 ; set motor current idle factor
M84 S30 ; set motor current idle timeout

; Set movement queue
M595 P0

; Axes
"
				);
			configTextBuilder.AppendLine($"\r\n; set axis mapping");
			configTextBuilder.Append($"M584");
			foreach (var x in spec.Axes)
			{
				configTextBuilder.Append($" {x.Designation}{x.DriverPort}");
            }
            configTextBuilder.AppendLine($"\r\n; configure microstepping with interpolation");
            configTextBuilder.Append($"M350");
			foreach (var x in spec.Axes)
			{
				configTextBuilder.Append($" {x.Designation}16");
			}
			configTextBuilder.Append($" I1");
			configTextBuilder.AppendLine($"\r\n; set axis driver currents");
			configTextBuilder.Append($"M906");
			foreach (var x in spec.Axes)
			{
				configTextBuilder.Append($" {x.Designation}{x.Driver.Current}");
			}
			configTextBuilder.AppendLine($"\r\n; configure steps per mm");
			configTextBuilder.Append($"M92");
			foreach (var x in spec.Axes)
			{
				configTextBuilder.Append($" {x.Designation}{x.StepsPerMillimeter}");
			}
			configTextBuilder.AppendLine($"\r\n; set minimum and maximum axis limits");
			configTextBuilder.Append($"M208");
			foreach (var x in spec.Axes)
			{
				configTextBuilder.Append($" {x.Designation}{x.Minimum}:{x.Maximum}");
			}
			configTextBuilder.AppendLine($"\r\n; set maximum instantaneous speed changes (mm/min)");
			configTextBuilder.Append($"M566");
			foreach (var x in spec.Axes)
			{
				configTextBuilder.Append($" {x.Designation}{x.MaxInstantaneousSpeedChange}");
			}
			configTextBuilder.AppendLine($"\r\n; set maximum speeds (mm/min)");
			configTextBuilder.Append($"M203");
			foreach (var x in spec.Axes)
			{
				configTextBuilder.Append($" {x.Designation}{x.MaxSpeed}");
			}
			configTextBuilder.AppendLine($"\r\n; set accelerations (mm/s^2)");
			configTextBuilder.Append($"M201");
			foreach (var x in spec.Axes)
			{
				configTextBuilder.Append($" {x.Designation}{x.Acceleration}");
			}
			configTextBuilder.Append(@"

; Kinematics
M669 K0 ; configure Cartesian kinematics

; Endstops
");
            foreach (var x in spec.Axes)
            {
                configTextBuilder.AppendLine($"M574 {x.Designation}{(x.Driver.Endstop.Location == EndstopLocation.LowEnd ? "1" : "2")} P\"{(x.Driver.Endstop.Signal == PinSignal.High ? null : "!")}{x.Driver.Endstop.SensorPort}\" S1");
            }
            var t = configTextBuilder.ToString();
            configTextBuilder.AppendLine("\r\n; Actuators");
			foreach (var (index, label, port) in GetActuatorOutputs())
				configTextBuilder.AppendLine($@"M950 P{index} C""{port.Designation}"" ; {label}");
			configTextBuilder.AppendLine("\r\n; Sensors");
			foreach (var sensor in spec.Sensors)
			{
				configTextBuilder.AppendLine($@"M308 S{sensor.SensorNumber} P""{sensor.PortDesignation}"" Y""linear-analog"" A""{sensor.Label}"" F0 B{sensor.ReadLow} C{sensor.ReadHigh}");
			}

			WriteGeneratedFile(Path.Combine(folder, "config.g"), configTextBuilder.ToString());
		}
		private void WriteGeneratedFile(string path, string text)
		{
			if (_capture != null)
			{
				_capture[Path.GetFileName(path)] = text;
				return;
			}
			File.WriteAllBytes(path, Encoding.ASCII.GetBytes(text));
			_generatedFiles.Add(path);
		}
		private void WriteHome(string folder, DeviceSpecification spec)
		{
			for (int i = 0; i < spec.Axes.Count; i++)
			{
				var homeTextBuilder = new StringBuilder();
				homeTextBuilder.Append(@"
; Home
"
				);
				var x = spec.Axes[i];
				var direction = x.Driver.Endstop.Location == EndstopLocation.HighEnd ? null : "-";
				// backing off the endstop moves the other way: down from a high-end endstop, up from a low-end one
				var backOff = x.Driver.Endstop.Location == EndstopLocation.HighEnd ? "-" : null;
				var distance = x.Maximum - x.Minimum + 5;
				homeTextBuilder.Append(@$"
G91 ; relative positioning
G1 H1 {x.Designation}{direction}{distance} F{Math.Min(x.Driver.Endstop.FirstHomingSpeed, x.MaxSpeed)} ; coarse home
G1 {x.Designation}{backOff}5 F{Math.Min(x.Driver.Endstop.FirstHomingSpeed, x.MaxSpeed)} ; move back 5mm off the endstop
G1 H1 {x.Designation}{direction}{distance} F{Math.Min(x.Driver.Endstop.SecondHomingSpeed, x.MaxSpeed)} ; fine home
G90 ; absolute positioning
				");
				WriteGeneratedFile(Path.Combine(folder, $"home{x.Designation}.g"), homeTextBuilder.ToString());
			}
			var homeAllBuilder = new StringBuilder();
			foreach (var x in spec.Axes)
			{
				homeAllBuilder.AppendLine($"M98 P\"home{x.Designation}.g\" ; home {x.Label} axis");
			}
			WriteGeneratedFile(Path.Combine(folder, $"homeall.g"), homeAllBuilder.ToString());
		}

		/// <summary>Builds the axes and sensors from the current <see cref="Settings"/> values (plates are added separately by <see cref="AddPlates"/>).</summary>
		private DeviceSpecification CreateDeviceSpec()
		{
			var spec = new DeviceSpecification();
			spec.DeviceName = spec.Label;
			spec.Axes.Add(WithMoveSpeed(Settings.TableXMoveSpeed, WithDriverMode(Settings.TableXDriverMode, WithMotorSettings(Settings.TableXStepsPerMillimeter, Settings.TableXTravelLength, Settings.TableXMotorCurrent, Settings.TableXMaxSpeed, Settings.TableXAcceleration, Settings.TableXMaxInstantaneousSpeedChange, WithHoming(Settings.TableXHomingSpeed, Settings.TableXFineHomingSpeed, new TableAxis(Axis.TableX,
				BoardPort.Driver(Settings.TableXDriverBoardNumber, Settings.TableXDriverPortNumber),
				BoardPort.Input(Settings.TableXEndstopBoardNumber, Settings.TableXEndstopPortNumber),
				500, Settings.TableXEndstopLocation, Settings.TableXDirection, Settings.TableXMaxSpeed))))));
			spec.Axes.Add(WithMoveSpeed(Settings.TableYMoveSpeed, WithDriverMode(Settings.TableYDriverMode, WithMotorSettings(Settings.TableYStepsPerMillimeter, Settings.TableYTravelLength, Settings.TableYMotorCurrent, Settings.TableYMaxSpeed, Settings.TableYAcceleration, Settings.TableYMaxInstantaneousSpeedChange, WithHoming(Settings.TableYHomingSpeed, Settings.TableYFineHomingSpeed, new TableAxis(Axis.TableY,
				BoardPort.Driver(Settings.TableYDriverBoardNumber, Settings.TableYDriverPortNumber),
				BoardPort.Input(Settings.TableYEndstopBoardNumber, Settings.TableYEndstopPortNumber),
				500, Settings.TableYEndstopLocation, Settings.TableYDirection, Settings.TableYMaxSpeed))))));
			spec.Axes.AddRange(GetAspirateAxes());
			spec.Axes.Add(WithMoveSpeed(Settings.DnaReagentMoveSpeed, WithDriverMode(Settings.DnaReagentDriverMode, WithMotorSettings(Settings.DnaReagentStepsPerMillimeter, Settings.DnaReagentTravelLength, Settings.DnaReagentMotorCurrent, Settings.DnaReagentMaxSpeed, Settings.DnaReagentAcceleration, Settings.DnaReagentMaxInstantaneousSpeedChange, WithMotion(Settings.DnaReagentDirection, Settings.DnaReagentEndstopLocation, new FluidAxis(Axis.DnaReagent,
				BoardPort.Driver(Settings.DnaReagentDriverBoardNumber, Settings.DnaReagentDriverPortNumber),
				BoardPort.Input(Settings.DnaReagentEndstopBoardNumber, Settings.DnaReagentEndstopPortNumber),
				Settings.DnaLocation, Settings.DnaSyringeLoadingPosition))))));
			spec.Axes.Add(WithMoveSpeed(Settings.KohMoveSpeed, WithDriverMode(Settings.KohDriverMode, WithMotorSettings(Settings.KohStepsPerMillimeter, Settings.KohTravelLength, Settings.KohMotorCurrent, Settings.KohMaxSpeed, Settings.KohAcceleration, Settings.KohMaxInstantaneousSpeedChange, WithMotion(Settings.KohDirection, Settings.KohEndstopLocation, new FluidAxis(Axis.Koh,
				BoardPort.Driver(Settings.KohDriverBoardNumber, Settings.KohDriverPortNumber),
				BoardPort.Input(Settings.KohEndstopBoardNumber, Settings.KohEndstopPortNumber),
				Settings.KohLocation, Settings.KohSyringeLoadingPosition))))));
			spec.Axes.Add(WithMoveSpeed(Settings.NadhMoveSpeed, WithDriverMode(Settings.NadhDriverMode, WithMotorSettings(Settings.NadhStepsPerMillimeter, Settings.NadhTravelLength, Settings.NadhMotorCurrent, Settings.NadhMaxSpeed, Settings.NadhAcceleration, Settings.NadhMaxInstantaneousSpeedChange, WithMotion(Settings.NadhDirection, Settings.NadhEndstopLocation, new FluidAxis(Axis.Nadh,
				BoardPort.Driver(Settings.NadhDriverBoardNumber, Settings.NadhDriverPortNumber),
				BoardPort.Input(Settings.NadhEndstopBoardNumber, Settings.NadhEndstopPortNumber),
				Settings.NadhLocation, Settings.NadhSyringeLoadingPosition))))));
			spec.Axes.Add(WithMoveSpeed(Settings.BasalMoveSpeed, WithDriverMode(Settings.BasalDriverMode, WithMotorSettings(Settings.BasalStepsPerMillimeter, Settings.BasalTravelLength, Settings.BasalMotorCurrent, Settings.BasalMaxSpeed, Settings.BasalAcceleration, Settings.BasalMaxInstantaneousSpeedChange, WithMotion(Settings.BasalDirection, Settings.BasalEndstopLocation, new FluidAxis(Axis.Basal,
				BoardPort.Driver(Settings.BasalDriverBoardNumber, Settings.BasalDriverPortNumber),
				BoardPort.Input(Settings.BasalEndstopBoardNumber, Settings.BasalEndstopPortNumber),
				Settings.BasalLocation, Settings.BasalSyringeLoadingPosition))))));

			spec.Sensors.Add(new DnaSensorSpec("DNA Sensor", Settings.DnaSensorNumber, Settings.DnaSensorBoardNumber, Settings.DnaSensorPortNumber, Settings.DnaSensorReadHigh, Settings.DnaSensorReadLow, Settings.DnaSensorLocation));
			spec.Sensors.Add(new NadhSensorSpec("NADH Sensor", Settings.NadhSensorNumber, Settings.NadhSensorBoardNumber, Settings.NadhSensorPortNumber, Settings.NadhSensorReadHigh, Settings.NadhSensorReadLow, Settings.NadhSensorLocation));
			return spec;
		}
		/// <summary>The number of sample wells on the table: every well of every tray.</summary>
		public static int GetWellCount(Settings settings)
		{
			return Math.Max(1, settings.HorizontalTrayCount) * Math.Max(1, settings.VerticalTrayCount) * settings.HorizontalWellCount * settings.VerticalWellCount;
		}
		/// <summary>
		/// Adds the trays mounted on the table and their wells. Trays are numbered the way wells are: along X first, then the next row of
		/// trays is one step along Y. Each tray is offset from the first by the tray offset setting times its position in the grid.
		/// </summary>
		private void AddPlates(DeviceSpecification spec)
		{
			for (int trayY = 0; trayY < Math.Max(1, Settings.VerticalTrayCount); trayY++)
			{
				for (int trayX = 0; trayX < Math.Max(1, Settings.HorizontalTrayCount); trayX++)
				{
					var plate = new Plate(new Coordinate(trayX, trayY));
					spec.Plates.Add(plate);
					var baseX = Settings.FirstWellLocation.X + (Settings.PlateOffset.X * trayX);
					var baseY = Settings.FirstWellLocation.Y + (Settings.PlateOffset.Y * trayY);
					// wells are numbered along X first (the next cell is across the tray), then the next row is one step along Y
					for (int y = 0; y < Settings.VerticalWellCount; y++)
					{
						for (int x = 0; x < Settings.HorizontalWellCount; x++)
						{
							var location = new Coordinate(
								((x * Settings.WellOffset.X) + baseX),
								((y * Settings.WellOffset.Y) + baseY));
							new Well(plate, x, y, location);
						}
					}
				}
			}
		}
		/// <summary>Applies the speeds of the coarse and fine homing passes (mm/min). Too fast and a stepper stalls (a high pitched whine and no movement).</summary>
		private static AxisSpec WithHoming(int firstHomingSpeed, int secondHomingSpeed, AxisSpec axis)
		{
			axis.Driver.Endstop.FirstHomingSpeed = firstHomingSpeed;
			axis.Driver.Endstop.SecondHomingSpeed = secondHomingSpeed;
			return axis;
		}
		/// <summary>Applies the speed moves use (mm/min); the board still holds them to the max speed (M203).</summary>
		private static AxisSpec WithMoveSpeed(int moveSpeed, AxisSpec axis)
		{
			axis.MoveSpeed = moveSpeed;
			return axis;
		}
		private static AxisSpec WithDriverMode(DriverMode mode, AxisSpec axis)
		{
			axis.Driver.Mode = mode;
			return axis;
		}
		private static AxisSpec WithMotorSettings(decimal stepsPerMillimeter, int travelLength, int motorCurrent, int maxSpeed, int acceleration, int maxInstantaneousSpeedChange, AxisSpec axis)
		{
			axis.StepsPerMillimeter = stepsPerMillimeter;
			axis.Maximum = travelLength;
			axis.Driver.Current = motorCurrent;
			axis.MaxSpeed = maxSpeed;
			axis.Acceleration = acceleration;
			axis.MaxInstantaneousSpeedChange = maxInstantaneousSpeedChange;
			// the position moved to at startup must stay within the travel
			axis.StartingPosition = Math.Clamp(axis.StartingPosition, axis.Minimum, axis.Maximum);
			return axis;
		}
		/// <summary>
		/// Applies the aspirate actuator settings that the AspirateZAxis constructor does not take (steps, current, driver mode,
		/// move speed, acceleration and jerk); the travel (0 to the stroke), endstop end, direction and max speed come from the constructor.
		/// </summary>
		private AxisSpec WithAspirateSettings(AxisSpec axis)
		{
			axis.StepsPerMillimeter = Settings.AspirateStepsPerMillimeter;
			axis.Driver.Current = Settings.AspirateMotorCurrent;
			axis.Driver.Mode = Settings.AspirateDriverMode;
			axis.MoveSpeed = Settings.AspirateMoveSpeed;
			axis.Acceleration = Settings.AspirateAcceleration;
			axis.MaxInstantaneousSpeedChange = Settings.AspirateMaxInstantaneousSpeedChange;
			// the travel runs from 0 to the stroke (set by the AspirateZAxis constructor), whichever end the endstop is at
			// Move To Start (and the move to starting positions at startup) takes the needle to its start position, up clear of the tray:
			// down is the positive direction, so the end of the travel (the maximum) is at the bottom
			axis.StartingPosition = Math.Clamp(Settings.AspirateActuatorStartPosition, axis.Minimum, axis.Maximum);
			return axis;
		}
		private static AxisSpec WithMotion(Direction direction, EndstopLocation endstopLocation, AxisSpec axis)
		{
			axis.Driver.Direction = direction;
			axis.Driver.Endstop.Location = endstopLocation;
			return axis;
		}
		private List<AxisSpec> GetAspirateAxes()
		{
			var ret = new List<AxisSpec>();
			ret.Add(WithAspirateSettings(new AspirateZAxis(Axis.Aspirate,
				BoardPort.Driver(Settings.AspirateDriverBoardNumber, Settings.AspirateDriverPortNumber),
				BoardPort.Input(Settings.AspirateEndstopBoardNumber, Settings.AspirateEndstopPortNumber),
				Settings.AspirateLocation, Settings.AspirateTravelLength, Settings.AspirateEndstopLocation, Settings.AspirateDirection, Settings.AspirateMaxSpeed,
				Settings.AspirateActuatorStartPosition)));

            return ret;
		}
		private Coordinate GetCoordinateConfigValue(string key)
		{
			var value = ConfigurationManager.AppSettings[key];
			if (value is null)
				throw new ValidationException($"Configuration key {key} not found");
			var x = decimal.Parse(value.Split(',')[0]);
			var y = decimal.Parse(value.Split(',')[1]);
			return new Coordinate(x, y);
		}

		public Settings Settings { get; set; }
		public DeviceSpecification? DeviceSpecification { get; private set; }
	}
}
