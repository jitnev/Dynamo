using CefSharp.Wpf;
using Dynamo.PackageManager;
using Dynamo.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dynamo.Wpf.Utilities
{
    internal class CefHelper
    {
        internal readonly DynamoViewModel dynamoViewModel;
        internal PackageLoader Model { get; private set; }

        public ChromiumWebBrowser CefBrowser { get; set; }

        public string SessionData { get { return JsonConvert.SerializeObject(dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.GetSession()); } }

        public CefHelper(DynamoViewModel dynamoViewModel, PackageLoader model)
        {
            this.dynamoViewModel = dynamoViewModel;
            this.Model = model;
        }

        public bool Login()
        {
            return dynamoViewModel.Model.AuthenticationManager.AuthProvider.Login();
        }
    }
}
