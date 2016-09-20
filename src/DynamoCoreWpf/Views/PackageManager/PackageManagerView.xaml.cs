using CefSharp;
using Dynamo.Wpf.Utilities;
using Dynamo.PackageManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Dynamo.PackageManager.UI
{
    /// <summary>
    /// Interaction logic for PackageManagerView.xaml
    /// </summary>
    public partial class PackageManagerView : Window
    {
        private readonly PackageManagerViewModel viewModel;
        
        public PackageManagerView(PackageManagerViewModel viewModel)
        {
            this.viewModel = viewModel;
            this.DataContext = viewModel;

            if (!Cef.IsInitialized)
            {
                var settings = new CefSettings { RemoteDebuggingPort = 8088 };
                settings.CefCommandLineArgs.Add("disable-gpu", "1");
                Cef.Initialize(settings);
            }
            viewModel.PublishCompCefHelper.PublishSuccess += PackageViewModelOnPublishSuccess;
            InitializeComponent();
            this.cefBrowser.RegisterJsObject("cefHelper", this.viewModel.CefHelper);
            this.cefBrowser.RegisterJsObject("publishCefHelper", this.viewModel.PublishCompCefHelper);

            this.cefBrowser.DownloadHandler = this.viewModel.CefHelper;
            this.viewModel.CefHelper.CefBrowser = this.cefBrowser;

            this.Height = (System.Windows.SystemParameters.PrimaryScreenHeight * 0.95);
            this.Width = (System.Windows.SystemParameters.PrimaryScreenWidth * 0.75);
        }

        private void PackageViewModelOnPublishSuccess(PublishCefHelper sender)
        {
            this.Dispatcher.BeginInvoke((Action)(Close));
        }
    }
}
