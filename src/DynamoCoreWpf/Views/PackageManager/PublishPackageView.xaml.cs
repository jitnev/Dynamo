using System;
using System.Windows;

using Dynamo.Controls;
using Dynamo.PackageManager.UI;
using Dynamo.Search.SearchElements;
using Dynamo.UI;
using Dynamo.Utilities;
using Dynamo.ViewModels;
using Dynamo.Wpf;
using CefSharp;
using System.Collections.Generic;
using System.IO;
using CefSharp.Wpf;
using Dynamo.Wpf.Utilities;

namespace Dynamo.PackageManager
{
    /// <summary>
    /// Interaction logic for PublishPackageView.xaml
    /// </summary>
    public partial class PublishPackageView : Window
    {
        public PublishPackageView(PublishPackageViewModel packageViewModel)
        {
            this.DataContext = packageViewModel;
            packageViewModel.CefHelper.PublishSuccess += PackageViewModelOnPublishSuccess;
            if (!Cef.IsInitialized)
            {
                var settings = new CefSettings { RemoteDebuggingPort = 8088 };
                Cef.Initialize(settings);
            }
            //InitializeComponent();
            //this.browser = new ChromiumWebBrowser();
            //this.browser.RegisterJsObject("cefHelper", packageViewModel.CefHelper, true);
           
            //packageViewModel.CefHelper.CefBrowser = browser;
            
            this.Height = (System.Windows.SystemParameters.PrimaryScreenHeight * 0.95);
            this.Width = (System.Windows.SystemParameters.PrimaryScreenWidth * 0.90);
        }

        private void PackageViewModelOnPublishSuccess(PublishCefHelper sender)
        {
            this.Dispatcher.BeginInvoke((Action) (Close));
        }

        private void OnRequestShowFolderBrowserDialog(object sender, PackagePathEventArgs e)
        {
            e.Cancel = true;

            var dialog = new DynamoFolderBrowserDialog
            {
                SelectedPath = e.Path,
                Owner = this
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                e.Cancel = false;
                e.Path = dialog.SelectedPath;
            }
        }
    }

}
