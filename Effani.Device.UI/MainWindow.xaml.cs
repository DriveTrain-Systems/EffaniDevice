using DevExpress.Xpf.Core;
using Effani.Device.DataCollection;
using Effani.Device.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Effani.Device.UI
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : ThemedWindow
	{
		public MainWindow(IDeviceService deviceService, IAssayManagerService assayManagerService, ISettingsService settingsService)
		{
			InitializeComponent();
			var viewModel = new MainViewModel(deviceService, assayManagerService, settingsService);
			DataContext = viewModel;
			// a newer version is looked for each time the program opens; one that is found is not installed while an assay runs
			UpdateWatch.Current.IsAssayRunning = () => viewModel.Assay != null;
			Loaded += async (s, e) => await UpdateWatch.Current.CheckAsync();
            ControlBoxButtonSet = ControlBoxButtons.None;
		}
		private void WhatsNew_Click(object sender, RoutedEventArgs e) => UpdateWatch.Current.ShowWhatsNew(this);
		/// <summary>Closes the program so that an update can be installed over it, without asking first: the choice to install was the answer.</summary>
		public void CloseForUpdate()
		{
			_closingForUpdate = true;
			Close();
		}
		bool _closingForUpdate;
		protected override void OnClosing(CancelEventArgs e)
		{
			var vm = DataContext as MainViewModel;
			if (!_closingForUpdate && !vm.ConfirmClosing())
			{
				e.Cancel = true;
				return;
			}
			(DataContext as MainViewModel).Dispose();
			base.OnClosing(e);
		}
	}
}
