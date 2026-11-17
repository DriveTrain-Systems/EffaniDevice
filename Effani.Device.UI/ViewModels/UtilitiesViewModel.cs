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
using Drivetrain.Robotics.Process;
using Effani.Device.Control;
using System.Collections.ObjectModel;
using DevExpress.Xpf.WindowsUI;
using DevExpress.Xpf.Editors;

namespace Effani.Device.UI
{
	public partial class UtilitiesViewModel : ViewModelBase, IDisposable
	{
		#region Constructors
		public UtilitiesViewModel(ISettingsService settingsService, ProcessEngine processEngine, EffaniDeviceControl device, ObservableCollection<string> consoleMessages)
		{
			ConsoleMessages = consoleMessages;
			_device = device;
			_processEngine = processEngine;
			_settingsService = settingsService;
			_sendTimeoutSeconds = Math.Max(1, _device.Settings.BoardReadyTimeoutSeconds);
			AttachSettings();
			RememberSettings();

			Axes = new ObservableCollection<AxisSpec>(_device.Configuration.DeviceSpecification.Axes);
			Sensors = new ObservableCollection<SensorSpec>(_device.Configuration.DeviceSpecification.Sensors);

			_executingTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			_executingTimer.Tick += (s, e) => RefreshCommandQueue();
			_executingTimer.Start();
			RefreshCommandQueue();
			_device.PositionsChanged += Device_PositionsChanged;
			_device.OutputsChanged += Device_OutputsChanged;
			// find out which axes are homed, so the move buttons of the others can be disabled
			_processEngine.QueueCommand(new ReadPositions(_device));
		}
		#endregion

		#region Public methods
		public void Dispose()
		{
			_executingTimer.Stop();
			if (_attachedSettings != null)
				_attachedSettings.PropertyChanged -= Settings_PropertyChanged;
			_device.PositionsChanged -= Device_PositionsChanged;
			_device.OutputsChanged -= Device_OutputsChanged;
		}
		#endregion

