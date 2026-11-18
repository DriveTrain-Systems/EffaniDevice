using DevExpress.Mvvm.POCO;
using DevExpress.Xpf.Core;
using Effani.Device.DataCollection;
using Effani.Device.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Configuration;
using System.IO;
using System.Windows;

namespace Effani.Device.UI
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		static App()
		{
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Effani", "Log.txt"),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}", flushToDiskInterval: TimeSpan.FromMilliseconds(500))
                .CreateLogger();

            AppHost = Host.CreateDefaultBuilder()
				.ConfigureServices((hostContext, services) =>
				{
					services.AddSingleton<ISettingsService, SettingsService>();
					services.AddSingleton<IDeviceService, DeviceService>();
					services.AddSingleton<IAssayManagerService, AssayManagerService>();
					var connectionString = $"data source={Path.Combine(SettingsService.DataFolder, "AssayData.effani")}";
					services.AddDbContext<EfContext>(options =>
						options.UseSqlite("connectionString",
						x => x.MigrationsAssembly("Effani.Device.Services")));
					services.AddSingleton<MainWindow>();
					services.AddSingleton<UtilitiesView>();
				})
				.Build();

			using (var scope = AppHost.Services.CreateScope())
			{
				var db = scope.ServiceProvider.GetRequiredService<EfContext>();
				db.Database.Migrate();
			}
		}
		protected override async void OnStartup(StartupEventArgs e)
		{
			await AppHost!.StopAsync();

			// typing on the touchscreen: a touched field opens the on-screen keyboard and a copy of the field above it
			OnScreenInput.Register();

#if DEBUG
			// A debug build asks before it brings saved settings up to date for its version (a release build does it without
			// asking): the settings may belong to the installed program, which a build under development should not alter
			// unasked. This is a plain message box because no window of the program exists yet.
			SettingsService.ConfirmMigration = question =>
				MessageBox.Show(question, "Migrate Settings", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
#endif
			// started with --emulator, the program runs against an emulated controller board: nothing is sent to a COM port. This is
			// decided before the device is first used, which creating the main window does.
			DeviceService.UseEmulator = Array.Exists(e.Args, a => String.Equals(a, "--emulator", StringComparison.OrdinalIgnoreCase));
			var startupForm = AppHost.Services.GetRequiredService<MainWindow>();
			startupForm.Show();
			if (DeviceService.UseEmulator)
				ShowEmulator(startupForm);

			base.OnStartup(e);

			DevExpress.Xpf.Core.ApplicationThemeHelper.UpdateApplicationThemeName();
		}

		/// <summary>
		/// Shows the emulated board's window in front of the main window, as a reminder that no board is in use. In a method of
		/// its own so that the emulator's assembly is only loaded when the emulator is used.
		/// </summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void ShowEmulator(Window owner)
		{
			if ((AppHost!.Services.GetRequiredService<IDeviceService>() as DeviceService)?.Emulator is Drivetrain.Robotics.Emulator.BoardEmulator emulator)
				new EmulatorView(emulator) { Owner = owner }.Show();
		}
		protected override async void OnExit(ExitEventArgs e)
		{
			await AppHost!.StopAsync();
            Log.CloseAndFlush();

            base.OnExit(e);
		}

		public static IHost AppHost { get; private set; }
	}
}
