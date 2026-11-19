using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Effani.Device.Services
{
	/// <summary>The outcome of <see cref="UpdateChecker.CheckAsync"/>.</summary>
	/// <param name="Succeeded">False when the check could not be completed (see <paramref name="Message"/>).</param>
	/// <param name="IsUpdateAvailable">True when the published version is newer than the running one.</param>
	/// <param name="LatestVersion">The newest version published in the update manifest, if it could be read.</param>
	/// <param name="IsCritical">True when the newest version is flagged as a critical update.</param>
	/// <param name="Message">A sentence describing the outcome, suitable for showing to the user.</param>
	/// <param name="DownloadUrl">Where the newest version's installer is published, if the manifest says; its release notes are found from it.</param>
	/// <param name="Sha256">The SHA-256 checksum the manifest gives for that installer; a download is checked against it before it is run.</param>
	/// <param name="Size">The installer's size in bytes, if the manifest says.</param>
	/// <param name="FileName">The installer's file name, if the manifest says.</param>
	public record UpdateCheckResult(bool Succeeded, bool IsUpdateAvailable, Version? LatestVersion, bool IsCritical, string Message, string? DownloadUrl = null,
		string? Sha256 = null, long? Size = null, string? FileName = null);

	/// <summary>
	/// Checks for new software versions published with Advanced Installer. The installer puts the Advanced Installer updater
	/// (<c>Effani Updater.exe</c>) and its configuration (<c>Effani Updater.ini</c>) next to the program. The configuration holds the
	/// address of the update manifest (the <c>EffaniUpdates.txt</c> built by the updates project), which this class downloads and
	/// compares with the running version. An update is installed by downloading the installer the manifest names, checking it
	/// against the manifest's checksum and running it (<see cref="DownloadAsync"/>); the updater program itself is not used, since
	/// it would choose a version from the manifest by its own rules.
	/// </summary>
	public class UpdateChecker
	{
		/// <summary>The updater's configuration file, which an installed copy of the program has beside it: it holds the manifest's address.</summary>
		public static string UpdaterIniPath => Path.Combine(AppContext.BaseDirectory, "Effani Updater.ini");

		/// <summary>Downloads the update manifest and reports whether it publishes a version newer than <paramref name="current"/>.</summary>
		public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellationToken = default)
		{
			try
			{
				var manifestUrl = ReadManifestUrl(UpdaterIniPath);
				if (manifestUrl == null)
					return Failed("Update checking is only available in an installed copy of the program.");

				string manifest;
				using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					timeout.CancelAfter(TimeSpan.FromSeconds(20));
					manifest = await _http.GetStringAsync(manifestUrl, timeout.Token);
				}

				var updates = ParseManifest(manifest);
				if (updates.Count == 0)
					return Failed("The update information could not be read.");

				// Only versions with the running version's major number are for this machine: a different major number is
				// software for different hardware, and is never offered here.
				var mine = updates.Where(u => u.Version.Major == current.Major).ToList();
				if (mine.Count == 0)
					return new UpdateCheckResult(true, false, null, false, $"No version {current.Major} updates are published (this is {Format(current)}).");

				var latest = mine.OrderByDescending(u => u.Version).First();
				var available = Normalize(latest.Version) > Normalize(current);
				var message = available
					? $"Version {Format(latest.Version)} is available (this is {Format(current)})."
					: $"This is the latest version ({Format(current)}).";
				return new UpdateCheckResult(true, available, latest.Version, latest.IsCritical, message, latest.Url, latest.Sha256, latest.Size, latest.FileName);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return Failed("Timed out contacting the update server.");
			}
			catch (HttpRequestException ex)
			{
				return Failed($"Could not contact the update server: {ex.Message}");
			}
			catch (Exception ex)
			{
				return Failed($"Could not check for updates: {ex.Message}");
			}
		}
		/// <summary>
		/// The release notes of the version a check found, as the Markdown text of its GitHub release (the version's section of
		/// CHANGELOG.md, put there when the release was drafted). Null when they cannot be had: the installer is not published
		/// as a GitHub release, or GitHub could not be reached.
		/// </summary>
		public async Task<string?> GetReleaseNotesAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
		{
			var address = ReleaseAddress(update.DownloadUrl);
			if (address == null)
				return null;
			try
			{
				using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(TimeSpan.FromSeconds(20));
				using var request = new HttpRequestMessage(HttpMethod.Get, address);
				request.Headers.UserAgent.ParseAdd("Effani-Device"); // GitHub's API refuses a request without one
				request.Headers.Accept.ParseAdd("application/vnd.github+json");
				using var response = await _http.SendAsync(request, timeout.Token);
				if (!response.IsSuccessStatusCode)
					return null;
				using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
				return document.RootElement.TryGetProperty("body", out var body) && body.ValueKind == System.Text.Json.JsonValueKind.String
					&& !String.IsNullOrWhiteSpace(body.GetString()) ? body.GetString() : null;
			}
			catch (Exception)
			{
				return null;
			}
		}
		/// <summary>
		/// The address GitHub gives a release's details at, worked out from where its installer is downloaded:
		/// https://github.com/{owner}/{repository}/releases/download/{tag}/{file} is release {tag} of that repository.
		/// </summary>
		internal static string? ReleaseAddress(string? downloadUrl)
		{
			if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
				return null;
			var parts = uri.AbsolutePath.Trim('/').Split('/');
			if (parts.Length < 6 || parts[2] != "releases" || parts[3] != "download")
				return null;
			return $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/tags/{parts[4]}";
		}
		/// <summary>
		/// Downloads the installer of the version a check found into <see cref="DownloadFolder"/> and returns its path. The file
		/// is only returned once its SHA-256 checksum matches the one in the manifest: anything else is deleted and reported as
		/// an error, so a damaged or substituted download is never run. A complete copy left by an earlier download is reused.
		/// </summary>
		/// <param name="progress">Told the bytes received so far and the total expected, when that is known.</param>
		public async Task<string> DownloadAsync(UpdateCheckResult update, IProgress<(long Received, long? Total)>? progress = null, CancellationToken cancellationToken = default)
		{
			if (!Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps && !address.IsLoopback)
				throw new InvalidOperationException("The update list does not say where to download this version from.");
			if (String.IsNullOrWhiteSpace(update.Sha256))
				throw new InvalidOperationException("The update list gives no checksum for this version, so its download could not be checked.");

			var name = Path.GetFileName(String.IsNullOrWhiteSpace(update.FileName) ? address.LocalPath : update.FileName);
			if (String.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
				name = $"Effani-Installer-{update.LatestVersion}.exe";
			Directory.CreateDirectory(DownloadFolder);
			var path = Path.Combine(DownloadFolder, name);
			if (File.Exists(path) && await HasChecksumAsync(path, update.Sha256, cancellationToken))
				return path;

			var partial = path + ".part";
			try
			{
				using (var response = await _http.GetAsync(ToDirectLink(address.ToString()), HttpCompletionOption.ResponseHeadersRead, cancellationToken))
				{
					response.EnsureSuccessStatusCode();
					var total = response.Content.Headers.ContentLength ?? update.Size;
					using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
					using var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
					var buffer = new byte[1 << 16];
					long received = 0;
					int read;
					while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
					{
						await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
						received += read;
						progress?.Report((received, total));
					}
				}
				if (!await HasChecksumAsync(partial, update.Sha256, cancellationToken))
					throw new InvalidDataException("The downloaded installer does not match the checksum in the update list, so it was not run.");
				File.Move(partial, path, overwrite: true);
				return path;
			}
			finally
			{
				if (File.Exists(partial))
					File.Delete(partial);
			}
		}
		/// <summary>Where downloaded installers are kept: the Updates folder beside the program's log and settings.</summary>
		public static string DownloadFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Effani", "Updates");
		private static async Task<bool> HasChecksumAsync(string path, string sha256, CancellationToken cancellationToken)
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
			var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
			return Convert.ToHexString(hash).Equals(sha256.Trim(), StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Reads the manifest address (the URL key of the General section) from the updater's configuration file.</summary>
		internal static string? ReadManifestUrl(string iniPath)
		{
			if (!File.Exists(iniPath))
				return null;

			var inGeneral = false;
			foreach (var raw in File.ReadLines(iniPath))
			{
				var line = raw.Trim();
				if (line.StartsWith('['))
				{
					inGeneral = line.Equals("[General]", StringComparison.OrdinalIgnoreCase);
					continue;
				}
				if (!inGeneral)
					continue;
				var separator = line.IndexOf('=');
				if (separator > 0 && line[..separator].Trim().Equals("URL", StringComparison.OrdinalIgnoreCase))
					return ToDirectLink(line[(separator + 1)..].Trim());
			}
			return null;
		}
		/// <summary>A Dropbox share link ending in dl=0 opens a web page; dl=1 downloads the file itself.</summary>
		internal static string ToDirectLink(string url)
		{
			if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.EndsWith("dropbox.com", StringComparison.OrdinalIgnoreCase))
				return url.Replace("dl=0", "dl=1", StringComparison.OrdinalIgnoreCase);
			return url;
		}
		/// <summary>Parses an Advanced Installer update manifest (an ini file with one section per published version).</summary>
		internal static List<PublishedUpdate> ParseManifest(string manifest)
		{
			var ret = new List<PublishedUpdate>();
			Dictionary<string, string>? section = null;
			var sections = new List<Dictionary<string, string>>();
			foreach (var raw in manifest.Split('\n'))
			{
				var line = raw.Trim().TrimStart('﻿');
				if (line.Length == 0 || line.StartsWith(';'))
					continue;
				if (line.StartsWith('['))
				{
					// every section is taken: the updates project names each after its version ("[4.1.7]"), other tools "[Update]".
					// One without a version, such as a "[General]" section, is dropped below.
					section = new(StringComparer.OrdinalIgnoreCase);
					sections.Add(section);
					continue;
				}
				var separator = line.IndexOf('=');
				if (section != null && separator > 0)
					section[line[..separator].Trim()] = line[(separator + 1)..].Trim();
			}

			foreach (var s in sections)
			{
				if (!(s.TryGetValue("Version", out var text) || s.TryGetValue("ProductVersion", out text)) || !Version.TryParse(text, out var version))
					continue;
				var critical = s.TryGetValue("Flags", out var flags) && flags.Split('|').Any(f => f.Trim().Equals("Critical", StringComparison.OrdinalIgnoreCase));
				ret.Add(new PublishedUpdate(version, critical, s.GetValueOrDefault("URL"), s.GetValueOrDefault("SHA256"),
					long.TryParse(s.GetValueOrDefault("Size"), out var size) ? size : null, s.GetValueOrDefault("ServerFileName")));
			}
			return ret;
		}
		internal record PublishedUpdate(Version Version, bool IsCritical, string? Url = null, string? Sha256 = null, long? Size = null, string? FileName = null);

		/// <summary>Gives a version all four parts so that 4.0.1 and 4.0.1.0 compare as equal.</summary>
		private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
		private static string Format(Version v) => Normalize(v).Revision == 0 ? Normalize(v).ToString(3) : Normalize(v).ToString();
		private static UpdateCheckResult Failed(string message) => new(false, false, null, false, message);

		private static readonly HttpClient _http = new();
	}
}
