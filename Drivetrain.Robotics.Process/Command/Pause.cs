using Drivetrain.Robotics.Control;
using System;
using System.Linq;

namespace Drivetrain.Robotics.Process
{
    /// <param name="timeout">How long to wait for the board; null allows the pause itself plus the board ready timeout from the settings.</param>
    public class Pause(int milliseconds, DeviceControl device, TimeSpan? timeout = null)
        : Command(device, timeout ?? (device is null ? null : TimeSpan.FromMilliseconds(milliseconds) + device.ReadyTimeout))
    {
        protected override void Run()
        {
            Device.Pause(Milliseconds);
        }

        public override string Description => $"Pause for {Milliseconds} milliseconds.";

        public int Milliseconds { get; } = milliseconds;
    }
}
