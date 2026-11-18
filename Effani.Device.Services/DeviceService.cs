using Drivetrain.Robotics.Control;
using Drivetrain.Robotics.Process;
using Effani.Device.Control;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Effani.Device.Services
{
	public class DeviceService : IDeviceService
	{
		public DeviceService(ISettingsService settingsService)
		{
			_device = new EffaniDeviceControl(() => settingsService.Settings);
			_device.Initialize();
			if (UseEmulator)
				Emulator = ConnectEmulator(_device, settingsService);
			_processEngine = new ProcessEngine();
		}

		public EffaniDeviceControl GetDevice()
		{
			return _device;
		}
		public ProcessEngine GetProcessEngine()
		{
			return _processEngine;
		}

		/// <summary>
		/// Set before the service is created (the program does when it is started with --emulator) to run against an emulated
		/// controller board instead of a real one: nothing is sent to a COM port.
		/// </summary>
		public static bool UseEmulator { get; set; }
		/// <summary>
		/// The emulated board (a Drivetrain.Robotics.Emulator.BoardEmulator), when <see cref="UseEmulator"/> was set. It is held
		/// as an object, and made in a method of its own, so that the emulator's assembly is only loaded when the emulator is
		/// used: a copy of the program installed without that assembly still starts.
		/// </summary>
		public object? Emulator { get; }
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static object ConnectEmulator(EffaniDeviceControl device, ISettingsService settingsService) =>
			Drivetrain.Robotics.Emulator.EmulatedBoardLink.Connect(device, () => settingsService.Settings, EmulatorFolder);
		/// <summary>The folder that stands for the emulated board's SD card: it holds the config and homing files sent to it.</summary>
		public static string EmulatorFolder => System.IO.Path.Combine(SettingsService.DataFolder, "Board Emulator");

		private EffaniDeviceControl _device;
		private ProcessEngine _processEngine;
	}

	public interface IDeviceService
	{
		ProcessEngine GetProcessEngine();
		EffaniDeviceControl GetDevice();
	}
}