		#region Private methods
		private void Device_OutputsChanged(object? sender, EventArgs e) =>
			Application.Current?.Dispatcher.BeginInvoke(() =>
			{
				RaisePropertyChanged(nameof(TrayUvIsOn));
				RaisePropertyChanged(nameof(ChamberUvIsOn));
				RaisePropertyChanged(nameof(NadhLaserIsOn));
				RaisePropertyChanged(nameof(DnaLaserIsOn));
			});
		/// <summary>Follows the active settings object (it is replaced when a version is activated or reset) to keep <see cref="ConfigNeedsGenerating"/> current.</summary>
		private void AttachSettings()
		{
			if (_attachedSettings != null)
				_attachedSettings.PropertyChanged -= Settings_PropertyChanged;
			_attachedSettings = _settingsService.Settings;
			_attachedSettings.PropertyChanged += Settings_PropertyChanged;
			UpdateConfigNeedsGenerating();
			UpdateLocationCautions();
			UpdateHasSettingsChanges();
		}
		private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			UpdateConfigNeedsGenerating();
			UpdateLocationCautions();
			UpdateHasSettingsChanges();
		}
		/// <summary>
		/// Checks each component location (the fluid ports, the needle, the sensors and the biopsy location) against the trays, their wells and the
		/// table's travel as the settings now have them; the Settings pages show what <see cref="LocationCautions"/> holds under
		/// the location's fields. Any of those settings can cause a caution, so all are checked whenever one changes.
		/// </summary>
		private void UpdateLocationCautions()
		{
			var settings = _settingsService.Settings;
			var cautions = new Dictionary<string, string?>();
			foreach (var (name, location) in new (string, Coordinate)[]
			{
				(nameof(Settings.DnaSensorLocation), settings.DnaSensorLocation),
				(nameof(Settings.NadhSensorLocation), settings.NadhSensorLocation),
				(nameof(Settings.NadhLocation), settings.NadhLocation),
				(nameof(Settings.DnaLocation), settings.DnaLocation),
				(nameof(Settings.KohLocation), settings.KohLocation),
				(nameof(Settings.BasalLocation), settings.BasalLocation),
				(nameof(Settings.AspirateLocation), settings.AspirateLocation),
				(nameof(Settings.BiopsyLocation), settings.BiopsyLocation),
			})
			{
				try { cautions[name] = location is null ? null : DeviceConfiguration.GetWellReachCaution(settings, location); }
				catch { cautions[name] = null; } // half-edited settings: no caution rather than an error
			}
			if (_locationCautions.Count == cautions.Count && cautions.All(c => _locationCautions.TryGetValue(c.Key, out var was) && was == c.Value))
				return;
			_locationCautions = cautions;
			RaisePropertyChanged(nameof(LocationCautions));
		}
		/// <summary>
		/// For each component location setting (by the setting's name): null when every well of every tray can be brought to it,
		/// otherwise the caution to show under its fields.
		/// </summary>
		public IReadOnlyDictionary<string, string?> LocationCautions => _locationCautions;
		private Dictionary<string, string?> _locationCautions = new();
		private void UpdateConfigNeedsGenerating()
		{
			string current;
			try { current = DeviceConfiguration.GetConfigSignature(_settingsService.Settings); }
			catch { return; } // half-edited settings that cannot be turned into a config: leave the state as it was
			// the first time through there is no record of what the board has: assume it matches the settings as they are now
			if (s_sentConfigSignature == null)
				SentConfigSignature = LoadSentSignature() ?? current;
			var needs = current != s_sentConfigSignature;
			if (needs == _configNeedsGenerating)
				return;
			_configNeedsGenerating = needs;
			RaisePropertyChanged(nameof(ConfigNeedsGenerating));
		}
		private static string SentSignaturePath => System.IO.Path.Combine(SettingsService.DataFolder, "sent-config.sig");
		private static string? LoadSentSignature()
		{
			try { return System.IO.File.Exists(SentSignaturePath) ? System.IO.File.ReadAllText(SentSignaturePath).Trim() : null; }
			catch { return null; }
		}
		/// <summary>The fingerprint of the config last sent to the board (kept in a file so it survives a restart).</summary>
		private static string? SentConfigSignature
		{
			get => s_sentConfigSignature;
			set
			{
				s_sentConfigSignature = value;
				try
				{
					System.IO.Directory.CreateDirectory(SettingsService.DataFolder);
					System.IO.File.WriteAllText(SentSignaturePath, value ?? "");
				}
				catch { }
			}
		}
		private bool IsOutputOn(int port) => _device.OutputStates.TryGetValue(port, out var on) && on;
		private void Device_PositionsChanged(object? sender, EventArgs e) =>
			Application.Current?.Dispatcher.BeginInvoke(() =>
			{
				RaisePropertyChanged(nameof(Positions));
				RaisePropertyChanged(nameof(Homed));
			});
		private void RefreshCommandQueue()
		{
			CommandQueue.Refresh(_processEngine);
			var busy = _processEngine.IsBusy;
			if (busy == _isCommandQueueBusy)
				return;
			_isCommandQueueBusy = busy;
			RaisePropertyChanged(nameof(IsCommandQueueBusy));
		}
		#endregion

