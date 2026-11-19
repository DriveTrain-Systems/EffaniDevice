using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using Effani.Device.Services;
using Serilog;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Effani.Device.UI
{
	/// <summary>
	/// Looks for a newer version of the program and holds what was found, for the main window and Tools to show. There is one
	/// for the whole program (<see cref="Current"/>): a check is made each time the program opens and each time Tools opens,
	/// and both windows show the same notice.
	/// </summary>
	public sealed class UpdateWatch : BindableBase
	{
		public static UpdateWatch Current { get; } = new();

		/// <summary>Says whether an assay is running; an update is not installed while one is.</summary>
		public Func<bool>? IsAssayRunning { get; set; }

		/// <summary>True when the last check found a version newer than the running one.</summary>
		public bool IsUpdateAvailable => _found != null;
		/// <summary>
		/// True when the main window should show its notice: an update was found and has not been ignored. Ignoring lasts until
		/// the program is closed, or a still newer version is found; Tools goes on showing its notice, so the update can still be
		/// installed.
		/// </summary>
		public bool IsBannerVisible => _found != null && _found.LatestVersion != _ignored;
		/// <summary>The notice, such as "Version 4.1.8 is available.", or empty when there is no update.</summary>
		public string Message => _found?.LatestVersion is Version version ? $"Version {Format(version)} is available." : "";
		/// <summary>The version of the running program (major.minor.build).</summary>
		public static string RunningVersion => Format(RunningAssemblyVersion);

		/// <summary>
		/// Looks for a newer version. Nothing is shown when there is none or the check fails (a copy that was not installed has no
		/// updater to check with, and the device may have no network): the outcome is written to the log.
		/// </summary>
		public async Task CheckAsync()
		{
			if (_checking)
				return;
			_checking = true;
			try
			{
				var result = await _checker.CheckAsync(RunningAssemblyVersion);
				Log.Information("Update check: {Outcome}", result.Message);
				if (result.Succeeded)
					Apply(result.IsUpdateAvailable ? result : null);
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "The check for updates failed");
			}
			finally
			{
				_checking = false;
			}
		}
		/// <summary>
		/// Shows the release notes of the version found, with the choice of installing it or ignoring it. Installing downloads
		/// the installer, checks it, starts it and closes this program.
		/// </summary>
		public void ShowWhatsNew(Window? owner)
		{
			if (_found?.LatestVersion is not Version version)
				return;
			if (_notes == null || _notesVersion != version)
			{
				_notes = _checker.GetReleaseNotesAsync(_found);
				_notesVersion = version;
			}
			var found = _found;
			var dialog = new WhatsNewView(Format(version), RunningVersion, _notes, IsAssayRunning?.Invoke() == true,
				(progress, cancel) => _checker.DownloadAsync(found, progress, cancel)) { Owner = owner };
			dialog.ShowDialog();
			switch (dialog.Choice)
			{
				case WhatsNewChoice.Install when dialog.InstallerPath != null:
					try
					{
						Log.Information("Installing version {Version} from {Installer}", Format(version), dialog.InstallerPath);
						Process.Start(new ProcessStartInfo(dialog.InstallerPath) { UseShellExecute = true });
					}
					catch (Exception ex)
					{
						Log.Error(ex, "Could not start the installer");
						DXMessageBox.Show($"Could not start the installer: {ex.Message}", "Software Update", MessageBoxButton.OK, MessageBoxImage.Error);
						break;
					}
					CloseProgram();
					break;
				case WhatsNewChoice.Ignore:
					_ignored = version;
					RaisePropertyChanged(nameof(IsBannerVisible));
					break;
			}
		}
		/// <summary>Closes every window, the main window last, so that the installer finds the program closed and can replace it.</summary>
		private static void CloseProgram()
		{
			var main = Application.Current?.MainWindow;
			foreach (var window in Application.Current?.Windows.Cast<Window>().Where(w => w != main).ToList() ?? new())
				window.Close();
			if (main is MainWindow mainWindow)
				mainWindow.CloseForUpdate();
			else
				main?.Close();
		}

		private void Apply(UpdateCheckResult? found)
		{
			_found = found;
			RaisePropertyChanged(nameof(IsUpdateAvailable));
			RaisePropertyChanged(nameof(IsBannerVisible));
			RaisePropertyChanged(nameof(Message));
		}
		private static Version RunningAssemblyVersion => typeof(UpdateWatch).Assembly.GetName().Version ?? new Version(0, 0);
		private static string Format(Version v) => v.Revision > 0 ? v.ToString() : v.ToString(Math.Min(3, v.Build < 0 ? 2 : 3));

		private UpdateWatch() { }

		readonly UpdateChecker _checker = new();
		UpdateCheckResult? _found;
		Version? _ignored;
		Task<string?>? _notes;
		Version? _notesVersion;
		bool _checking;
	}
}
