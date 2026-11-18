using Effani.Device.DataCollection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using DevExpress.Mvvm;
using Effani.Device.Services;
using DevExpress.Mvvm.DataAnnotations;
using DevExpress.Mvvm.Xpf;
using Drivetrain.Robotics.Control;
using System.Windows.Input;
using Effani.Device.Control;
using Drivetrain.Robotics.Process;
using DevExpress.Xpf.Core;

namespace Effani.Device.UI
{
	public partial class UtilitiesViewModel : ViewModelBase, IDisposable
	{
		[Command]
		public async Task AlignForSterilization()
		{
			Queue(new TableToTraySterilizationLocation(_device));
		}
		[Command]
		public async Task AlignForTrayLoading()
		{
			Queue(new TableToTrayLoadingLocation(_device));
		}
		[Command]
		public async Task AlignForWaste()
		{
			Queue(new AlignTableForWaste(_device));
		}
		[Command]
		public async Task AlignToFirstWell(object parameter)
		{
			RefreshWells();
			var plate = _config.DeviceSpecification.Plates.First();
			var well = plate.Wells.First();
			CurrentWell = well;
			if (parameter is AxisSpec axisSpec)
				Queue(new AlignWellToAxis(axisSpec.Axis, well, _device));
			else if (parameter is SensorSpec sensorSpec)
				Queue(new AlignWellToLocation(sensorSpec.Location, well, _device));
		}
		[Command]
		public async Task AlignToLastWell(object parameter)
		{
			RefreshWells();
			// the last well of the tray the current well is on, so every tray can be reached: Align Next Well steps from here onto the next tray
			var plates = _config.DeviceSpecification.Plates;
			var plate = plates.FirstOrDefault(p => p == CurrentWell?.Plate) ?? plates.First();
			var well = plate.Wells.Last();
			CurrentWell = well;
			if (parameter is AxisSpec axisSpec)
				Queue(new AlignWellToAxis(axisSpec.Axis, well, _device));
			else if (parameter is SensorSpec sensorSpec)
				Queue(new AlignWellToLocation(sensorSpec.Location, well, _device));
		}
		/// <summary>
		/// Works the wells out again from the settings as they are now, before an Align uses them: First Well Location, the well and
		/// tray offsets and the counts may have been edited on the Settings tab since Tools was opened, and the axis and sensor
		/// locations an Align also uses are always the current ones. The current well stays the same well of the same tray.
		/// </summary>
		private void RefreshWells()
		{
			if (IsAssayRunning)
				return; // the assay holds these wells, and the settings cannot be edited while it runs
			var current = _currentWell;
			var tray = current is null ? -1 : _config.DeviceSpecification.Plates.IndexOf(current.Plate);
			_config.RefreshPlates();
			if (current is null)
				return;
			var plates = _config.DeviceSpecification.Plates;
			// null (the tray or well no longer exists) makes the first well the current one again
			CurrentWell = tray >= 0 && tray < plates.Count ? plates[tray].Wells.FirstOrDefault(w => w.X == current.X && w.Y == current.Y) : null;
		}
		/// <summary>
		/// The active settings were replaced by others (a version was activated or saved as new, or the settings were reset): has
		/// the device and this window work from them. The configuration is rebuilt on the process engine thread, between commands,
		/// so no command is using it at that moment; until then moves go on using the settings Tools was opened with.
		/// </summary>
		private void UseActiveSettings()
		{
			if (IsAssayRunning)
				return;
			Queue(new NotifyComplete(_ =>
			{
				_device.ResetConfiguration();
				Application.Current?.Dispatcher.BeginInvoke(() =>
				{
					// the axis and sensor boxes show and act on the rebuilt configuration
					Axes.Clear();
					foreach (var axis in _device.Configuration.DeviceSpecification.Axes)
						Axes.Add(axis);
					Sensors.Clear();
					foreach (var sensor in _device.Configuration.DeviceSpecification.Sensors)
						Sensors.Add(sensor);
					CurrentWell = null;
				});
			}, "Use the settings now active.", _device));
		}
		/// <summary>Reports the outcome of sending the generated files to the controller board; runs on the process engine thread.</summary>
		private void SendConfigFilesComplete(string? error)
		{
			// the board now runs a config built from the current settings: rebuild Effani's own picture of the axes (limits, start
			// positions, endstop ends) from the same settings, so moves are worked out the way the board now expects them. This runs on
			// the process engine thread between commands, so no command is using the configuration at this moment.
			if (error is null)
				_device.ResetConfiguration();
			Application.Current.Dispatcher.BeginInvoke(() =>
			{
				var now = DateTime.Now.ToString("H:mm:ss");
				if (error is null)
				{
					// the axis boxes show and act on the rebuilt axes
					Axes.Clear();
					foreach (var axis in _device.Configuration.DeviceSpecification.Axes)
						Axes.Add(axis);
					ConsoleMessages.Insert(0, $"{now} Generated config files sent to the controller board; the board was rebooted.");
						// the board has this config now: the Generate Config button goes back to normal unless settings changed since
						if (_pendingConfigSignature != null)
							SentConfigSignature = _pendingConfigSignature;
						UpdateConfigNeedsGenerating();
					return;
				}
				ConsoleMessages.Insert(0, $"{now} Sending the generated config files to the controller board failed: {error}");
				DXMessageBox.Show($"The config files were generated but could not be sent to the controller board (it was not rebooted).\n\n{error}", "Send Config Files");
			});
		}
		/// <summary>Closes the controller board's COM port so a terminal program can open it; the next command sent reopens it.</summary>
		[Command]
		public void ReleaseComPort()
		{
			var now = DateTime.Now.ToString("H:mm:ss");
			if (_processEngine.IsBusy)
			{
				ConsoleMessages.Insert(0, $"{now} The COM port was not released: commands are queued or executing.");
				return;
			}
			try
			{
				var name = _device.ClosePort();
				ConsoleMessages.Insert(0, name is null
					? $"{now} The COM port is not open, so other programs can already use it."
					: $"{now} {name} released: other programs (a terminal) can open it now. Close them before sending a command from here, which reopens it.");
			}
			catch (Exception ex)
			{
				ConsoleMessages.Insert(0, $"{now} Releasing the COM port failed: {ex.Message}");
			}
		}
		/// <summary>
		/// Generates config.g and the homing files from the settings as they are now and, unless an assay is running, queues
		/// sending them to the controller board (which is then rebooted). Returns where the files were written (the error message
		/// when they could not be generated), whether they were generated and whether sending was queued.
		/// </summary>
		private (string Location, bool Generated, bool Sent) GenerateAndSendConfig()
		{
			// The active settings object is replaced when a settings version is activated or reset, so point the configuration at the current one.
			_config.Settings = _settingsService.Settings;
			_pendingConfigSignature = DeviceConfiguration.GetConfigSignature(_settingsService.Settings);
			var fileLocation = _config.GenerateConfigFiles(SettingsService.DataFolder);
			if (!System.IO.File.Exists(fileLocation))
				return (fileLocation, false, false);
			if (IsAssayRunning)
				return (fileLocation, true, false);
			Queue(new SendConfigFiles(_config.GeneratedFiles, SendConfigFilesComplete, _device));
			_configGeneratedSinceOpened = true;
			return (fileLocation, true, true);
		}
		[Command]
		public async Task GenerateConfigFiles()
		{
			try
			{
				var (fileLocation, generated, sent) = GenerateAndSendConfig();
				string messageBoxText = $"Config generated at {fileLocation}";
				var warnings = _config.GetConnectionWarnings();
				if (warnings.Count > 0)
					messageBoxText += $"\n\nWARNING - check these settings in Electronic Settings:\n- {string.Join("\n- ", warnings)}";
				string caption = "Generate Config";

				if (!generated)
				{
					// generation failed: fileLocation holds the error message
					DXMessageBox.Show(messageBoxText, caption);
					return;
				}
				if (!sent)
					messageBoxText += "\n\nThe generated files were NOT sent to the controller board because an assay is running.";
				else
					messageBoxText += "\n\nSending the generated files to the controller board, which will then be rebooted. The result will appear in the console messages.";
					var openFolder = new UICommand { Id = "open", Caption = "Open Folder", IsDefault = true };
				var close = new UICommand { Id = "close", Caption = "Close", IsCancel = true };
				var dialog = new DXDialogWindow(caption, new[] { openFolder, close })
				{
					Content = new System.Windows.Controls.TextBlock { Text = messageBoxText, TextWrapping = TextWrapping.Wrap, MaxWidth = 600, Margin = new Thickness(15) },
					SizeToContent = SizeToContent.WidthAndHeight,
					WindowStartupLocation = WindowStartupLocation.CenterOwner,
					Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow
				};
				if (dialog.ShowDialogWindow() == openFolder)
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{fileLocation}\"") { UseShellExecute = true });
			}
			catch (Exception ex)
			{
				var a = ex;
			}
		}
		[Command]
		public async Task LaserOff(SensorSpec sensor)
		{
			if (sensor is DnaSensorSpec or NadhSensorSpec)
				Queue(new SwitchLaser(sensor is DnaSensorSpec, on: false, _device));
		}
		[Command]
		public async Task LaserOn(SensorSpec sensor)
		{
			if (sensor is DnaSensorSpec or NadhSensorSpec)
				Queue(new SwitchLaser(sensor is DnaSensorSpec, on: true, _device));
		}
		/// <summary>
		/// Queues a move of an axis to a position (mm) on its travel. Returns false, queuing nothing, when the position is outside
		/// the axis's travel.
		/// </summary>
		public bool MoveAxisTo(AxisSpec axisSpec, decimal position)
		{
			if (position < axisSpec.Minimum || position > axisSpec.Maximum)
				return false;
			Queue(new MoveAbsolute(position, _device, axisSpec));
			return true;
		}
		/// <summary>Queues a move of an axis by a distance (mm) from where it is: negative is back, toward 0.</summary>
		public void MoveAxisBy(AxisSpec axisSpec, decimal distance)
		{
			if (distance != 0)
				Queue(new MoveRelative(distance, _device, axisSpec));
		}
		[Command]
		public async Task MoveRapid(List<object> parameters)
		{
			var axisSpec = (AxisSpec)parameters[0];
			var distance = Convert.ToDecimal(parameters[1]);
			Queue(new MoveRelative(distance, _device, axisSpec));
		}
		[Command]
		public async Task MoveToStart(AxisSpec axisSpec)
		{
			Queue(new MoveToStartingLocation(axisSpec.Axis, _device));
		}
		[Command]
		public async Task MoveToStartingLocations()
		{
			Queue(new MoveToStartingLocations(_device));
		}
		[Command]
		public async Task TrayUvOff()
		{
			Queue(new TrayUvOff(_device));
		}
		[Command]
		public async Task TrayUvOn()
		{
			Queue(new TrayUvOn(_device));
		}
		[Command]
		public void PrimeFluid()
		{
			var job = new Job("Priming fluid pumps.");
			job.Commands.Add(new EmitFluid(Axis.DnaReagent, _config.Settings.InitialDnaFluidPrimingVolume, _device));
			job.Commands.Add(new EmitFluid(Axis.Koh, _config.Settings.InitialKohFluidPrimingVolume, _device));
			job.Commands.Add(new EmitFluid(Axis.Nadh, _config.Settings.InitialNadhFluidPrimingVolume, _device));
			job.Commands.Add(new EmitFluid(Axis.Basal, _config.Settings.InitialBasalFluidPrimingVolume, _device));
			Queue(job);
		}
		[Command]
		public async Task GetBoardDiagnostics(int boardNumber)
		{
			Queue(new GetBoardDiagnostics(boardNumber, _device, ShowBoardDiagnostics));
		}
		/// <summary>Shows a board's diagnostics in a window of their own; called on the process engine thread.</summary>
		private void ShowBoardDiagnostics(GetBoardDiagnostics command)
		{
			Application.Current.Dispatcher.BeginInvoke(() =>
			{
				var caption = $"Board {command.BoardNumber} Diagnostics";
				var report = command.Result;
				var now = DateTime.Now.ToString("H:mm:ss");
				if (string.IsNullOrWhiteSpace(report) || report.StartsWith("Error"))
				{
					var reason = string.IsNullOrWhiteSpace(report) ? "the board did not reply" : report;
					ConsoleMessages.Insert(0, $"{now} No diagnostics from board {command.BoardNumber}: {reason}");
					DXMessageBox.Show($"No diagnostics were received from board {command.BoardNumber}: {reason}", caption);
					return;
				}

				ConsoleMessages.Insert(0, $"{now} Diagnostics received from board {command.BoardNumber}.");
				var window = new ThemedWindow
				{
					Title = caption,
					Content = new System.Windows.Controls.TextBox
					{
						Text = report,
						IsReadOnly = true, // still selectable, so it can be copied
						FontFamily = new System.Windows.Media.FontFamily("Consolas"),
						VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
						HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
						Margin = new Thickness(10),
					},
					Width = 900,
					Height = 650,
					WindowStartupLocation = WindowStartupLocation.CenterOwner,
					Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow,
				};
				window.Show(); // not modal, so the reports of several boards can be compared
			});
		}
		[Command]
		public async Task Home(AxisSpec axisSpec)
		{
			Queue(new Home(_device, axisSpec));
		}
		//[Command]
		//public async Task AspirateFluid(AxisSpec axisSpec)
		//{
		//	Queue(new AspirateFluid(_config.TestMediaAspirationVolume, _device));
		//}
		[Command]
		public async Task HomeAll()
		{
			Queue(new HomeAll(_device));
		}
		[Command]
		public async Task AlignToNextWell(object parameter)
		{
			RefreshWells();
			// across all trays: the well after the last one of a tray is the first one of the next tray
			var wells = _config.DeviceSpecification.Plates.SelectMany(p => p.Wells).ToList();
			var nextIndex = wells.IndexOf(CurrentWell) + 1; // the first well when the current well is not on the trays as they are now configured
			if (nextIndex >= wells.Count)
				return;
			var well = wells[nextIndex];
			CurrentWell = well;
			if (parameter is AxisSpec axisSpec)
				Queue(new AlignWellToAxis(axisSpec.Axis, well, _device));
			else if (parameter is SensorSpec sensorSpec)
				Queue(new AlignWellToLocation(sensorSpec.Location, well, _device));
		}
		[Command]
		public async Task ChamberUvOff()
		{
			Queue(new ChamberUvOff(_device));
		}
		[Command]
		public async Task ChamberUvOn()
		{
			Queue(new ChamberUvOn(_device));
		}
		[Command]
		public async Task ClearMessages()
		{
			ConsoleMessages.Clear();
		}
		/// <summary>The queue is not cleared from here while an assay is running: its jobs belong to the assay, which is cancelled from the assay screen.</summary>
		public bool CanClearCommandQueue() => !IsAssayRunning;
		/// <summary>
		/// Discards every command that is waiting to run. The command being executed is not interrupted: the controller board does
		/// not read anything sent to it over USB until it has finished the command it is running, so a move or homing under way
		/// always completes.
		/// </summary>
		[Command]
		public void ClearCommandQueue()
		{
			var executing = _processEngine.ExecutingCommand;
			var discarded = _processEngine.ClearPendingJobs();
			// a sensor reading that was still waiting in the queue will not be taken now: its button goes back to its plain caption
			foreach (var sensor in Sensors)
			{
				var waiting = sensor is DnaSensorSpec ? _dnaSensorReading == ReadingPending && executing is not ReadDna
					: sensor is NadhSensorSpec && _nadhSensorReading == ReadingPending && executing is not ReadNadh;
				if (waiting)
					SetSensorReading(sensor, null);
			}
			var now = DateTime.Now.ToString("H:mm:ss");
			var running = executing is null ? "" : $" {executing.Description} was already running and will finish.";
			ConsoleMessages.Insert(0, discarded == 0
				? $"{now} Command queue cleared: nothing was waiting.{running}"
				: $"{now} Command queue cleared: {discarded} queued command{(discarded == 1 ? "" : "s")} discarded.{running}");
		}
		[Command]
		public async Task EmitFluid(AxisSpec axisSpec)
		{
			Queue(new EmitFluid(axisSpec.Axis, EmitFluidVolume, _device));
		}
		[Command]
		public async Task ReprimeIfNeeded(AxisSpec axisSpec)
		{
			Queue(new ReprimeIfNeeded(axisSpec.Axis, _device));
		}
		[Command]
		public async Task ApplyConfig()
		{
			_device.ApplyConfig();
		}
		[Command]
		public async Task Aspirate(AxisSpec axisSpec)
		{
			Queue(new Aspirate(_config.Settings.KohAspirationVolume, _device));
		}
		[Command]
		public async Task UploadSettings()
		{
			await _settingsService.UploadSettings();
		}
		[Command]
		public async Task Read(SensorSpec sensor)
		{
			Mouse.OverrideCursor = Cursors.Wait;
			switch (sensor)
			{
				case DnaSensorSpec:
					SetSensorReading(sensor, ReadingPending);
					Queue(new ReadDna(_device, command => ShowSensorReading(sensor, command.Result)));
					break;
				case NadhSensorSpec:
					SetSensorReading(sensor, ReadingPending);
					Queue(new ReadNadh(_device, command => ShowSensorReading(sensor, command.Result)));
					break;
			}
			Mouse.OverrideCursor = Cursors.Arrow;
		}
		/// <summary>The caption of the DNA sensor's Read Sensor button: once a reading has been asked for from here, it carries the reading.</summary>
		public string DnaReadSensorText => ReadSensorText(_dnaSensorReading);
		/// <summary>The caption of the NADH sensor's Read Sensor button: once a reading has been asked for from here, it carries the reading.</summary>
		public string NadhReadSensorText => ReadSensorText(_nadhSensorReading);
		private static string ReadSensorText(string? reading) => reading is null ? "Read Sensor" : $"Read Sensor: {reading}";
		/// <summary>Shown on a Read Sensor button from the moment it is pressed until the reading arrives, so the last reading is not taken for the new one.</summary>
		private const string ReadingPending = "…";
		/// <summary>Puts the reading a sensor read came back with on that sensor's button; runs on the process engine thread.</summary>
		private void ShowSensorReading(SensorSpec sensor, string? result)
		{
			// a failed reading comes back as text starting with "Error"; the whole of it is in Messages (the engine reports every reading there)
			var reading = String.IsNullOrWhiteSpace(result) ? "no reply"
				: result.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ? "error"
				: result.Trim();
			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher is null || dispatcher.CheckAccess())
				SetSensorReading(sensor, reading);
			else
				dispatcher.BeginInvoke(() => SetSensorReading(sensor, reading));
		}
		private void SetSensorReading(SensorSpec sensor, string? reading)
		{
			switch (sensor)
			{
				case DnaSensorSpec:
					_dnaSensorReading = reading;
					RaisePropertyChanged(nameof(DnaReadSensorText));
					break;
				case NadhSensorSpec:
					_nadhSensorReading = reading;
					RaisePropertyChanged(nameof(NadhReadSensorText));
					break;
			}
		}
		private string? _dnaSensorReading;
		private string? _nadhSensorReading;
		[Command]
		public async Task EnableWifi()
		{
			Queue(new SetWifiEnabled(true, _device));
		}
		[Command]
		public async Task DisableWifi()
		{
			Queue(new SetWifiEnabled(false, _device));
		}
		[Command]
		public async Task SetWifi()
		{
			if (string.IsNullOrWhiteSpace(Settings.WifiSsid))
			{
				DXMessageBox.Show("Enter a WiFi SSID first.", "Set WiFi");
				return;
			}
			if (Settings.WifiAuthentication != WifiAuthentication.Personal && string.IsNullOrWhiteSpace(Settings.WifiUsername))
			{
				DXMessageBox.Show("Enter a WiFi username first; the selected authentication needs one.", "Set WiFi");
				return;
			}
			Queue(new SetWifi(Settings.WifiAuthentication, Settings.WifiSsid, Settings.WifiUsername, Settings.WifiPassword, _device));
		}
		/// <summary>Updates are not checked for (or installed) while an assay is running.</summary>
		public bool CanCheckForUpdates() => !IsAssayRunning;
		[Command]
		public async Task CheckForUpdates()
		{
			if (_checkingForUpdates)
				return;
			_checkingForUpdates = true;
			try
			{
				UpdateStatus = "Checking for updates...";
				var current = typeof(UtilitiesViewModel).Assembly.GetName().Version ?? new Version(0, 0);
				var result = await _updateChecker.CheckAsync(current);
				UpdateStatus = result.Message;
				if (!result.IsUpdateAvailable)
					return;

				if (IsAssayRunning)
				{
					UpdateStatus += " Install it once the assay has finished.";
					return;
				}
				var prompt = $"{result.Message}\n\nThe updater will close this program to install it. Run the updater now?";
				if (DXMessageBox.Show(prompt, "Software Update", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
					UpdateChecker.StartUpdater();
			}
			catch (Exception ex)
			{
				UpdateStatus = $"Could not start the updater: {ex.Message}";
			}
			finally
			{
				_checkingForUpdates = false;
			}
		}
		[Command]
		public async Task Send()
		{
			if (String.IsNullOrWhiteSpace(_sendCommandText))
				return;
			Queue(new SendCommand(_sendCommandText, _device, ReportSendResponse, TimeSpan.FromSeconds(Math.Max(1, SendTimeoutSeconds))));
		}
	}
}
