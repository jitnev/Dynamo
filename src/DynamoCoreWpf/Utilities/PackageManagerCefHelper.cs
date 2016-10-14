using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

using Dynamo.PackageManager;

using Microsoft.Practices.Prism.ViewModel;
using System.Collections.Generic;
using Newtonsoft.Json;
using CefSharp;
using System.Windows;
using System;
using CefSharp.Wpf;
using Dynamo.Wpf.Properties;
using RestSharp;
using static Dynamo.PackageManager.PackageDownloadHandle;
//using ACG.Utility;
using System.IO;
using Dynamo.ViewModels;
using System.Configuration;
using ACGClientForCEF.Requests;
using ACGClientForCEF;
using ACGClientForCEF.Utility;
using System.Diagnostics;
using Dynamo.UI;
using Dynamo.Utilities;
using System.Threading;

namespace Dynamo.Wpf.Utilities
{
    internal class PackageManagerCefHelper : CefHelper, IDownloadHandler
    {

        public PackageManagerCefHelper(DynamoViewModel dynamoViewModel, PackageLoader model) : base(dynamoViewModel, model)
        {
        }

        public List<string> PackagesToInstall { get; set; }

        public State PackageDownloadInstallState { get; set; }

        private dynamic _downloadRequest;
        public dynamic DownloadRequest
        {
            get { return _downloadRequest; }
            set
            {
                if (value is Newtonsoft.Json.Linq.JObject)
                    _downloadRequest = value;
                else
                    _downloadRequest = JsonConvert.DeserializeObject<dynamic>(value);
            }
        }

        private dynamic _pkgRequest;
        public dynamic PkgRequest
        {
            get { return _pkgRequest; }
            set
            {
                if (value is Newtonsoft.Json.Linq.JObject)
                    _pkgRequest = value;
                else
                    _pkgRequest = JsonConvert.DeserializeObject<dynamic>(value);
            }
        }

        private string PackageInstallPath { get; set; }

        internal void GoToWebsite()
        {
            dynamoViewModel.PackageManagerClientViewModel.GoToWebsite();
        }

        #region "Download Handler"
        public void OnBeforeDownload(IBrowser browser, DownloadItem downloadItem, IBeforeDownloadCallback callback)
        {
            if (!callback.IsDisposed)
            {
                using (callback)
                {
                    callback.Continue(downloadItem.SuggestedFileName, showDialog: false);
                }
            }
        }

