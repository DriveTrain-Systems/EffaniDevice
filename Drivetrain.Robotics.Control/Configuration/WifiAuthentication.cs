namespace Drivetrain.Robotics.Control
{
	/// <summary>How the controller board authenticates to the WiFi network. The values are the M587 X parameter.</summary>
	public enum WifiAuthentication
	{
		/// <summary>Network password only (WPA2-PSK).</summary>
		Personal = 0,
		/// <summary>WPA2-Enterprise with a username and password (EAP-PEAP-MSCHAPv2).</summary>
		EnterprisePeap = 2,
		/// <summary>WPA2-Enterprise with a username and password (EAP-TTLS-MSCHAPv2).</summary>
		EnterpriseTtls = 3
	}
}
