using DevExpress.Xpf.Core;
using Effani.Device.DataCollection;
using Effani.Device.Services;
using System;
using System.Linq;
using System.Windows.Controls;

namespace Effani.Device.UI
{
	public partial class UploadView : ThemedWindow
	{
		public UploadView(IAssayManagerService assayManagerService, ISettingsService settingsService)
		{
			InitializeComponent();
			ControlBoxButtonSet = DevExpress.Xpf.Core.ControlBoxButtons.None; // no title bar buttons: the Back button closes the window
			DataContext = new UploadViewModel(assayManagerService, settingsService);
			this.Height = (System.Windows.SystemParameters.PrimaryScreenHeight * 0.90);
			this.Width = (System.Windows.SystemParameters.PrimaryScreenWidth * 0.90);
		}
		private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
	}
}
