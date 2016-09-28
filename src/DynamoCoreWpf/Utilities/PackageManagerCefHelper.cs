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
                var script = string.Format("window['updateDownloadStatus'].updateStatus(); var st = $('[data-role=\"search-list-panel\"]').scrollTop(); $('[data-role=\"search-list-panel\"]').scrollTop(st+1).scrollTop(st-1);");
                CefBrowser.GetMainFrame().ExecuteJavaScriptAsync(script);
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

        }

        public void DownloadDependencies()
        {
            //var client = new RestClient("https://beta-api.acg.autodesk.com");
            //var fileClient = new RestClient("https://beta-storage.acg.autodesk.com");

            foreach (string pkg in PackagesToInstall)
            {
                string[] temp = pkg.Split(',');
                DynamoRequest req = new DynamoRequest("assets/" + temp[0], Method.GET);
                CefResponseWithContentBody response = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(req);
                DownloadRequest = response.content;

                DynamoRequest fileReq = new DynamoRequest(@"files/download?file_ids=" + temp[1] + "&asset_id=" + temp[0], Method.GET, true);
                CefResponse res = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteDynamoCefRequest(fileReq);
                var pathToPackage = GetFileFromResponse(res.InternalRestReponse);
                InstallPackage(pathToPackage);
            }
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

            List<dynamic> packageVersionData = new List<dynamic>();
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

                        var res = response.content;

                        if (res.custom_data.Count > 0)
                        {
                            dynamic versionData;
                            string json;
                            for (int i = 0; i < res.custom_data.Count; i++)
                            {
                                if (res.custom_data[i].key == "version:" + temp[1])
                                {
                                    json = System.Uri.UnescapeDataString(res.custom_data[i].data.ToString());
                                    versionData = JsonConvert.DeserializeObject<dynamic>(json);
                                    packageVersionData.Add(versionData);

                                    PackagesToInstall.Add(temp[0] + "," + versionData.file_id.Value);
                                }
                            }
                        }

                    }

                    //    var headers = version.dependencies.Select(dep => dep.name).Select((id) =>
                    //    {
                    //        PackageHeader pkgHeader;
                    //        var res = pmExt.PackageManagerClient.DownloadPackageHeader(id, out pkgHeader);

                    //        if (!res.Success)
                    //            MessageBox.Show(String.Format(Resources.MessageFailedToDownloadPackage, id),
                    //                Resources.PackageDownloadErrorMessageBoxTitle,
                    //                MessageBoxButton.OK, MessageBoxImage.Error);

                    //        return pkgHeader;
                    //    }).ToList();

                    //    // if any header download fails, abort
                    //    if (headers.Any(x => x == null))
                    //    {
                    //        return;
                    //    }

                    //    var allPackageVersions = PackageManagerSearchElement.ListRequiredPackageVersions(headers, version);

                    //    // determine if any of the packages contain binaries or python scripts.  
                    //    var containsBinaries =
                    //        allPackageVersions.Any(
                    //            x => x.Item2.contents.Contains(PackageManagerClient.PackageContainsBinariesConstant) || x.Item2.contains_binaries);

                    //    var containsPythonScripts =
                    //        allPackageVersions.Any(
                    //            x => x.Item2.contents.Contains(PackageManagerClient.PackageContainsPythonScriptsConstant));

                    //    // if any do, notify user and allow cancellation
                    //    if (containsBinaries || containsPythonScripts)
                    //    {
                    //        var res = MessageBox.Show(Resources.MessagePackageContainPythonScript,
                    //            Resources.PackageDownloadMessageBoxTitle,
                    //            MessageBoxButton.OKCancel, MessageBoxImage.Exclamation);

                    //        if (res == MessageBoxResult.Cancel) return;
                    //    }

                    //    // Determine if there are any dependencies that are made with a newer version
                    //    // of Dynamo (this includes the root package)
                    //    var dynamoVersion = this.PackageManagerClientViewModel.DynamoViewModel.Model.Version;
                    //    var dynamoVersionParsed = VersionUtilities.PartialParse(dynamoVersion, 3);
                    //    var futureDeps = allPackageVersions.FilterFuturePackages(dynamoVersionParsed);

                    //    // If any of the required packages use a newer version of Dynamo, show a dialog to the user
                    //    // allowing them to cancel the package download
                    //    if (futureDeps.Any())
                    //    {
                    //        var versionList = FormatPackageVersionList(futureDeps);

                    //        if (MessageBox.Show(String.Format(Resources.MessagePackageNewerDynamo,
                    //            PackageManagerClientViewModel.DynamoViewModel.BrandingResourceProvider.ProductName,
                    //            versionList),
                    //            string.Format(Resources.PackageUseNewerDynamoMessageBoxTitle,
                    //            PackageManagerClientViewModel.DynamoViewModel.BrandingResourceProvider.ProductName),
                    //            MessageBoxButton.OKCancel,
                    //            MessageBoxImage.Warning) == MessageBoxResult.Cancel)
                    //        {
                    //            return;
                    //        }
                    //    }

                    //    var localPkgs = pmExt.PackageLoader.LocalPackages;

                    //    var uninstallsRequiringRestart = new List<Package>();
                    //    var uninstallRequiringUserModifications = new List<Package>();
                    //    var immediateUninstalls = new List<Package>();

                    //    // if a package is already installed we need to uninstall it, allowing
                    //    // the user to cancel if they do not want to uninstall the package
                    //    foreach (var localPkg in headers.Select(x => localPkgs.FirstOrDefault(v => v.Name == x.name)))
                    //    {
                    //        if (localPkg == null) continue;

                    //        if (localPkg.LoadedAssemblies.Any())
                    //        {
                    //            uninstallsRequiringRestart.Add(localPkg);
                    //            continue;
                    //        }

                    //        if (localPkg.InUse(this.PackageManagerClientViewModel.DynamoViewModel.Model))
                    //        {
                    //            uninstallRequiringUserModifications.Add(localPkg);
                    //            continue;
                    //        }

                    //        immediateUninstalls.Add(localPkg);
                    //    }

                    //    if (uninstallRequiringUserModifications.Any())
                    //    {
                    //        MessageBox.Show(String.Format(Resources.MessageUninstallToContinue,
                    //            PackageManagerClientViewModel.DynamoViewModel.BrandingResourceProvider.ProductName,
                    //            JoinPackageNames(uninstallRequiringUserModifications)),
                    //            Resources.CannotDownloadPackageMessageBoxTitle,
                    //            MessageBoxButton.OK, MessageBoxImage.Error);
                    //        return;
                    //    }

                    //    var settings = PackageManagerClientViewModel.DynamoViewModel.Model.PreferenceSettings;

                    //    if (uninstallsRequiringRestart.Any())
                    //    {
                    //        // mark for uninstallation
                    //        uninstallsRequiringRestart.ForEach(
                    //            x => x.MarkForUninstall(settings));

                    //        MessageBox.Show(String.Format(Resources.MessageUninstallToContinue2,
                    //            PackageManagerClientViewModel.DynamoViewModel.BrandingResourceProvider.ProductName,
                    //            JoinPackageNames(uninstallsRequiringRestart)),
                    //            Resources.CannotDownloadPackageMessageBoxTitle,
                    //            MessageBoxButton.OK, MessageBoxImage.Error);
                    //        return;
                    //    }

                    //    if (immediateUninstalls.Any())
                    //    {
                    //        // if the package is not in use, tell the user we will be uninstall it and give them the opportunity to cancel
                    //        if (MessageBox.Show(String.Format(Resources.MessageAlreadyInstallDynamo,
                    //            PackageManagerClientViewModel.DynamoViewModel.BrandingResourceProvider.ProductName,
                    //            JoinPackageNames(immediateUninstalls)),
                    //            Resources.DownloadWarningMessageBoxTitle,
                    //            MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel)
                    //            return;
                    //    }

                    //    // add custom path to custom package folder list
                    //    if (!String.IsNullOrEmpty(downloadPath))
                    //    {
                    //        if (!settings.CustomPackageFolders.Contains(downloadPath))
                    //            settings.CustomPackageFolders.Add(downloadPath);
                    //    }

                    //    // form header version pairs and download and install all packages
                    //    allPackageVersions
                    //            .Select(x => new PackageDownloadHandle(x.Item1, x.Item2))
                    //            .ToList()
                    //            .ForEach(x => this.PackageManagerClientViewModel.DownloadAndInstall(x, downloadPath));

                    //}
                }

            }
            else
            {
                //CancelAction();
                return "cancel";
            }
            return String.Join("|", PackagesToInstall);
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


        private void GoToRootDirectory()
        {
            Package localPkg = Model.LocalPackages.Where(a => a.Name == this.PkgRequest.asset_name.ToString()).First();
            Process.Start(localPkg.RootDirectory);
        }
    }
}