		#region Properties
		public ObservableCollection<string> ConsoleMessages { get; init; }
		/// <summary>The numbers of the controller boards (0 is the main board), from the Board Count setting.</summary>
		public IReadOnlyList<int> Boards => Enumerable.Range(0, Math.Max(1, _device.Settings.ControllerBoardCount)).ToList();
		/// <summary>Everything still to run, in order, with the executing command flagged (and counting down while the board is waited for).</summary>
		public CommandQueueDisplay CommandQueue { get; } = new();
		/// <summary>Where each axis was after the last move or home, by axis letter; read from the board after every move.</summary>
		public IReadOnlyDictionary<string, decimal> Positions => _device.Positions;
		/// <summary>Which axes are homed, by axis letter; an axis's move buttons are disabled until it is.</summary>
		public IReadOnlyDictionary<string, bool> Homed => _device.Homed;
		/// <summary>
		/// Whether the tray UV light (output port 0) is on, as the board last reported it. Setting it queues the command that switches
		/// the light; the switch then shows what the board reports once the command has run.
		/// </summary>
		public bool TrayUvIsOn
		{
			get => IsOutputOn(0);
			set
			{
				if (value != IsOutputOn(0))
					_processEngine.QueueCommand(value ? new TrayUvOn(_device) : new TrayUvOff(_device));
			}
		}
		/// <summary>Whether the chamber UV light (output port 1) is on, as the board last reported it; setting it switches the light.</summary>
		public bool ChamberUvIsOn
		{
			get => IsOutputOn(1);
			set
			{
				if (value != IsOutputOn(1))
					_processEngine.QueueCommand(value ? new ChamberUvOn(_device) : new ChamberUvOff(_device));
			}
		}
		/// <summary>Whether the NADH laser (output port 2) is on, as the board last reported it; setting it switches the laser.</summary>
		public bool NadhLaserIsOn
		{
			get => IsOutputOn(2);
			set
			{
				if (value != IsOutputOn(2))
					_processEngine.QueueCommand(new SwitchLaser(dna: false, on: value, _device));
			}
		}
		/// <summary>Whether the DNA laser (output port 3) is on, as the board last reported it; setting it switches the laser.</summary>
		public bool DnaLaserIsOn
		{
			get => IsOutputOn(3);
			set
			{
				if (value != IsOutputOn(3))
					_processEngine.QueueCommand(new SwitchLaser(dna: true, on: value, _device));
			}
		}
		/// <summary>True while any command is queued or executing (checked every second); settings such as the COM port are locked while it is.</summary>
		public bool IsCommandQueueBusy => _isCommandQueueBusy;
		/// <summary>True when the settings now differ, in a way that matters to config.g or the homing files, from the config last sent to the board.</summary>
		public bool ConfigNeedsGenerating => _configNeedsGenerating;
		/// <summary>The version of the running software (major.minor.build).</summary>
		public string SoftwareVersion { get; } = typeof(UtilitiesViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown";
		/// <summary>The outcome of the last check for a new software version, or the progress of a check under way.</summary>
		public string UpdateStatus
		{
			get => _updateStatus;
			private set
			{
				_updateStatus = value;
				RaisePropertyChanged(nameof(UpdateStatus));
			}
		}
		/// <summary>True when an assay is running; settings the device control uses are locked while it is.</summary>
		public bool IsAssayRunning { get; init; }
		public Well CurrentWell
		{
			get
			{
				if (_currentWell == null && _device is not null)
					_currentWell = _device.Configuration.DeviceSpecification.Plates.FirstOrDefault()?.Wells.FirstOrDefault();
				return _currentWell;
			}
			set
			{
				_currentWell = value;
				RaisePropertyChanged(nameof(CurrentWell));
			}
		}
		public int EmitFluidVolume
		{
			get => _emitFluidVolume;
			set
			{
				if (_emitFluidVolume == value)
				{
					return;
				}

				_emitFluidVolume = value;
				RaisePropertyChanged(nameof(EmitFluidVolume));
			}
		}
		public string Password
		{
			get
			{
				return _settingsService.Settings.EffaniPassword;
			}
			set
			{
				if (_settingsService.Settings.EffaniPassword == value)
					return;
				_settingsService.Settings.EffaniPassword = value;
				RaisePropertyChanged(nameof(Password));
				RaisePropertyChanged(nameof(UsernameAndPasswordComplete));
			}
		}
		public string SendCommandText
		{
			get => _sendCommandText;
			set
			{
				if (_sendCommandText == value)
				{
					return;
				}

				_sendCommandText = value;
				RaisePropertyChanged(nameof(SendCommandText));
			}
		}
		/// <summary>How long Send waits for the board when <see cref="ReportSendResponse"/> is true; starts as the general board timeout.</summary>
		public int SendTimeoutSeconds
		{
			get => _sendTimeoutSeconds;
			set
			{
				if (_sendTimeoutSeconds == value)
					return;
				_sendTimeoutSeconds = value;
				RaisePropertyChanged(nameof(SendTimeoutSeconds));
			}
		}
		/// <summary>When true, Send waits for the board to finish the command and reports what it replied in the console messages.</summary>
		public bool ReportSendResponse
		{
			get => _reportSendResponse;
			set
			{
				if (_reportSendResponse == value)
					return;
				_reportSendResponse = value;
				RaisePropertyChanged(nameof(ReportSendResponse));
			}
		}
		public ObservableCollection<string> SettingsServiceActivity => _settingsService.Activity;
		public string Username
		{
			get
			{
				return _settingsService.Settings.EffaniUsername;
			}
			set
			{
				if (_settingsService.Settings.EffaniUsername == value)
					return;
				_settingsService.Settings.EffaniUsername = value;
				RaisePropertyChanged(nameof(Username));
				RaisePropertyChanged(nameof(UsernameAndPasswordComplete));
			}
		}
		public async void ActivateSettingsVersion(SettingsVersion settingsVersion)
		{
			if (settingsVersion == null)
				return;

			_settingsService.ActivateSettingsVersion(settingsVersion.Id);
			AttachSettings();
			UseActiveSettings();

			RaisePropertyChanged(nameof(Settings));
			RaisePropertyChanged(nameof(ActiveSettingsVersion));
		}
		[Command]
		public async void DeleteSettings(object parameter)
		{
			if(parameter is SettingsVersion settingsVersion)
			{
				SettingsVersions.Remove(settingsVersion);
				_settingsService.DeleteSettings(settingsVersion.Id);
			}
		}
		[Command]
		public async void SaveActiveSettingsAsNewVersion()
		{
			if(DialogService.ShowDialog(MessageBoxButton.OKCancel, "Save Settings As...", this) == MessageBoxResult.Cancel)
				return;

			_settingsService.SaveAcitveSettingsAsNew(NewSettingsDescription);
			AttachSettings();
			UseActiveSettings();
			NewSettingsDescription = null;

			RaisePropertyChanged(nameof(Settings));
			RaisePropertyChanged(nameof(ActiveSettingsVersion));
        }
        [Command]
        public async void ResetActiveSettings()
        {
            if (DevExpress.Xpf.Core.DXMessageBox.Show("Reset all settings to their default values? Any changes to the active settings will be lost.",
                    "Reset Settings", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;

            _settingsService.ResetSettings();
            AttachSettings();
            UseActiveSettings();

            RaisePropertyChanged(nameof(Settings));
            RaisePropertyChanged(nameof(ActiveSettingsVersion));
        }

        public string NewSettingsDescription { get; set; }
		public ObservableCollection<SettingsVersion> SelectedSettingsVersions { get; set; } = new();
		public ObservableCollection<SettingsVersion> SettingsVersions => _settingsService.SettingsVersions;
		public Settings Settings => _settingsService.Settings;
		public SettingsVersion ActiveSettingsVersion => _settingsService.ActiveSettingsVersion;

		public bool UsernameAndPasswordComplete => !String.IsNullOrEmpty(Username) && !String.IsNullOrEmpty(Password);
		public ObservableCollection<AxisSpec> Axes { get; set; }
		public ObservableCollection<SensorSpec> Sensors { get; set; }
		protected IDialogService DialogService { get { return GetService<IDialogService>(); } }
		#endregion

		#region Fields
		DeviceConfiguration _config => _device.Configuration;
		Well _currentWell;
		readonly EffaniDeviceControl _device;
		int _emitFluidVolume;
		readonly ProcessEngine _processEngine;
		string _sendCommandText;
		readonly System.Windows.Threading.DispatcherTimer _executingTimer;
		bool _reportSendResponse;
		bool _isCommandQueueBusy;
		int _sendTimeoutSeconds;
		string _updateStatus = "";
		bool _checkingForUpdates;
		readonly UpdateChecker _updateChecker = new();
		readonly ISettingsService _settingsService;
		Settings? _attachedSettings;
		bool _configNeedsGenerating;
		static string? s_sentConfigSignature;
		string? _pendingConfigSignature;
		#endregion
	}
}
