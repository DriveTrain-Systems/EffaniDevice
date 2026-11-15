using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Effani.Device.UI
{
	/// <summary>
	/// Shows and hides the Windows touch keyboard (TabTip), and says where on the screen it is. Windows only shows that keyboard
	/// by itself when a field is touched on a device without a keyboard, so it is asked for here.
	/// </summary>
	internal static class TouchKeyboard
	{
		/// <summary>The part of the screen the keyboard covers, in screen pixels; null while it is not showing (or cannot be found).</summary>
		public static Rect? Bounds
		{
			get
			{
				try
				{
					var pane = (IFrameworkInputPane)new FrameworkInputPane();
					try
					{
						if (pane.Location(out var area) != 0)
							return null;
						var bounds = new Rect(area.Left, area.Top, Math.Max(0, area.Right - area.Left), Math.Max(0, area.Bottom - area.Top));
						if (bounds.Width <= 0 || bounds.Height <= 0)
							return null;
						_seen = true;
						return bounds;
					}
					finally
					{
						Marshal.ReleaseComObject(pane);
					}
				}
				catch (Exception)
				{
					return null;
				}
			}
		}
		/// <summary>Shows the keyboard, unless it is showing already.</summary>
		public static void Show()
		{
			try
			{
				if (Bounds is not null)
					return;
				Toggle();
				_shownHere = true;
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "The on-screen keyboard could not be shown");
			}
		}
		/// <summary>Hides the keyboard if it is showing.</summary>
		public static void Hide()
		{
			try
			{
				// where the keyboard's place on the screen cannot be read, go by whether it was shown from here
				if (Bounds is not null || (!_seen && _shownHere))
					Toggle();
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "The on-screen keyboard could not be hidden");
			}
			finally
			{
				_shownHere = false;
			}
		}
		/// <summary>What the keyboard's two Windows interfaces answer, for troubleshooting; shows or hides nothing.</summary>
		internal static string Probe()
		{
			string location;
			try
			{
				var pane = (IFrameworkInputPane)new FrameworkInputPane();
				var result = pane.Location(out var area);
				location = $"location 0x{result:X8} ({area.Left},{area.Top})-({area.Right},{area.Bottom})";
				Marshal.ReleaseComObject(pane);
			}
			catch (Exception ex)
			{
				location = $"location failed: {ex.Message}";
			}
			string host;
			try
			{
				var tip = (ITipInvocation)new UIHostNoLaunch();
				Marshal.ReleaseComObject(tip);
				host = "keyboard host running";
			}
			catch (Exception ex)
			{
				host = $"keyboard host not running ({ex.GetType().Name}); TabTip.exe {(File.Exists(TabTipPath) ? "found" : "not found")}";
			}
			return $"{location}; {host}";
		}

		private static void Toggle()
		{
			ITipInvocation tip;
			try
			{
				tip = (ITipInvocation)new UIHostNoLaunch();
			}
			catch (COMException)
			{
				// the keyboard's host is not running yet: start it. On some versions of Windows that shows the keyboard by itself.
				if (!File.Exists(TabTipPath))
					throw;
				Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true })?.Dispose();
				tip = null!;
				for (var attempt = 0; attempt < 15 && tip is null; attempt++)
				{
					Thread.Sleep(100);
					if (Bounds is not null)
						return;
					try
					{
						tip = (ITipInvocation)new UIHostNoLaunch();
					}
					catch (COMException)
					{
					}
				}
				if (tip is null)
					throw;
			}
			try
			{
				tip.Toggle(GetDesktopWindow());
			}
			finally
			{
				Marshal.ReleaseComObject(tip);
			}
		}

		private static string TabTipPath =>
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "microsoft shared", "ink", "TabTip.exe");

		private static bool _seen;      // the keyboard's place on the screen has been read at least once, so Bounds can be trusted
		private static bool _shownHere; // the keyboard was last shown from here

		[DllImport("user32.dll")]
		private static extern IntPtr GetDesktopWindow();

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int Left, Top, Right, Bottom;
		}
		[ComImport, Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
		private class UIHostNoLaunch
		{
		}
		[ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface ITipInvocation
		{
			void Toggle(IntPtr window);
		}
		[ComImport, Guid("D5120AA3-46BA-44C5-822D-CA8092C1FC72")]
		private class FrameworkInputPane
		{
		}
		[ComImport, Guid("5752238B-24F0-495A-82F1-2FD593056796"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IFrameworkInputPane
		{
			[PreserveSig] int Advise(IntPtr window, IntPtr handler, out int cookie);
			[PreserveSig] int AdviseWithHWND(IntPtr window, IntPtr handler, out int cookie);
			[PreserveSig] int Unadvise(int cookie);
			[PreserveSig] int Location(out RECT area);
		}
	}
}
