using Drivetrain.Robotics.Emulator;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Effani.Device.UI
{
	/// <summary>
	/// Shown while the program runs against the board emulator (started with --emulator): what the emulated board holds (where
	/// each axis is and whether it is homed, which outputs are on, what the sensors read) and the G-code going to and from it.
	/// It is also the standing reminder that no controller board is being used.
	/// </summary>
	public sealed class EmulatorView : Window
	{
		public EmulatorView(BoardEmulator emulator)
		{
			_emulator = emulator;
			Title = "Board Emulator - no controller board is being used";
			Width = 560;
			Height = 640;
			ShowInTaskbar = false;
			WindowStartupLocation = WindowStartupLocation.Manual;
			Left = 20;
			Top = 120;
			Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1));

			var speed = new ComboBox { Width = 150, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
			foreach (var (label, scale) in new[] { ("Real time", 1.0), ("5 times faster", 5.0), ("20 times faster", 20.0), ("100 times faster", 100.0) })
				speed.Items.Add(new ComboBoxItem { Content = label, Tag = scale });
			speed.SelectedIndex = 0;
			speed.SelectionChanged += (s, e) => _emulator.TimeScale = (double)((ComboBoxItem)speed.SelectedItem).Tag;
			speed.ToolTip = "How fast the emulated board works. Moves, dwells and restarts take this much less time; the program's own timeouts do not change.";
			var queries = new CheckBox { Content = "Show status queries", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
				ToolTip = "Also list the position, output and sensor queries (M114, M409) and the markers the program uses to find the end of each reply." };
			queries.Checked += (s, e) => _showQueries = true;
			queries.Unchecked += (s, e) => _showQueries = false;
			var clear = new Button { Content = "Clear", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(16, 0, 0, 0) };
			clear.Click += (s, e) => _log.Items.Clear();
			var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 4) };
			bar.Children.Add(new TextBlock { Text = "Speed", VerticalAlignment = VerticalAlignment.Center });
			bar.Children.Add(speed);
			bar.Children.Add(queries);
			bar.Children.Add(clear);

			_state = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13, Margin = new Thickness(10, 4, 10, 8) };
			_log = new ListBox { FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(10, 0, 10, 10) };

			var root = new DockPanel();
			DockPanel.SetDock(bar, Dock.Top);
			DockPanel.SetDock(_state, Dock.Top);
			root.Children.Add(bar);
			root.Children.Add(_state);
			root.Children.Add(_log);
			Content = root;

			_emulator.Traffic += Emulator_Traffic;
			_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
			_timer.Tick += (s, e) => Refresh();
			_timer.Start();
			Closed += (s, e) =>
			{
				_timer.Stop();
				_emulator.Traffic -= Emulator_Traffic;
			};
			Refresh();
		}

		private void Emulator_Traffic(string line, bool fromProgram) => _traffic.Enqueue((DateTime.Now, line, fromProgram));
		private static bool IsQuery(string line) =>
			line.StartsWith("M409", StringComparison.Ordinal) || line.StartsWith("M114", StringComparison.Ordinal) || line.StartsWith("M400", StringComparison.Ordinal)
			|| line.StartsWith("{\"key\"", StringComparison.Ordinal) || line.StartsWith("X:", StringComparison.Ordinal)
			|| line.StartsWith("echo \"effani-", StringComparison.Ordinal) || line.StartsWith("effani-", StringComparison.Ordinal);
		private void Refresh()
		{
			var snapshot = _emulator.Snapshot();
			var text = new StringBuilder();
			text.AppendLine(snapshot.IsHalted ? "STOPPED (M112): waiting for a restart (M999)"
				: !snapshot.IsPresent ? "RESTARTING"
				: $"Up {TimeSpan.FromSeconds(snapshot.UpTime):h\\:mm\\:ss}   {(snapshot.Activity == "idle" ? "idle" : "running " + snapshot.Activity)}");
			text.AppendLine();
			text.AppendLine("Axis  board says  really at  homed  travel");
			foreach (var axis in snapshot.Axes)
				text.AppendLine($"{axis.Letter,-4}  {axis.Position,10:0.000}  {axis.Physical,9:0.000}  {(axis.Homed ? "yes" : "no"),-5}  {axis.Minimum:0.###} to {axis.Maximum:0.###}");
			text.AppendLine();
			text.AppendLine("Outputs: " + (snapshot.Outputs.Count == 0 ? "none configured" : string.Join("   ", snapshot.Outputs.Select(o => $"P{o.Port} {o.Pin} {(o.On ? "ON" : "off")}"))));
			text.Append("Sensors: " + (snapshot.Sensors.Count == 0 ? "none configured" : string.Join("   ", snapshot.Sensors.Select(s => $"S{s.Number} {s.Name} about {s.Reading:0}"))));
			_state.Text = text.ToString();

			var added = false;
			while (_traffic.TryDequeue(out var entry))
			{
				if (!_showQueries && IsQuery(entry.Line))
					continue;
				_log.Items.Add(new TextBlock
				{
					Text = $"{entry.At:HH:mm:ss.f} {(entry.FromProgram ? ">" : "<")} {entry.Line}",
					Foreground = entry.FromProgram ? Brushes.Black : entry.Line.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ? Brushes.Firebrick : Brushes.SeaGreen,
				});
				added = true;
			}
			while (_log.Items.Count > 500)
				_log.Items.RemoveAt(0);
			if (added && _log.Items.Count > 0)
				_log.ScrollIntoView(_log.Items[_log.Items.Count - 1]);
		}

		private readonly BoardEmulator _emulator;
		private readonly TextBlock _state;
		private readonly ListBox _log;
		private readonly DispatcherTimer _timer;
		private readonly ConcurrentQueue<(DateTime At, string Line, bool FromProgram)> _traffic = new();
		private bool _showQueries;
	}
}
