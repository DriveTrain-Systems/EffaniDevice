using Drivetrain.Robotics.Control;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Effani.Device.Control
{
	public class EffaniDeviceControl : DeviceControl
	{
		public EffaniDeviceControl(Func<Settings> settingsProvider)
			: base (settingsProvider)
		{
        }
		/// <summary>
		/// Picks up the current settings: the configuration, axis specifications and priming timers are rebuilt from them the next
		/// time they are needed, and the serial port is chosen again. Call only while no assay is running.
		/// </summary>
		public void RefreshSettings()
		{
			ResetConfiguration();
			SelectPort(Settings);
		}
		protected override void OnConfigurationReset()
		{
			lock (_derivedLock)
			{
				_axes = null;
				_primingStopwatches = null;
			}
		}
        public override void Dispose()
        {
            lock (_derivedLock)
            {
                if (_primingStopwatches != null)
                    foreach (var t in _primingStopwatches.Values)
                        t.Reset();
            }
            base.Dispose();
        }
		public void MoveAbsolute(Axis axis, decimal location, decimal? speed = null)
		{
			if(!Axes.TryGetValue(axis, out var axisSpec))
				throw new ApplicationException($"Unknown axis {axis} in {nameof(MoveAbsolute)}");

			MoveAbsolute(axisSpec, location, speed);
		}

		public void MoveRelative(Axis axis, decimal distance, decimal? speed = null)
		{
			if (!Axes.TryGetValue(axis, out var axisSpec))
				throw new ApplicationException($"Unknown axis {axis} in {nameof(MoveRelative)}");

			MoveRelative(axisSpec, distance, speed);
		}
		public void TableGoTo(Coordinate location, decimal? speed = null)
		{
			TableGoTo(location.X, location.Y, speed);
		}
		public void TableGoTo(decimal x, decimal y, decimal? speed = null)
		{
			var xaxis = Axes[Axis.TableX];
			var yaxis = Axes[Axis.TableY];

			// a move of both axes has a single speed: asking for the faster of the two move speeds lets a move that is mostly along
			// one axis run at that axis's speed instead of being held to the slower one (the board still holds each axis to its own
			// max speed, M203)
			if (!speed.HasValue)
				speed = Math.Max(MoveSpeedFor(xaxis), MoveSpeedFor(yaxis));

			if (x < xaxis.Minimum || x > xaxis.Maximum || y < yaxis.Minimum || y > yaxis.Maximum)
				return;

			MoveAbsolute([ new MoveSpec(xaxis, x), new MoveSpec(yaxis, y)], speed.Value);
		}
		public void Home(Axis axis)
		{
			if (!Axes.TryGetValue(axis, out var axisSpec))
				throw new ApplicationException($"Unknown axis {axis} in {nameof(MoveRelative)}");

			Home(axisSpec);
		}
		public string ReadDna()
		{
			return ReadSensor(0, Settings.SensorReadDwell);
		}
		public string ReadNadh()
		{
			return ReadSensor(1, Settings.SensorReadDwell);
		}
		/// <summary>
		/// Where an output is wired, for messages: its board and connector from the settings, e.g. "board 0, out4" or "board 1, out6".
		/// </summary>
		public string OutputLocation(int board, int port) => $"board {board}, out{port}";
		public string TrayUvLocation => OutputLocation(Settings.TrayUvBoardNumber, Settings.TrayUvPortNumber);
		public string ChamberUvLocation => OutputLocation(Settings.ChamberUvBoardNumber, Settings.ChamberUvPortNumber);
		public string DnaLaserLocation => LaserLocation(Settings.DnaLaserPortKind, Settings.DnaLaserBoardNumber, Settings.DnaLaserPortNumber);
		public string NadhLaserLocation => LaserLocation(Settings.NadhLaserPortKind, Settings.NadhLaserBoardNumber, Settings.NadhLaserPortNumber);
		private string LaserLocation(PortKind kind, int board, int port) => kind switch
		{
			PortKind.Laser => $"board {board}, laser",
			PortKind.IoOutput => $"board {board}, io{port}.out",
			_ => OutputLocation(board, port)
		};
		public string? LaserDnaOn()
		{
			return AccOn(3);
		}
		public string? LaserDnaOff()
		{
			return AccOff(3);
		}
		public string? LaserNadhOn()
		{
			return AccOn(2);
		}
		public string? LaserNadhOff()
		{
			return AccOff(2);
		}
		public void EmitFluid(AxisSpec axisSpec, int microLiters)
		{
			if(!(axisSpec is FluidAxis))
				return;

			// emitting moves the actuator in the positive direction, away from its low-end endstop; the travel per microlitre is a
			// distance, so its sign does not matter
			var distance = Math.Abs(CalcFluidTravelMmForMl(microLiters, axisSpec));
			MoveRelative(axisSpec.Axis, distance, Settings.FluidSyringeSpeed);
			Pause(Settings.FluidEmissionDwell);
		}
		/// <summary>
		/// Aspirates: the needle goes down from its start position to the plate (start + distance to tray), on down by the travel
		/// for the volume while stirring, then back up to the start. Down is the positive direction: 0 is the top of the travel.
		/// </summary>
		public void Aspirate(int microLiters)
		{
			MoveAbsolute(Axis.Aspirate, (Settings.AspirateActuatorStartPosition + Settings.AspirateDistanceToPlate), Settings.AspiratePositioningSpeed);
			var distance = CalcAspirateTravelMmForMl(microLiters);
			DoStirringDescent(Axes[Axis.Aspirate], distance);
			MoveAbsolute(Axis.Aspirate, Settings.AspirateActuatorStartPosition);
		}
		//public void AspirationPressureOn()
		//{
		//	AccOn(4);
		//}
		//public void AspirationPressureOff()
		//{
		//	AccOff(4);
		//}
		//public void AspirateFluid(int microLiters)
		//{
		//	// lower fluid aspirator needle to top of the plate
		//	MoveAbsolute(Axis.AspirateFluid, (Configuration.AspirateFluidActuatorStartPosition + Configuration.AspirateFluidDistanceToPlate), Configuration.AspiratePositioningSpeed);
		//	// lower aspirator needle calculated distance per ml to extract
		//	var distance = CalcAspirateTravelMmForMl(microLiters);
		//	MoveRelative(Axis.AspirateFluid, distance, Configuration.AspirateStirSpeed);
		//	// turn suction on
		//	AspirationSuctionOn();
		//	// wait for extraction to complete
		//	Pause(Configuration.AspirationDwell);
		//	// in case the sample got in the way of removing as much liquid as planned, raise with suction off and lower with suction on again
		//	AspirationSuctionOff();
		//	MoveRelative(Axis.AspirateFluid, Configuration.AspirationPressureAscension * -1, Configuration.AspirateStirSpeed);
		//	MoveRelative(Axis.AspirateFluid, Configuration.AspirationPressureAscension, Configuration.AspirateStirSpeed);
		//	AspirationSuctionOn();
		//	Pause(Configuration.AspirationDwell);
		//	// turn suction off
		//	AspirationSuctionOff();
		//	// raise aspiration needle to sample ejection position
		//	MoveRelative(Axis.AspirateFluid, Configuration.AspirationPressureAscension * -1);
		//	// turn pressure on
		//	AspirationPressureOn();
		//	// wait for sample to be ejected
		//	Pause(Configuration.AspirationPressureDwell);
		//	// turn pressure off
		//	AspirationPressureOff();
		//	// raise aspiration needle to its starting location
		//	MoveAbsolute(Axis.AspirateFluid, Configuration.AspirateFluidActuatorStartPosition);
		//}

		private decimal CalcFluidTravelMmForMl(int microLiters, AxisSpec axisSpec)
		{
			var microLitersPerMm = axisSpec.Axis switch
			{
				Axis.Basal => Configuration.Settings.BasalTravelPerMicroLiter,
				Axis.Nadh => Configuration.Settings.NadhTravelPerMicroLiter,
				Axis.Koh => Configuration.Settings.KohTravelPerMicroLiter,
				Axis.DnaReagent => Configuration.Settings.DnaTravelPerMicroLiter,
				_ => throw new ApplicationException($"Unknown axis {axisSpec.Axis} in {nameof(CalcFluidTravelMmForMl)}")
			};
			var ret = microLitersPerMm * microLiters;
			return ret;
		}
		private decimal CalcAspirateTravelMmForMl(int microLiters)
		{
			var ret = (Configuration.Settings.AspirateTravelPerMicroLiter * microLiters);
			return ret;
		}
		private void DoStirringDescent(AxisSpec axis, decimal distance)
		{
			MoveRelative(axis, Math.Abs(distance), Configuration.Settings.AspirateStirSpeed); // down is the positive direction: 0 is the top of the travel
			Pause(Configuration.Settings.AspirationDwell);
			//var repetitions = 1;
			//var descentFactor = distance / (repetitions * 4);
			//var proximityFactor = (Configuration.WellWidth / 2) * 0.9f;
			//MoveRelative(Axis.TableY, proximityFactor, Configuration.AspirateStirSpeed); // move near well edge
			//while(repetitions > 0)
			//{
			//	//arc to right edge and down
			//	var rightMultiAxis = new List<MoveSpec>();
			//	rightMultiAxis.Add(new MoveSpec(Axes[Axis.TableX], proximityFactor));
			//	rightMultiAxis.Add(new MoveSpec(Axes[Axis.TableY], proximityFactor * -1));
			//	rightMultiAxis.Add(new MoveSpec(axis, descentFactor));
			//	MoveRelativeClockwiseArc(rightMultiAxis, Configuration.AspirateStirSpeed);
			//	//arc to bottom edge and down
			//	var bottomMultiAxis = new List<MoveSpec>();
			//	bottomMultiAxis.Add(new MoveSpec(Axes[Axis.TableX], proximityFactor * -1));
			//	bottomMultiAxis.Add(new MoveSpec(Axes[Axis.TableY], proximityFactor * -1));
			//	bottomMultiAxis.Add(new MoveSpec(axis, descentFactor));
			//	MoveRelativeClockwiseArc(bottomMultiAxis, Configuration.AspirateStirSpeed);
			//	//arc to left edge and down
			//	var leftMultiAxis = new List<MoveSpec>();
			//	leftMultiAxis.Add(new MoveSpec(Axes[Axis.TableX], proximityFactor * -1));
			//	leftMultiAxis.Add(new MoveSpec(Axes[Axis.TableY], proximityFactor));
			//	leftMultiAxis.Add(new MoveSpec(axis, descentFactor));
			//	MoveRelativeClockwiseArc(leftMultiAxis, Configuration.AspirateStirSpeed);
			//	//arc to top edge and down
			//	var topMultiAxis = new List<MoveSpec>();
			//	topMultiAxis.Add(new MoveSpec(Axes[Axis.TableX], proximityFactor));
			//	topMultiAxis.Add(new MoveSpec(Axes[Axis.TableY], proximityFactor));
			//	topMultiAxis.Add(new MoveSpec(axis, descentFactor));
			//	MoveRelativeClockwiseArc(topMultiAxis, Configuration.AspirateStirSpeed);
			//	repetitions--;
			//}
			////arc back
			//var backMultiAxis = new List<MoveSpec>();
			//backMultiAxis.Add(new MoveSpec(Axes[Axis.TableX], proximityFactor * -1));
			//backMultiAxis.Add(new MoveSpec(Axes[Axis.TableY], proximityFactor * -1));
			//MoveRelativeCounterClockwiseArc(backMultiAxis, Configuration.AspirateStirSpeed);
		}

		/// <summary>The axis specifications of the current configuration (built on first use).</summary>
		public Dictionary<Axis, AxisSpec> Axes
		{
			get
			{
				lock (_derivedLock)
					return _axes ??= Configuration.DeviceSpecification.Axes.ToDictionary(k => k.Axis, v => v);
			}
		}
		/// <summary>One stopwatch per axis, timing since the axis was last primed; restarted whenever the configuration is rebuilt.</summary>
        public Dictionary<Axis, Stopwatch> PrimingStopwatches
		{
			get
			{
				lock (_derivedLock)
					return _primingStopwatches ??= Configuration.DeviceSpecification.Axes.ToDictionary(k => k.Axis, v => new Stopwatch());
			}
		}
		private readonly object _derivedLock = new();
		private Dictionary<Axis, AxisSpec>? _axes;
		private Dictionary<Axis, Stopwatch>? _primingStopwatches;
        public List<AxisSpec> FluidAxes => Axes.Values.Where(x => x is FluidAxis).ToList();
	}
}
