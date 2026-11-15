using Drivetrain.Robotics.Control;

namespace Drivetrain.Robotics.Process
{
	public class SetWifi(WifiAuthentication authentication, string ssid, string username, string password, DeviceControl device, TimeSpan? timeout = null) : Command(device, timeout ?? SettingTimeout(device, s => s.WifiTimeoutSeconds))
	{
		protected override void Run()
		{
			Device.SetWifi(_authentication, _ssid, _username, _password);
		}

		// The password is deliberately left out of the description, which is displayed in the UI.
		public override string Description => $"Set WiFi network to {_ssid}.";
		private readonly WifiAuthentication _authentication = authentication;
		private readonly string _ssid = ssid;
		private readonly string _username = username;
		private readonly string _password = password;
	}
}
