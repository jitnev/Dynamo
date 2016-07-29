
using Microsoft.Practices.Prism.ViewModel;
using Dynamo.ViewModels;
using Dynamo.Wpf.Utilities;
using System.Configuration;

namespace Dynamo.PackageManager
{
    public class PackageManagerViewModel : NotificationObject
    {
        //public ChromiumWebBrowser CefBrowser { get; set; }

        public string Address { get; set; }

        internal PackageManagerCefHelper CefHelper { get; set; }

        internal PublishCefHelper PublishCompCefHelper { get; set; }

        public PackageManagerViewModel(DynamoViewModel dynamoViewModel, PackageLoader model, string address)
        {
            CefHelper = new PackageManagerCefHelper(dynamoViewModel, model);
            PublishCompCefHelper = new PublishCefHelper(dynamoViewModel, model);

            var path = this.GetType().Assembly.Location;
            var config = ConfigurationManager.OpenExeConfiguration(path);
            this.Address = config.AppSettings.Settings["packageManagerWebAddress"].Value + "/#/" + address;
        }
    }
}
