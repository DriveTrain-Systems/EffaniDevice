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
		/// <summary>
		/// How long is left before this command gives up on the controller board, or null when it is not waiting for the board. Only
		/// meaningful for the command being executed. Timeout information is reported here rather than in <see cref="Description"/>, so
		/// that descriptions (which are logged) never carry it.
		/// </summary>
		public TimeSpan? TimeoutRemaining => Device?.ReadyTimeRemaining;
		/// <summary><see cref="TimeoutRemaining"/> as text for display beside the command being executed, such as "12 s left"; null when it is not waiting for the board.</summary>
		public string? TimeoutCountdown
		{
			get
			{
				var remaining = TimeoutRemaining;
				return remaining is null ? null : $"{Math.Ceiling(remaining.Value.TotalSeconds):0} s left";
			}
		}
		public string Result { get; set; }
	}
}
