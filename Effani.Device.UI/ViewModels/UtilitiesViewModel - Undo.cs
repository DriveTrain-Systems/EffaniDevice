using DevExpress.Xpf.Core;
using Drivetrain.Robotics.Control;
using Effani.Device.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;

namespace Effani.Device.UI
{
	// Undoing settings changes. The settings are saved as they are edited, so there is nothing to cancel when Tools is closed;
	// instead the settings are remembered as they were when Tools opened, and Undo puts them back.
	public partial class UtilitiesViewModel
	{
		/// <summary>Remembers the active settings as they are now; called once, when Tools opens.</summary>
		private void RememberSettings()
		{
			_openedVersionId = _settingsService.ActiveSettingsVersion?.Id;
			_openedSettingsJson = JsonSerializer.Serialize(_settingsService.Settings, s_settingsJson);
			_openedSettings = JsonSerializer.Deserialize<Settings>(_openedSettingsJson, s_settingsJson);
			UpdateHasSettingsChanges();
		}
		/// <summary>True when a setting differs from what it was when Tools opened, or other settings have been made the active ones since.</summary>
		public bool HasSettingsChanges
		{
			get => _hasSettingsChanges;
			private set
			{
				if (_hasSettingsChanges == value)
					return;
				_hasSettingsChanges = value;
				RaisePropertyChanged(nameof(HasSettingsChanges));
			}
		}
		private void UpdateHasSettingsChanges()
		{
			if (_openedSettings is null)
				return;
			HasSettingsChanges = ActiveVersionChanged || ChangedSettings().Count > 0;
		}
		private bool ActiveVersionChanged => _settingsService.ActiveSettingsVersion?.Id != _openedVersionId;
		/// <summary>The settings whose value now differs from the one remembered when Tools opened.</summary>
		private List<PropertyInfo> ChangedSettings()
		{
			var now = _settingsService.Settings;
			return s_settingsProperties.Where(p => !SameValue(p.GetValue(now), p.GetValue(_openedSettings))).ToList();
		}
		private static bool SameValue(object? a, object? b) =>
			a is Coordinate ca && b is Coordinate cb ? ca.X == cb.X && ca.Y == cb.Y : Equals(a, b);
		/// <summary>
		/// Puts every setting back as it was when Tools opened, after asking; true when it was done (Tools then closes). If the
		/// board's config was generated from changed settings in the meantime, offers to generate and send it again from the
		/// restored ones.
		/// </summary>
		public bool UndoSettingsChanges()
		{
			if (_openedSettings is null || !HasSettingsChanges)
				return false;
			const string caption = "Undo Settings Changes";
			var versionChanged = ActiveVersionChanged;
			var opened = versionChanged ? _settingsService.SettingsVersions.FirstOrDefault(v => v.Id == _openedVersionId) : null;
			if (versionChanged && opened is null)
			{
				DXMessageBox.Show("The settings that were active when Tools was opened have since been deleted, so they cannot be put back.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
				return false;
			}

			string question;
			if (versionChanged)
				question = $"Undo the settings changes made since Tools was opened?\n\n\"{opened!.Description}\" will be made the active settings again, with every setting as it was then. Settings saved under a new name in the meantime are kept.";
			else
			{
				var changed = ChangedSettings().Select(p => SettingName(p.Name)).ToList();
				var shown = changed.Take(12).ToList();
				if (changed.Count > shown.Count)
					shown.Add($"and {changed.Count - shown.Count} more");
				question = $"Undo the settings changes made since Tools was opened?\n\n{(changed.Count == 1 ? "This setting goes" : $"These {changed.Count} settings go")} back to the value {(changed.Count == 1 ? "it" : "they")} had then:\n\n{String.Join("\n", shown.Select(n => "   " + n))}";
			}
			question += "\n\nTools then closes.";
			if (DXMessageBox.Show(question, caption, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
				return false;

			// the board has a config generated in this session: is it one the restored settings would not produce?
			var resend = false;
			if (_configGeneratedSinceOpened && !IsAssayRunning)
			{
				string restoredSignature;
				try { restoredSignature = DeviceConfiguration.GetConfigSignature(_openedSettings); }
				catch { restoredSignature = ""; }
				if (restoredSignature != _pendingConfigSignature)
					resend = DXMessageBox.Show(
						"The controller board's config was generated from the changed settings while Tools was open.\n\n" +
						"Generate it again from the restored settings and send it to the board? The board is then rebooted.\n\n" +
						"Choose No to leave the board as it is; Generate Config will then show that the settings and the board differ.",
						caption, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes;
			}

			if (versionChanged)
			{
				_settingsService.ActivateSettingsVersion(_openedVersionId!);
				AttachSettings();
			}
			RestoreSettings();
			RaisePropertyChanged(nameof(Settings));
			RaisePropertyChanged(nameof(ActiveSettingsVersion));
			UpdateHasSettingsChanges();

			var now = DateTime.Now.ToString("H:mm:ss");
			ConsoleMessages.Insert(0, $"{now} Settings put back as they were when Tools was opened.");
			if (resend)
			{
				var (location, generated, sent) = GenerateAndSendConfig();
				if (!generated)
					DXMessageBox.Show($"The settings were restored, but the config could not be generated from them:\n\n{location}", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
				else if (sent)
					ConsoleMessages.Insert(0, $"{now} Config generated from the restored settings; sending it to the controller board, which will then be rebooted.");
			}
			return true;
		}
		/// <summary>
		/// Copies the remembered values onto the active settings, one setting at a time, so that everything holding the settings
		/// object (and its coordinates) keeps a valid one and each change is announced and saved in the usual way.
		/// </summary>
		private void RestoreSettings()
		{
			var now = _settingsService.Settings;
			// a fresh copy, so no object is shared between the remembered settings and the active ones
			var then = JsonSerializer.Deserialize<Settings>(_openedSettingsJson!, s_settingsJson)!;
			foreach (var property in s_settingsProperties)
			{
				var value = property.GetValue(then);
				if (SameValue(property.GetValue(now), value))
					continue;
				if (value is Coordinate coordinate && property.GetValue(now) is not null
					&& typeof(Settings).GetProperty(property.Name + "X") is { CanWrite: true } x && typeof(Settings).GetProperty(property.Name + "Y") is { CanWrite: true } y)
				{
					// through the X and Y settings, which change the coordinate in place: the axes and sensors hold that very object
					x.SetValue(now, coordinate.X);
					y.SetValue(now, coordinate.Y);
				}
				else
					property.SetValue(now, value);
			}
		}

		/// <summary>A setting's name as words, for a message: "TableXTravelLength" becomes "Table X Travel Length", "DnaVolume" "DNA Volume".</summary>
		private static string SettingName(string property)
		{
			var words = Regex.Replace(property, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ").Split(' ');
			return String.Join(" ", words.Select(w => w switch
			{
				"Dna" => "DNA", "Koh" => "KOH", "Nadh" => "NADH", "Uv" => "UV", "Wifi" => "WiFi", "Ssid" => "SSID", "Api" => "API", "Url" => "URL", "Com" => "COM",
				_ => w
			}));
		}
		// the settings as the database stores them: every setting with a value of its own (the X and Y of a location are not among them)
		private static readonly JsonSerializerOptions s_settingsJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
		private static readonly PropertyInfo[] s_settingsProperties = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<JsonIgnoreAttribute>() is null).ToArray();
		private string? _openedVersionId;
		private string? _openedSettingsJson;
		private Settings? _openedSettings;
		private bool _hasSettingsChanges;
		/// <summary>True once Generate Config has sent the board a config in this session of Tools.</summary>
		private bool _configGeneratedSinceOpened;
	}
}