        public void OnDownloadUpdated(IBrowser browser, DownloadItem downloadItem, IDownloadItemCallback callback)
        {
            if (downloadItem.IsComplete && DownloadRequest != null)
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    InstallPackage(downloadItem.FullPath);
                }));
                //var script = string.Format("window['updateDownloadStatus'].updateStatus(); var st = $('[data-role=\"search-list-panel\"]').scrollTop(); $('[data-role=\"search-list-panel\"]').scrollTop(st+1).scrollTop(st-1);");
                //CefBrowser.GetMainFrame().ExecuteJavaScriptAsync(script);
            }
        }
        #endregion

        private void CancelAction()
        {
            var script = string.Format("window['actionCanceled'] = true;");
            CefBrowser.GetMainFrame().ExecuteJavaScriptAsync(script);
        }

        private void InstallPackage(string downloadPath)
        {
            var firstOrDefault = Model.LocalPackages.FirstOrDefault(pkg => pkg.AssetID == DownloadRequest.asset_id.ToString());
            if (firstOrDefault != null)
            {
                var dynModel = dynamoViewModel.Model;
                try
                {
                    firstOrDefault.UninstallCore(dynModel.CustomNodeManager, dynamoViewModel.Model.GetPackageManagerExtension().PackageLoader, dynModel.PreferenceSettings);
                }
                catch
                {
                    //MessageBox.Show(String.Format(Resources.MessageFailToUninstallPackage,
                    //    DynamoViewModel.BrandingResourceProvider.ProductName,
                    //    packageDownloadHandle.Name),
                    //    Resources.UninstallFailureMessageBoxTitle,
                    //    MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            var settings = dynamoViewModel.Model.PreferenceSettings;
            PackageDownloadHandle packageDownloadHandle = new PackageDownloadHandle();
            packageDownloadHandle.Name = DownloadRequest.asset_name;
            packageDownloadHandle.Done(downloadPath);

            string installedPkgPath = string.Empty;
            if (packageDownloadHandle.Extract(dynamoViewModel.Model, this.PackageInstallPath, out installedPkgPath))
            {
                var p = Package.FromDirectory(installedPkgPath, dynamoViewModel.Model.Logger);
                //TODO: Jitendra to see if assetID also uploaded, when package is uploaded.
                p.AssetID = DownloadRequest.asset_id;
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    dynamoViewModel.Model.GetPackageManagerExtension().PackageLoader.Load(p);
                }));
                packageDownloadHandle.DownloadState = PackageDownloadHandle.State.Installed;
                PackageDownloadInstallState = State.Installed;
                this.PackageInstallPath = string.Empty;
            }

            //var script = string.Format("window['updateDownloadStatus'].updateStatus('" + DownloadRequest.asset_name.ToString() + "', 'Installed'" + ");");
            //CefBrowser.GetMainFrame().ExecuteJavaScriptAsync(script);
        }

        public void DownloadAndInstall(string pkg)
        {
            //var client = new RestClient("https://beta-api.acg.autodesk.com");
            //var fileClient = new RestClient("https://beta-storage.acg.autodesk.com");

            
            
            //foreach (string pkg in PackagesToInstall)
            //{
                string[] temp = pkg.Split(',');
                DynamoRequest req = new DynamoRequest("assets/" + temp[0], Method.GET);
                CefResponseWithContentBody response = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(req);
                DownloadRequest = response.content;

                DynamoRequest fileReq = new DynamoRequest(@"files/download?file_ids=" + temp[1] + "&asset_id=" + temp[0], Method.GET, true);
                CefResponse res = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteDynamoCefRequest(fileReq);
                var pathToPackage = GetFileFromResponse(res.InternalRestReponse);
                InstallPackage(pathToPackage);

            //}
        }

        public string GetFileFromResponse(IRestResponse gregResponse)
        {
            var response = gregResponse;

            if (!(response.ResponseUri != null && response.ResponseUri.AbsolutePath != null)) return "";

            var tempOutput = System.IO.Path.Combine(FileUtilities.GetTempFolder(), System.IO.Path.GetFileName(response.ResponseUri.AbsolutePath));
            using (var f = new FileStream(tempOutput, FileMode.Create))
            {
                f.Write(response.RawBytes, 0, (int)response.ContentLength);
            }
            //TODO: Jitendra verify if this needed
            //var md5HeaderResp = response.Headers.FirstOrDefault(x => x.Name == "ETag");
            //if (md5HeaderResp == null) throw new Exception("Could not check integrity of package download!");

            //var md5HeaderComputed =
            //    String.Join("", FileUtilities.GetMD5Checksum(tempOutput).Select(x => x.ToString("X"))).ToLower();

            //if (md5HeaderResp.Value.ToString() == md5HeaderComputed )
            //    throw new Exception("Could not validate package integrity!  Please try again!");

            return tempOutput;

        }

        public string GetCustomPathForInstall()
        {
            string downloadPath = string.Empty;

            downloadPath = GetDownloadPath();

            if (String.IsNullOrEmpty(downloadPath))
                return string.Empty;

            PackageInstallPath = downloadPath;

            return downloadPath;
        }

        private string GetDownloadPath()
        {
            var args = new PackagePathEventArgs();

            ShowFileDialog(args);

            if (args.Cancel)
                return string.Empty;

            return args.Path;
        }

        private void ShowFileDialog(PackagePathEventArgs e)
        {
            string initialPath = dynamoViewModel.Model.PathManager.DefaultPackagesDirectory;

            e.Cancel = true;

            var dialog = new DynamoFolderBrowserDialog
            {
                // Navigate to initial folder.
                SelectedPath = initialPath,
                Title = "Install Path",
                Owner = this.ParentWindow
            };
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    e.Cancel = false;
                    e.Path = dialog.SelectedPath;
                }
            }));
        }


        public string PackageOnExecuted(dynamic asset, dynamic version)
        {
            string downloadPath = this.PackageInstallPath;

            List<Tuple<dynamic, dynamic>> packageVersionData = new List<Tuple<dynamic, dynamic>>();
            string msg = String.IsNullOrEmpty(downloadPath) ?
                String.Format(Resources.MessageConfirmToInstallPackage, asset["asset_name"], version["version"]) :
                String.Format(Resources.MessageConfirmToInstallPackageToFolder, asset["asset_name"], version["version"], downloadPath);

            var result = MessageBox.Show(msg,
                Resources.PackageDownloadConfirmMessageBoxTitle,
                MessageBoxButton.OKCancel, MessageBoxImage.Question);

            var pmExt = dynamoViewModel.Model.GetPackageManagerExtension();

            if (PackagesToInstall == null)
                PackagesToInstall = new List<string>();
            else
                PackagesToInstall.Clear();

            if (result == MessageBoxResult.OK)
            {
                if (!string.IsNullOrEmpty(version["dependencies"]))
                {
                    // get all of the headers
                    DynamoRequest req;
                    string[] depends = version["dependencies"].Split(',');
                    foreach (string depend in depends)
                    {
                        string[] temp = depend.Split('|');
                        req = new DynamoRequest(("assets/" + temp[0] + "/customdata").Trim(), Method.GET);
                        CefResponseWithContentBody response = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(req);
                        var customData = response.content;

                        req = new DynamoRequest(("assets/" + temp[0]).Trim(), Method.GET);
                        response = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(req);
                        var depAsset = response.content;

                        if (customData.custom_data.Count > 0)
                        {
                            dynamic versionData;
                            string json;
                            for (int i = 0; i < customData.custom_data.Count; i++)
                            {
                                if (customData.custom_data[i].key == "version:" + temp[1])
                                {
                                    json = System.Uri.UnescapeDataString(customData.custom_data[i].data.ToString());
                                    versionData = JsonConvert.DeserializeObject<dynamic>(json);

                                    packageVersionData.Add(new Tuple<dynamic, dynamic>(depAsset, versionData));
                                    PackagesToInstall.Add(temp[0] + "," + versionData.file_id.Value + "," + depAsset.asset_name);
                                }
                            }
                        }
                    }
                }

                PackagesToInstall.Add(asset["asset_id"] + "," + version["file_id"] + "," + asset["asset_name"]);

                //    // determine if any of the packages contain binaries or python scripts.  
                var containsBinaries =
                    packageVersionData.Any(
                        x => x.Item2.contents.ToString().Contains(PackageManagerClient.PackageContainsBinariesConstant) || (bool)x.Item2.contains_binaries);

                containsBinaries = containsBinaries || (version["contents"].ToString().Contains(PackageManagerClient.PackageContainsBinariesConstant) || (bool)version["contains_binaries"]);

                var containsPythonScripts =
                    packageVersionData.Any(
                        x => x.Item2.contents.ToString().Contains(PackageManagerClient.PackageContainsPythonScriptsConstant));

                containsPythonScripts = containsPythonScripts || (version["contents"].ToString().Contains(PackageManagerClient.PackageContainsPythonScriptsConstant));

                // if any do, notify user and allow cancellation
                if (containsBinaries || containsPythonScripts)
                {
                    var res = MessageBox.Show(Resources.MessagePackageContainPythonScript,
                        Resources.PackageDownloadMessageBoxTitle,
                        MessageBoxButton.OKCancel, MessageBoxImage.Exclamation);

                    if (res == MessageBoxResult.Cancel) return "cancel";
                }

                // Determine if there are any dependencies that are made with a newer version
                // of Dynamo (this includes the root package)
                var dynamoVersion = dynamoViewModel.Model.Version;
                var dynamoVersionParsed = VersionUtilities.PartialParse(dynamoVersion, 3);
                var futureDeps = FilterFuturePackages(packageVersionData, dynamoVersionParsed);

                // If any of the required packages use a newer version of Dynamo, show a dialog to the user
                // allowing them to cancel the package download
                if (futureDeps.Any())
                {
                    var versionList = FormatPackageVersionList(futureDeps.ToList());

                    if (MessageBox.Show(String.Format(Resources.MessagePackageNewerDynamo,
                        dynamoViewModel.BrandingResourceProvider.ProductName,
                        versionList),
                        string.Format(Resources.PackageUseNewerDynamoMessageBoxTitle,
                        dynamoViewModel.BrandingResourceProvider.ProductName),
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning) == MessageBoxResult.Cancel)
                    {
                        return "cancel";
                    }
                }

                var localPkgs = pmExt.PackageLoader.LocalPackages;

                var uninstallsRequiringRestart = new List<Package>();
                var uninstallRequiringUserModifications = new List<Package>();
                var immediateUninstalls = new List<Package>();

                // if a package is already installed we need to uninstall it, allowing
                // the user to cancel if they do not want to uninstall the package
                foreach (var localPkg in packageVersionData.Select(x => localPkgs.FirstOrDefault(v => v.Name == x.Item1.asset_name.ToString())))
                {
                    if (localPkg == null) continue;

                    if (localPkg.LoadedAssemblies.Any())
                    {
                        uninstallsRequiringRestart.Add(localPkg);
                        continue;
                    }

                    if (localPkg.InUse(dynamoViewModel.Model))
                    {
                        uninstallRequiringUserModifications.Add(localPkg);
                        continue;
                    }

                    immediateUninstalls.Add(localPkg);
                }

                if (uninstallRequiringUserModifications.Any())
                {
                    MessageBox.Show(String.Format(Resources.MessageUninstallToContinue,
                        dynamoViewModel.BrandingResourceProvider.ProductName,
                        JoinPackageNames(uninstallRequiringUserModifications)),
                        Resources.CannotDownloadPackageMessageBoxTitle,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return "cancel";
                }

                var settings = dynamoViewModel.Model.PreferenceSettings;

                if (uninstallsRequiringRestart.Any())
                {
                    // mark for uninstallation
                    uninstallsRequiringRestart.ForEach(
                        x => x.MarkForUninstall(settings));

                    MessageBox.Show(String.Format(Resources.MessageUninstallToContinue2,
                        dynamoViewModel.BrandingResourceProvider.ProductName,
                        JoinPackageNames(uninstallsRequiringRestart)),
                        Resources.CannotDownloadPackageMessageBoxTitle,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return "cancel";
                }

                if (immediateUninstalls.Any())
                {
                    // if the package is not in use, tell the user we will be uninstall it and give them the opportunity to cancel
                    if (MessageBox.Show(String.Format(Resources.MessageAlreadyInstallDynamo,
                        dynamoViewModel.BrandingResourceProvider.ProductName,
                        JoinPackageNames(immediateUninstalls)),
                        Resources.DownloadWarningMessageBoxTitle,
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel)
                        return "cancel";
                }

                //    // form header version pairs and download and install all packages
                //    allPackageVersions
                //            .Select(x => new PackageDownloadHandle(x.Item1, x.Item2))
                //            .ToList()
                //            .ForEach(x => this.PackageManagerClientViewModel.DownloadAndInstall(x, downloadPath));

                //}


                // add custom path to custom package folder list
                if (!String.IsNullOrEmpty(downloadPath))
                {
                    if (!settings.CustomPackageFolders.Contains(downloadPath))
                        settings.CustomPackageFolders.Add(downloadPath);
                }
            }
            else
            {
                //CancelAction();
                return "cancel";
            }
            return String.Join("|", PackagesToInstall);
        }

        private string JoinPackageNames(IEnumerable<Package> pkgs)
        {

            return String.Join(", ", pkgs.Select(x => x.Name));
        }

        public static string FormatPackageVersionList(List<Tuple<dynamic, dynamic>> packages)
        {
            return String.Join("\r\n", packages.Select(x => x.Item1.name.ToString() + " " + x.Item2.version.ToString()));
        }

        public IEnumerable<Tuple<dynamic, dynamic>> FilterFuturePackages(List<Tuple<dynamic, dynamic>> pkgVersions, Version currentAppVersion, int numberOfFieldsToCompare = 3)
        {
            foreach (Tuple<dynamic,dynamic> version in pkgVersions)
            {
                var depAppVersion = VersionUtilities.PartialParse(version.Item2.engine_version.ToString(), numberOfFieldsToCompare);

                if (depAppVersion > currentAppVersion)
                {
                    yield return version;
                }
            }
        }


        public bool Uninstall()
        {
            Package localPkg = Model.LocalPackages.Where(a => a.Name == this.PkgRequest.asset_name.ToString()).First();

            if (localPkg.LoadedAssemblies.Any())
            {
                var resAssem =
                    MessageBox.Show(string.Format(Resources.MessageNeedToRestart,
                        dynamoViewModel.BrandingResourceProvider.ProductName),
                        Resources.UninstallingPackageMessageBoxTitle,
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Exclamation);
                if (resAssem == MessageBoxResult.Cancel) return false;
            }

            var res = MessageBox.Show(String.Format(Resources.MessageConfirmToUninstallPackage, localPkg.Name),
                                      Resources.UninstallingPackageMessageBoxTitle,
                                      MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.No)
            {
                CancelAction();
                return false;
            }

            try
            {
                var dynModel = dynamoViewModel.Model;
                var pmExtension = dynModel.GetPackageManagerExtension();
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    localPkg.UninstallCore(dynModel.CustomNodeManager, pmExtension.PackageLoader, dynModel.PreferenceSettings);
                }));

                return true;
            }
            catch (Exception)
            {
                MessageBox.Show(string.Format(Resources.MessageFailedToUninstall,
                    dynamoViewModel.BrandingResourceProvider.ProductName),
                    Resources.UninstallFailureMessageBoxTitle,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }


        public void GoToRootDirectory()
        {
            Package localPkg = Model.LocalPackages.Where(a => a.Name == this.PkgRequest.asset_name.ToString()).First();
            Process.Start(localPkg.RootDirectory);
        }

        public void UnmarkForUninstallation()
        {
            Package pkg = Model.LocalPackages.Where(a => a.Name == this.PkgRequest.asset_name.ToString()).First();
            if (pkg != null)
            {
                pkg.UnmarkForUninstall(dynamoViewModel.Model.PreferenceSettings);
            }
        }
    }
}
