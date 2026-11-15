namespace Drivetrain.Robotics.Control
{
	public static class WifiCommand
	{
		/// <summary>
		/// Builds the M587 command that adds a WiFi network to the board's remembered list. Enterprise authentication needs RRF 3.5.1
		/// or later with WiFi module firmware 2.1.0 or later (experimental), e.g. <c>M587 X2 S"ssid" U"user" P"password"</c>.
		/// </summary>
		public static string BuildM587(WifiAuthentication authentication, string? ssid, string? username, string? password)
		{
			var enterprise = authentication != WifiAuthentication.Personal;
			var command = enterprise ? $"M587 X{(int)authentication} S\"{Escape(ssid)}\"" : $"M587 S\"{Escape(ssid)}\"";
			if (enterprise)
				command += $" U\"{Escape(username)}\"";
			return command + $" P\"{Escape(password)}\"";
		}

		/// <summary>
		/// Escapes text for use inside a G-code string literal: a double quote is written as two double quotes, and so is a single
		/// quote, because a single quote makes the firmware lowercase the next character.
		/// </summary>
		public static string Escape(string? text) => (text ?? "").Replace("\"", "\"\"").Replace("'", "''");
	}
}
