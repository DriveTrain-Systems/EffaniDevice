using Drivetrain.Robotics.Control;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Drivetrain.Robotics.Process
{
	/// <param name="device">The device the command acts on.</param>
	/// <param name="timeout">
	/// How long to wait for the controller board to report that it has finished (each time the command waits for it) before giving
	/// up. Null uses the board ready timeout from the settings. Commands that run for a long time, such as homing or a long pause,
	/// pass a longer one.
	/// </param>
	public abstract class Command(DeviceControl device, TimeSpan? timeout = null)
	{
		public void Execute()
		{
			if (Device is null)
			{
				Run();
				return;
			}
			using (Device.OverrideReadyTimeout(Timeout))
				Run();
		}

		protected abstract void Run();

		/// <summary>The default timeout for a command that can take a long time: a setting (in seconds), never less than the general board timeout. Null if there is no device.</summary>
		protected static TimeSpan? SettingTimeout(DeviceControl device, Func<Settings, int> seconds)
			=> device is null ? null : device.TimeoutFor(seconds(device.Settings));
		public DeviceControl Device { get; } = device;
		/// <summary>The timeout passed with this command, or null to use the board ready timeout from the settings.</summary>
		public TimeSpan? Timeout { get; init; } = timeout;
		/// <summary>The timeout that applies to this command: the one passed with it, otherwise the one from the settings.</summary>
		public TimeSpan EffectiveTimeout => Timeout ?? Device?.ReadyTimeout ?? TimeSpan.Zero;
		public abstract string Description { get; }
		/// <summary>The description followed by how long the board is waited for before the command is given up on; used wherever a command being executed is reported.</summary>
		public string DescriptionWithTimeout
		{
			get
			{
				if (Device is null)
					return Description;
				// while the board is being waited for, count down to giving up on it
				var remaining = Device.ReadyTimeRemaining;
				return remaining is null
					? $"{Description} (board timeout {EffectiveTimeout.TotalSeconds:0} s)"
					: $"{Description} (waiting for board: {Math.Ceiling(remaining.Value.TotalSeconds):0} s left)";
			}
		}
		public string Result { get; set; }
	}
}
