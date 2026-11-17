using Drivetrain.Robotics.Control;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Effani.Device.Services
{
	/// <summary>
	/// One change to make to settings saved by an older version of the program.
	/// </summary>
	/// <param name="To">The version of the program that needs the change: it is made to settings older than this version.</param>
	/// <param name="Description">What the change does, for the log.</param>
	/// <param name="Apply">Makes the change.</param>
	public record SettingsMigration(Version To, string Description, Action<Settings> Apply);

	/// <summary>
	/// Brings saved settings up to date when a newer version of the program is installed. Every settings object carries the
	/// version of the program it was created with, or last brought up to date by (<see cref="Settings.SoftwareVersion"/>). When
	/// the settings are loaded, each migration for a version later than that, up to the running version, is applied in order,
	/// and the settings are then marked with the running version, so no migration is ever applied twice.
	/// </summary>
	/// <remarks>
	/// To add a migration, add an entry to <see cref="Migrations"/> with the version that introduces the change, e.g.
	/// <code>
	/// new(new Version(4, 2, 0), "Tray offset is measured between tray centres", s => s.PlateOffsetX += s.WellWidth),
	/// </code>
	/// and release the program with that version (or a later one). A setting that is only new needs no migration: saved
	/// settings get its default value. Migrations are for when the meaning, unit or name of an existing setting changes.
	/// </remarks>
	public static class SettingsMigrator
	{
		/// <summary>Every migration there is; the order here does not matter, they are applied in order of version.</summary>
		public static IReadOnlyList<SettingsMigration> Migrations { get; } = new SettingsMigration[]
		{
			// none yet
		};

		/// <summary>The version of the running program (three parts, like the version shown in Tools).</summary>
		public static Version CurrentVersion
		{
			get
			{
				var version = (Assembly.GetEntryAssembly() ?? typeof(SettingsMigrator).Assembly).GetName().Version ?? new Version(0, 0, 0);
				return new Version(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));
			}
		}
		/// <summary>The text <see cref="Settings.SoftwareVersion"/> holds for a version.</summary>
		public static string Format(Version version) => version.ToString(3);

		/// <summary>Marks settings the program has just created with its own version: they are up to date and need no migration.</summary>
		public static void MarkCurrent(Settings settings) => settings.SoftwareVersion = Format(CurrentVersion);

		/// <summary>
		/// Applies to <paramref name="settings"/> every migration between the version they carry and <paramref name="current"/>,
		/// then marks them with <paramref name="current"/>. Returns true when the settings were changed (so they need saving);
		/// <paramref name="applied"/> lists what was done. Settings that carry no version were saved before versions were
		/// recorded, so every migration applies to them. Settings from a newer version of the program than the one running are
		/// left exactly as they are.
		/// </summary>
		public static bool Migrate(Settings settings, Version current, out IReadOnlyList<string> applied, IEnumerable<SettingsMigration>? migrations = null)
		{
			var done = new List<string>();
			applied = done;
			var due = Due(settings, current, migrations);
			if (due is null)
				return false;
			foreach (var migration in due)
			{
				migration.Apply(settings);
				done.Add(Describe(migration));
			}
			settings.SoftwareVersion = Format(current);
			return true;
		}
		/// <summary>
		/// What <see cref="Migrate"/> would do to <paramref name="settings"/>, without doing it: null when it would leave them as
		/// they are, otherwise the migrations it would apply (none when the settings would only be marked with the version).
		/// </summary>
		public static IReadOnlyList<string>? Pending(Settings settings, Version current, IEnumerable<SettingsMigration>? migrations = null)
			=> Due(settings, current, migrations)?.Select(Describe).ToList();

		private static string Describe(SettingsMigration migration) => $"{Format(migration.To)}: {migration.Description}";
		/// <summary>The migrations the settings still need, in order; null when the settings need neither a migration nor a new mark.</summary>
		private static List<SettingsMigration>? Due(Settings settings, Version current, IEnumerable<SettingsMigration>? migrations)
		{
			var carried = Version.TryParse(settings.SoftwareVersion, out var parsed) ? parsed : null;
			if (carried is not null && carried > current)
				return null;
			var due = (migrations ?? Migrations).Where(m => m.To <= current && (carried is null || m.To > carried)).OrderBy(m => m.To).ToList();
			return due.Count == 0 && settings.SoftwareVersion == Format(current) ? null : due;
		}
	}
}
