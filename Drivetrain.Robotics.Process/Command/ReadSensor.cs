using Drivetrain.Robotics.Control;
using System;
using System.Linq;

namespace Drivetrain.Robotics.Process
{
	public class ReadSensor(DeviceControl device, Action<ReadSensor> resultCallback, TimeSpan? timeout = null) : Command(device, timeout)
	{
		protected override void Run()
		{
			// the pause before the reading is waited for too, so it is added to the timeout unless one was passed
			using (device.OverrideReadyTimeout(Timeout ?? TimeSpan.FromMilliseconds(PauseTime) + device.ReadyTimeout))
				Result = device.ReadSensor(SensorDesignator, PauseTime);
			_resultCallback?.Invoke(this);
		}

		public int PauseTime { get; set; }
		public int SensorDesignator { get; set; }
		public override string Description => $"Read sensor {SensorDesignator}";

		private Action<ReadSensor> _resultCallback = resultCallback;
	}

}
