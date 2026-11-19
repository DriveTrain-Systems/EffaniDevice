using DevExpress.Xpf.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Effani.Device.UI
{
	/// <summary>What was chosen in <see cref="WhatsNewView"/>.</summary>
	public enum WhatsNewChoice
	{
		/// <summary>The window was closed without choosing.</summary>
		None,
		Install,
		Ignore
	}

	/// <summary>
	/// Shows the release notes of a newer version that was found, and asks whether to install it or ignore it. The notes arrive
	/// after the window opens (they are fetched from the release), so the window says they are loading until they do.
	///
	/// Install Now downloads the installer while the window stays open, showing how far it has got; Ignore then reads Cancel
	/// and stops the download. The window closes with <see cref="WhatsNewChoice.Install"/> once the installer has been
	/// downloaded and checked, with its path in <see cref="InstallerPath"/>; a download that fails says why and can be retried.
	/// </summary>
	public partial class WhatsNewView : ThemedWindow
	{
		/// <param name="newVersion">The version found, e.g. "4.1.8".</param>
		/// <param name="runningVersion">The version of the running program.</param>
		/// <param name="notes">The release notes as Markdown, or null when they could not be had.</param>
		/// <param name="assayRunning">True while an assay is running: the update cannot be installed until it has finished.</param>
		/// <param name="download">Downloads and checks the installer, reporting bytes received and expected, and returns its path.</param>
		public WhatsNewView(string newVersion, string runningVersion, Task<string?> notes, bool assayRunning,
			Func<IProgress<(long Received, long? Total)>, CancellationToken, Task<string>> download)
		{
			InitializeComponent();
			_download = download;
			Heading.Text = $"Effani {newVersion} is available";
			Running.Text = $"This is version {runningVersion}.";
			InstallButton.IsEnabled = !assayRunning;
			Caution.Text = assayRunning
				? "An assay is running. The new version can be installed once the assay has finished."
				: "Installing downloads the new version, then closes this program and starts its installer.";
			ShowNotes(notes);
		}
		/// <summary>What was chosen; <see cref="WhatsNewChoice.None"/> until a button is pressed.</summary>
		public WhatsNewChoice Choice { get; private set; }
		/// <summary>The downloaded, checked installer, once <see cref="Choice"/> is <see cref="WhatsNewChoice.Install"/>.</summary>
		public string? InstallerPath { get; private set; }

		private async void ShowNotes(Task<string?> notes)
		{
			Notes.Children.Add(Paragraph("Loading the release notes..."));
			string? text;
			try
			{
				text = await notes;
			}
			catch (Exception)
			{
				text = null;
			}
			Notes.Children.Clear();
			var blocks = text == null ? new List<FrameworkElement>() : Render(text);
			if (blocks.Count == 0)
				blocks.Add(Paragraph("The release notes for this version could not be loaded. The version can still be installed."));
			foreach (var block in blocks)
				Notes.Children.Add(block);
		}
		/// <summary>
		/// Lays out release notes written as the change log is: "###" headings, "-" list items whose text may run on over
		/// indented lines, and paragraphs. Code marks and emphasis marks are dropped rather than shown.
		/// </summary>
		internal static List<FrameworkElement> Render(string markdown)
		{
			var blocks = new List<FrameworkElement>();
			string? pending = null; // the paragraph or list item being gathered
			var pendingIsItem = false;
			void Flush()
			{
				if (pending != null)
					blocks.Add(pendingIsItem ? Item(pending) : Paragraph(pending));
				pending = null;
			}
			foreach (var raw in markdown.Replace("\r", "").Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0)
				{
					Flush();
				}
				else if (line.StartsWith('#'))
				{
					Flush();
					blocks.Add(HeadingBlock(line.TrimStart('#').Trim()));
				}
				else if (line.StartsWith("- ") || line.StartsWith("* "))
				{
					Flush();
					pending = line[2..].Trim();
					pendingIsItem = true;
				}
				else if (pending != null)
				{
					pending += " " + line; // a line that carries on the paragraph or item above it
				}
				else
				{
					pending = line;
					pendingIsItem = false;
				}
			}
			Flush();
			return blocks;
		}
		private static string Plain(string text) => text.Replace("`", "").Replace("**", "");
		private static FrameworkElement HeadingBlock(string text) =>
			new TextBlock { Text = Plain(text), FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) };
		private static FrameworkElement Paragraph(string text) =>
			new TextBlock { Text = Plain(text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
		private static FrameworkElement Item(string text)
		{
			var row = new DockPanel { Margin = new Thickness(4, 5, 0, 5) };
			var bullet = new TextBlock { Text = "•", Width = 22, VerticalAlignment = VerticalAlignment.Top, Foreground = Brushes.Gray };
			DockPanel.SetDock(bullet, Dock.Left);
			row.Children.Add(bullet);
			row.Children.Add(new TextBlock { Text = Plain(text), TextWrapping = TextWrapping.Wrap });
			return row;
		}
		private async void Install_Click(object sender, RoutedEventArgs e)
		{
			if (_downloading != null)
				return;
			_downloading = new CancellationTokenSource();
			InstallButton.IsEnabled = false;
			IgnoreButton.Content = "Cancel";
			Caution.Foreground = Running.Foreground;
			Caution.Text = "Downloading the new version...";
			Progress.Value = 0;
			Progress.IsIndeterminate = true;
			Progress.Visibility = Visibility.Visible;
			// reports come from whichever thread the download is reading on: each is shown on the window's own thread
			var progress = new Reporter(p => Dispatcher.BeginInvoke(() =>
			{
				if (_downloading == null)
					return; // a report that arrives after the download was stopped
				Progress.IsIndeterminate = p.Total is not > 0;
				if (p.Total is long total && total > 0)
					Progress.Value = Math.Min(1.0, (double)p.Received / total);
				Caution.Text = p.Total is long size && size > 0
					? $"Downloading the new version: {p.Received / 1048576.0:0} of {size / 1048576.0:0} MB"
					: $"Downloading the new version: {p.Received / 1048576.0:0} MB";
			}));
			try
			{
				InstallerPath = await _download(progress, _downloading.Token);
				Choice = WhatsNewChoice.Install;
				_downloading = null;
				Close();
			}
			catch (Exception ex)
			{
				var cancelled = ex is OperationCanceledException;
				_downloading = null;
				Progress.Visibility = Visibility.Collapsed;
				InstallButton.IsEnabled = true;
				IgnoreButton.Content = "Ignore";
				Caution.Foreground = cancelled ? Running.Foreground : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
				Caution.Text = cancelled ? "The download was stopped. The new version was not installed."
					: $"The new version could not be downloaded: {ex.Message}";
			}
		}
		/// <summary>Ignore; while the installer is downloading the button reads Cancel and stops the download instead.</summary>
		private void Ignore_Click(object sender, RoutedEventArgs e)
		{
			if (_downloading != null)
			{
				_downloading.Cancel();
				return;
			}
			Choice = WhatsNewChoice.Ignore;
			Close();
		}
		protected override void OnClosed(EventArgs e)
		{
			_downloading?.Cancel(); // the window was closed while downloading
			base.OnClosed(e);
		}

		private sealed class Reporter(Action<(long Received, long? Total)> report) : IProgress<(long Received, long? Total)>
		{
			public void Report((long Received, long? Total) value) => report(value);
		}

		readonly Func<IProgress<(long Received, long? Total)>, CancellationToken, Task<string>> _download;
		CancellationTokenSource? _downloading;
	}
}
