using Drivetrain.Robotics.Control;
using Flurl;
using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Effani.Device.Services
{
	public class SettingsService : ISettingsService
	{
		#region Constructors
		public SettingsService(EfContext efContext)
		{
			_efContext = efContext;
			ActiveSettingsVersion = _efContext.SettingsVersions.FirstOrDefault(x => x.IsActive);
			if (ActiveSettingsVersion == null)
			{
				ActiveSettingsVersion = _efContext.SettingsVersions.Add(new SettingsVersion() { Description = "Default Settings", IsActive = true }).Entity;
				SettingsMigrator.MarkCurrent(ActiveSettingsVersion.DeviceSettings); // made by this version of the program: nothing to migrate
				_efContext.SaveChanges();
			}
			SettingsVersions = new(_efContext.SettingsVersions.ToList());
			MigrateSettings();
			ActiveSettingsVersion.DeviceSettings.PropertyChanged += DeviceSettings_PropertyChanged;
		}
		#endregion

		#region Public methods
		public void ActivateSettingsVersion(string id)
		{
			ActivateSettingsVersion(_efContext.SettingsVersions.Find(id));
		}
		public void SaveAcitveSettingsAsNew(string description)
		{
			var newSettings = _efContext.SettingsVersions.Add(new SettingsVersion() { Description = description }).Entity;
			newSettings.DeviceSettings = ActiveSettingsVersion.DeviceSettings;
			SettingsVersions.Add(newSettings);
			_efContext.SaveChanges();
			ActivateSettingsVersion(newSettings);
        }
        public void ResetSettings()
        {
			var resetSettings = new Settings();
			SettingsMigrator.MarkCurrent(resetSettings);

            ActiveSettingsVersion.DeviceSettings.PropertyChanged -= DeviceSettings_PropertyChanged;
			ActiveSettingsVersion.DeviceSettings = resetSettings;
            resetSettings.PropertyChanged += DeviceSettings_PropertyChanged;
            _efContext.Entry(ActiveSettingsVersion).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
            _efContext.SaveChanges();
        }
        public void DeleteSettings(string id)
		{
			var settings = _efContext.SettingsVersions.Find(id);
			if(settings != null)
			{
				_efContext.SettingsVersions.Remove(settings);
				SettingsVersions.Remove(settings);
			}
			_efContext.SaveChanges();
		}
		public async Task UploadSettings()
		{
			Activity.Clear();

			if (String.IsNullOrWhiteSpace(Settings.EffaniUsername) ||
				String.IsNullOrWhiteSpace(Settings.EffaniPassword))
			{
				Activity.Add($"Missing Effani username or password.");
				return;
			}

			var toUpload = _efContext.SettingsVersions;

			foreach (var a in toUpload)
			{
				Activity.Add($"Uploading settings from {a.CreatedOn}.");
				try
				{
					var result = await Settings.EffaniApiUrl
						.AppendPathSegment("Document")
						.AppendPathSegment("Upload")
						.AppendQueryParam("documentTypeName", "EffaniSettingsVersion")
						.AppendQueryParam("ownerId", Settings.EffaniUsername)
						.WithBasicAuth(Settings.EffaniUsername, Settings.EffaniPassword)
						.PostJsonAsync(JsonSerializer.Serialize(ActiveSettingsVersion), cancellationToken: CancellationToken.None);
				}
				catch (FlurlHttpException ex)
				{
					Activity.Add($"* Failed: {ex.Call.Exception}");
					continue;
				}
			}
		}
		#endregion

		#region Private methods
		private void ActivateSettingsVersion(SettingsVersion nextSettings)
		{
			if (nextSettings == null)
				throw new ArgumentNullException(nameof(nextSettings));

			ActiveSettingsVersion.DeviceSettings.PropertyChanged -= DeviceSettings_PropertyChanged;
			ActiveSettingsVersion.IsActive = false;
			nextSettings.IsActive = true;
			nextSettings.DeviceSettings.PropertyChanged += DeviceSettings_PropertyChanged;
			ActiveSettingsVersion = nextSettings;
			_efContext.SaveChanges();
		}
		/// <summary>
		/// Brings every saved settings version up to date with the running version of the program (see
		/// <see cref="SettingsMigrator"/>) and saves the ones that changed. Called once, when the settings are loaded. A version
		/// that cannot be migrated is left as it was saved, and the reason logged, so that one bad version does not stop the
		/// program from starting.
		/// </summary>
		private void MigrateSettings()
		{
			var current = SettingsMigrator.CurrentVersion;
			var pending = SettingsVersions
				.Select(v => (Version: v, Changes: SettingsMigrator.Pending(v.DeviceSettings, current)))
				.Where(p => p.Changes is not null)
				.ToList();
			if (pending.Count == 0)
				return;
			if (ConfirmMigration is not null
				&& !ConfirmMigration(MigrationQuestion(pending.Select(p => (p.Version.Description, p.Version.DeviceSettings.SoftwareVersion, p.Changes!)), current)))
			{
				Serilog.Log.Information("Saved settings were not brought up to version {To}: declined when asked", SettingsMigrator.Format(current));
				return;
			}
			var changed = false;
			foreach (var version in SettingsVersions)
			{
				var from = version.DeviceSettings.SoftwareVersion;
				var saved = JsonSerializer.Serialize(version.DeviceSettings, s_settingsJson); // to go back to if a migration fails part way
				try
				{
					if (!SettingsMigrator.Migrate(version.DeviceSettings, current, out var applied))
						continue;
					_efContext.Entry(version).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
					changed = true;
					Serilog.Log.Information("Settings \"{Description}\" brought from version {From} to {To}: {Applied}", version.Description,
						from ?? "(none recorded)", SettingsMigrator.Format(current), applied.Count == 0 ? "no changes were needed" : String.Join("; ", applied));
				}
				catch (Exception ex)
				{
					Serilog.Log.Error(ex, "Settings \"{Description}\" could not be brought up to version {To}", version.Description, SettingsMigrator.Format(current));
					version.DeviceSettings = JsonSerializer.Deserialize<Settings>(saved, s_settingsJson)!;
				}
			}
			if (changed)
				_efContext.SaveChanges();
		}
		/// <summary>The question put to <see cref="ConfirmMigration"/>: which saved settings would be changed, and how.</summary>
		public static string MigrationQuestion(IEnumerable<(string? Description, string? From, IReadOnlyList<string> Changes)> pending, Version current)
		{
			var lines = pending.Select(p =>
			{
				var from = p.From is null ? "no version recorded" : $"from version {p.From}";
				var what = p.Changes.Count == 0
					? "nothing to change, only marked with the new version"
					: String.Join("; ", p.Changes);
				return $"\u2022 \"{p.Description}\" ({from}): {what}";
			}).ToList();
			return $"Saved settings are from an older version of the program than this one ({SettingsMigrator.Format(current)}).\n\n"
				+ String.Join("\n", lines)
				+ "\n\nBring them up to date now?\n\nNo leaves them exactly as they are saved; you will be asked again the next time the program starts.";
		}
		private void DeviceSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			_efContext.Entry(ActiveSettingsVersion).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
			_efContext.SaveChanges();
		}
		#endregion

		#region Properties
		public ObservableCollection<string> Activity { get; init; } = new();
		/// <summary>
		/// When set, saved settings from an older version of the program are only brought up to date if this answers true to the
		/// question it is given (which says what would be changed); when not set they are brought up to date without asking. The
		/// program sets it in a debug build, so that a build under development does not alter the saved settings unasked. Set it
		/// before the settings service is first used: the settings are migrated when it is created.
		/// </summary>
		public static Func<string, bool>? ConfirmMigration { get; set; }
		public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Effani");
		public Settings Settings => ActiveSettingsVersion.DeviceSettings;
		public ObservableCollection<SettingsVersion> SettingsVersions { get; init; }
		#endregion

		#region Fields
		public SettingsVersion ActiveSettingsVersion { get; private set; }
		readonly EfContext _efContext;
		// the settings as the database stores them (see EfContext)
		static readonly JsonSerializerOptions s_settingsJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
		#endregion
	}

	public interface ISettingsService
	{
		void DeleteSettings(string id);
		#region Public methods
		void ActivateSettingsVersion(string id);
		void SaveAcitveSettingsAsNew(string description);
        void ResetSettings();
        Task UploadSettings();
		#endregion

		#region Properties
		ObservableCollection<string> Activity { get; }
		static string DataFolder { get; }
		Settings Settings { get; }
		SettingsVersion ActiveSettingsVersion { get; }
		ObservableCollection<SettingsVersion> SettingsVersions { get; }
		#endregion
	}
}
