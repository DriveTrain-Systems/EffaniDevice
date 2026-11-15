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
			_processEngine.QueueCommand(new TableToTraySterilizationLocation(_device));
		}
		[Command]
		public async Task AlignForTrayLoading()
		{
			_processEngine.QueueCommand(new TableToTrayLoadingLocation(_device));
		}
		[Command]
		public async Task AlignForWaste()
		{
			_processEngine.QueueCommand(new AlignTableForWaste(_device));
		}
		[Command]
		public async Task AlignToFirstWell(object parameter)
		{
			var plate = _config.DeviceSpecification.Plates.First();
			var well = plate.Wells.First();
			CurrentWell = well;
			if (parameter is AxisSpec axisSpec)
				_processEngine.QueueCommand(new AlignWellToAxis(axisSpec.Axis, well, _device));
			else if (parameter is SensorSpec sensorSpec)
				_processEngine.QueueCommand(new AlignWellToLocation(sensorSpec.Location, well, _device));
		}
		[Command]
		public async Task AlignToLastWell(object parameter)
		{
			// the last well of the tray the current well is on, so every tray can be reached: Align Next Well steps from here onto the next tray
			var plates = _config.DeviceSpecification.Plates;
			var plate = plates.FirstOrDefault(p => p == CurrentWell?.Plate) ?? plates.First();
			var well = plate.Wells.Last();
			CurrentWell = well;
			if (parameter is AxisSpec axisSpec)
				_processEngine.QueueCommand(new AlignWellToAxis(axisSpec.Axis, well, _device));
			else if (parameter is SensorSpec sensorSpec)
				_processEngine.QueueCommand(new AlignWellToLocation(sensorSpec.Location, well, _device));
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
		[Command]
		public async Task GenerateConfigFiles()
		{
			try
			{
				// The active settings object is replaced when a settings version is activated or reset, so point the configuration at the current one.
				_config.Settings = _settingsService.Settings;
					_pendingConfigSignature = DeviceConfiguration.GetConfigSignature(_settingsService.Settings);
				var fileLocation = _config
					.GenerateConfigFiles(SettingsService.DataFolder);
				string messageBoxText = $"Config generated at {fileLocation}";
				var warnings = _config.GetConnectionWarnings();
				if (warnings.Count > 0)
					messageBoxText += $"\n\nWARNING - check these settings in Electronic Settings:\n- {string.Join("\n- ", warnings)}";
				string caption = "Generate Config";

				if (!System.IO.File.Exists(fileLocation))
				{
					// generation failed: fileLocation holds the error message
					DXMessageBox.Show(messageBoxText, caption);
					return;
				}
				if (IsAssayRunning)
						messageBoxText += "\n\nThe generated files were NOT sent to the controller board because an assay is running.";
					else
					{
						_processEngine.QueueCommand(new SendConfigFiles(_config.GeneratedFiles, SendConfigFilesComplete, _device));
						messageBoxText += "\n\nSending the generated files to the controller board, which will then be rebooted. The result will appear in the console messages.";
					}
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
				_processEngine.QueueCommand(new SwitchLaser(sensor is DnaSensorSpec, on: false, _device));
		}
		[Command]
		public async Task LaserOn(SensorSpec sensor)
		{
			if (sensor is DnaSensorSpec or NadhSensorSpec)
				_processEngine.QueueCommand(new SwitchLaser(sensor is DnaSensorSpec, on: true, _device));
		}
		[Command]
		public async Task MoveRapid(List<object> parameters)
		{
			var axisSpec = (AxisSpec)parameters[0];
			var distance = Convert.ToDecimal(parameters[1]);
			_processEngine.QueueCommand(new MoveRelative(distance, _device, axisSpec));
		}
		[Command]
		public async Task MoveToStart(AxisSpec axisSpec)
		{
			_processEngine.QueueCommand(new MoveToStartingLocation(axisSpec.Axis, _device));
		}
		[Command]
		public async Task MoveToStartingLocations()
		{
			_processEngine.QueueCommand(new MoveToStartingLocations(_device));
		}
		[Command]
		public async Task TrayUvOff()
		{
			_processEngine.QueueCommand(new TrayUvOff(_device));
		}
		[Command]
		public async Task TrayUvOn()
		{
			_processEngine.QueueCommand(new TrayUvOn(_device));
		}
		[Command]
		public void PrimeFluid()
		{
			var job = new Job("Priming fluid pumps.");
			job.Commands.Add(new EmitFluid(Axis.DnaReagent, _config.Settings.InitialDnaFluidPrimingVolume, _device));
			job.Commands.Add(new EmitFluid(Axis.Koh, _config.Settings.InitialKohFluidPrimingVolume, _device));
			job.Commands.Add(new EmitFluid(Axis.Nadh, _config.Settings.InitialNadhFluidPrimingVolume, _device));
			job.Commands.Add(new EmitFluid(Axis.Basal, _config.Settings.InitialBasalFluidPrimingVolume, _device));
			_processEngine.QueueJob(job);
		}
		[Command]
		public async Task GetBoardDiagnostics(int boardNumber)
		{
			_processEngine.QueueCommand(new GetBoardDiagnostics(boardNumber, _device, ShowBoardDiagnostics));
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
			_processEngine.QueueCommand(new Home(_device, axisSpec));
		}
		//[Command]
		//public async Task AspirateFluid(AxisSpec axisSpec)
		//{
		//	_processEngine.QueueCommand(new AspirateFluid(_config.TestMediaAspirationVolume, _device));
		//}
		[Command]
		public async Task HomeAll()
		{
			_processEngine.QueueCommand(new HomeAll(_device));
		}
		[Command]
		public async Task AlignToNextWell(object parameter)
		{
			// across all trays: the well after the last one of a tray is the first one of the next tray
			var wells = _config.DeviceSpecification.Plates.SelectMany(p => p.Wells).ToList();
			var nextIndex = wells.IndexOf(CurrentWell) + 1; // the first well when the current well is not on the trays as they are now configured
			if (nextIndex >= wells.Count)
				return;
			var well = wells[nextIndex];
			CurrentWell = well;
			if (parameter is AxisSpec axisSpec)
				_processEngine.QueueCommand(new AlignWellToAxis(axisSpec.Axis, well, _device));
			else if (parameter is SensorSpec sensorSpec)
				_processEngine.QueueCommand(new AlignWellToLocation(sensorSpec.Location, well, _device));
		}
		[Command]
		public async Task ChamberUvOff()
		{
			_processEngine.QueueCommand(new ChamberUvOff(_device));
		}
		[Command]
		public async Task ChamberUvOn()
		{
			_processEngine.QueueCommand(new ChamberUvOn(_device));
		}
		[Command]
		public async Task ClearMessages()
		{
			ConsoleMessages.Clear();
		}
		[Command]
		public async Task EmitFluid(AxisSpec axisSpec)
		{
			_processEngine.QueueCommand(new EmitFluid(axisSpec.Axis, EmitFluidVolume, _device));
		}
		[Command]
		public async Task ReprimeIfNeeded(AxisSpec axisSpec)
		{
			_processEngine.QueueCommand(new ReprimeIfNeeded(axisSpec.Axis, _device));
		}
		[Command]
		public async Task ApplyConfig()
		{
			_device.ApplyConfig();
		}
		[Command]
		public async Task Aspirate(AxisSpec axisSpec)
		{
			_processEngine.QueueCommand(new Aspirate(_config.Settings.KohAspirationVolume, _device));
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
					_processEngine.QueueCommand(new ReadDna(_device, null));
					break;
				case NadhSensorSpec:
					_processEngine.QueueCommand(new ReadNadh(_device, null));
					break;
			}
			Mouse.OverrideCursor = Cursors.Arrow;
		}
		[Command]
		public async Task EnableWifi()
		{
			_processEngine.QueueCommand(new SetWifiEnabled(true, _device));
		}
		[Command]
		public async Task DisableWifi()
		{
			_processEngine.QueueCommand(new SetWifiEnabled(false, _device));
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
			_processEngine.QueueCommand(new SetWifi(Settings.WifiAuthentication, Settings.WifiSsid, Settings.WifiUsername, Settings.WifiPassword, _device));
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
			_processEngine.QueueCommand(new SendCommand(_sendCommandText, _device, ReportSendResponse, TimeSpan.FromSeconds(Math.Max(1, SendTimeoutSeconds))));
		}
	}
}
