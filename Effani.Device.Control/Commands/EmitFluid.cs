using Drivetrain.Robotics.Control;
using System;
using System.Linq;
using Drivetrain.Robotics.Process;

namespace Effani.Device.Control
{
	public class EmitFluid(Axis axis, int microLiters, EffaniDeviceControl device, TimeSpan? timeout = null)
		: Command(device, timeout ?? DefaultTimeout(device))
	{
		// the longer of the setting and the dwell after the move (each wait for the board gets the whole timeout)
		private static TimeSpan? DefaultTimeout(EffaniDeviceControl device)
		{
			var setting = SettingTimeout(device, s => s.FluidEmissionTimeoutSeconds);
			var dwell = TimeSpan.FromMilliseconds(device.Settings.FluidEmissionDwell) + device.ReadyTimeout;
			return setting > dwell ? setting : dwell;
		}
		protected override void Run()
		{
			var device = Device as EffaniDeviceControl;
			if (axisSpec is null)
				throw new ApplicationException($"Unknown axis {axis} in {nameof(EmitFluid)}");

			if (device.PrimingStopwatches.TryGetValue(Axis, out var stopwatch))
			{
				stopwatch.Reset();
				stopwatch.Start();
			}

			device.EmitFluid(axisSpec, MicroLiters);
		}

		public override string Description => $"Emit {microLiters}μl from {axisSpec.Label}.";

		public Axis Axis { get; } = axis;
		public int MicroLiters { get; } = microLiters;
		private FluidAxis axisSpec
		{
			get
			{
				var device = Device as EffaniDeviceControl;
				device.Axes.TryGetValue(axis, out var axisSpec);
				return axisSpec as FluidAxis;
			}
		}
    }
}
