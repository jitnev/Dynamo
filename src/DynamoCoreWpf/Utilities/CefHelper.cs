using CefSharp.Wpf;
using Dynamo.PackageManager;
using Dynamo.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Dynamo.Wpf.Utilities
{
    internal class CefHelper
    {
        internal readonly DynamoViewModel dynamoViewModel;
        internal PackageLoader Model { get; private set; }

        public ChromiumWebBrowser CefBrowser { get; set; }

        public string SessionData { get { return JsonConvert.SerializeObject(dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.GetSession()); } }

        public Window ParentWindow { get; set; }

        public CefHelper(DynamoViewModel dynamoViewModel, PackageLoader model)
        {
            this.dynamoViewModel = dynamoViewModel;
            this.Model = model;
        }

        public string InstalledPackages
        {
            get { return JsonConvert.SerializeObject(Model.LocalPackages.ToList()); }
        }

        public bool Login()
        {
            return dynamoViewModel.Model.AuthenticationManager.AuthProvider.Login();
        }
    }
}
