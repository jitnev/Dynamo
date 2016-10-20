using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Security.AccessControl;
using System.Security.Permissions;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Dynamo.Core;
using Dynamo.Graph;
using Dynamo.Graph.Nodes.ZeroTouch;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Nodes;
using Dynamo.PackageManager.UI;
using Dynamo.UI;
using Dynamo.Utilities;
using Dynamo.ViewModels;

using DynamoUtilities;

//using ACG.Requests;
using Microsoft.Practices.Prism.Commands;
using Dynamo.Wpf.Properties;
using CefSharp.Wpf;
using Newtonsoft.Json;
using CefSharp;
using Dynamo.PackageManager.Interfaces;
using RestSharp;
using ACGClientForCEF.Requests;
using Dynamo.PackageManager;
using ACGClientForCEF.Models;
using Dynamo.Graph.Nodes;
using Dynamo.Configuration;

namespace Dynamo.Wpf.Utilities
{
    internal class PublishCefHelper : CefHelper
    {
        //private readonly DynamoViewModel dynamoViewModel;
        private MutatingFileCompressor fileCompressor;
        private IFileInfo fileToUpload;

        private dynamic _versionCustomData;

        private PackageManagerViewModel packageMgrViewModel { get; set; }

        public PublishCefHelper(DynamoViewModel dynamoViewModel, PackageLoader model, PackageManagerViewModel pkgManagerViewModel) : base(dynamoViewModel, model, pkgManagerViewModel)
        {
            fileCompressor = new MutatingFileCompressor();
            customNodeDefinitions = new List<CustomNodeDefinition>();
            Dependencies = new List<PackageDependency>();
            Assemblies = new List<PackageAssembly>();
            PackageAssemblyNodes = new List<TypeLoadData>();
            FilesToUpload = new List<string>();
        }

        public string EngineVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
        }
        //public PackageLoader Model { get; private set; }

        private List<string> _filesToUpload;
        public List<string> FilesToUpload
        {
            get;
            set;
        }

        public string FilesToUploadJson { get { return JsonConvert.SerializeObject(FilesToUpload); } }
        public dynamic PackageDetails { get; set; }
        public dynamic PackageCustomDetails { get; set; }


        /// <summary>
        /// The package used for this submission
        /// </summary>
        private Package _publishPackageData;
        public Package PublishPackageData
        {
            get { return _publishPackageData; }
            set { _publishPackageData = value; }
        }

        internal const long MaximumPackageSize = 1000 * 1024 * 1024; // 1 GB

        /// <summary>
        /// CustomNodeDefinitions property 
        /// </summary>
        private List<CustomNodeDefinition> customNodeDefinitions;
        public List<CustomNodeDefinition> CustomNodeDefinitions
        {
            get { return customNodeDefinitions; }
            set
            {
                customNodeDefinitions = value;

                if (customNodeDefinitions.Count > 0 && Name == null)
                {
                    Name = CustomNodeDefinitions[0].DisplayName;
                }

                UpdateDependencies();
            }
        }

        public List<PackageAssembly> Assemblies { get; set; }
        private List<TypeLoadData> PackageAssemblyNodes { get; set; }

        /// <summary>
        /// AdditionalFiles property 
        /// </summary>
        private List<string> _additionalFiles = new List<string>();
        public List<string> AdditionalFiles
        {
            get { return _additionalFiles; }
            set
            {
                if (_additionalFiles != value)
                {
                    _additionalFiles = value;
                }
            }
        }

        /// <summary>
        /// IsNewVersion property </summary>
        /// <value>
        /// Specifies whether we're negotiating uploading a new version </value>
        private bool _isNewVersion = false;
        public bool IsNewVersion
        {
            get { return _isNewVersion; }
            set
            {
                if (_isNewVersion != value)
                {
                    _isNewVersion = value;
                }
            }
        }

        public event PublishSuccessHandler PublishSuccess;
        public List<PackageDependency> Dependencies { get; set; }

        public event EventHandler<PackagePathEventArgs> RequestShowFolderBrowserDialog;
        public virtual void OnRequestShowFileDialog(object sender, PackagePathEventArgs e)
        {
            if (RequestShowFolderBrowserDialog != null)
            {
                RequestShowFolderBrowserDialog(sender, e);
            }
        }

        public string[] ShowAddFileDialogAndAdd()
        {
            List<string> files = new List<string>();
            System.Windows.Application.Current.Dispatcher.Invoke((Action)(() =>
            {

                // show file open dialog
                FileDialog fDialog = null;

                if (fDialog == null)
                {
                    fDialog = new OpenFileDialog()
                    {
                        Filter = string.Format(Resources.FileDialogCustomNodeDLLXML, "*.dyf;*.dll;*.xml") + "|" +
                             string.Format(Resources.FileDialogAllFiles, "*.*"),
                        Title = Resources.AddCustomFileToPackageDialogTitle,
                        Multiselect = true
                    };

                }

                // if you've got the current space path, add it to shortcuts 
                // so that user is able to easily navigate there
                var currentFileName = dynamoViewModel.Model.CurrentWorkspace.FileName;
                if (!string.IsNullOrEmpty(currentFileName))
                {
                    var fi = new FileInfo(currentFileName);
                    fDialog.CustomPlaces.Add(fi.DirectoryName);
                }

                // add the definitions directory to shortcuts as well
                var pathManager = dynamoViewModel.Model.PathManager;
                if (Directory.Exists(pathManager.DefaultUserDefinitions))
                {
                    fDialog.CustomPlaces.Add(pathManager.DefaultUserDefinitions);
                }

                if (fDialog.ShowDialog() != DialogResult.OK) return;

                foreach (var file in fDialog.FileNames)
                {
                    AddFile(file);
                    files.Add(new FileInfo(file).Name);
                }
            }));
            return files.ToArray();
        }

        public void OnPublishSuccess()
        {
            if (PublishSuccess != null)
                PublishSuccess(this);
        }

        internal void AddFile(string filename)
        {
            if (!File.Exists(filename)) return;

            FilesToUpload.Add(filename);

            if (filename.ToLower().EndsWith(".dll"))
            {
                AddDllFile(filename);
                return;
            }

            if (filename.ToLower().EndsWith(".dyf"))
            {
                AddCustomNodeFile(filename);
                return;
            }

            AddAdditionalFile(filename);
        }

        private void AddCustomNodeFile(string filename)
        {
            CustomNodeInfo nodeInfo;
            if (dynamoViewModel.Model.CustomNodeManager.AddUninitializedCustomNode(filename, DynamoModel.IsTestMode, out nodeInfo))
            {
                // add the new packages folder to path
                dynamoViewModel.Model.CustomNodeManager.AddUninitializedCustomNodesInPath(Path.GetDirectoryName(filename), DynamoModel.IsTestMode);

                CustomNodeDefinition funcDef;
                if (dynamoViewModel.Model.CustomNodeManager.TryGetFunctionDefinition(nodeInfo.FunctionId, DynamoModel.IsTestMode, out funcDef)
                    && CustomNodeDefinitions.All(x => x.FunctionId != funcDef.FunctionId))
                {
                    CustomNodeDefinitions.Add(funcDef);
                }
            }
        }

        private void AddAdditionalFile(string filename)
        {
            try
            {
                AdditionalFiles.Add(filename);
            }
            catch (Exception e)
            {
                dynamoViewModel.Model.Logger.Log(e);
            }
        }

        private void AddDllFile(string filename)
        {
            try
            {
                Assembly assem;

                // we're not sure if this is a managed assembly or not
                // we try to load it, if it fails - we add it as an additional file
                var result = PackageLoader.TryLoadFrom(filename, out assem);
                if (result)
                {
                    var assemName = assem.GetName().Name;

                    // The user has attempted to load an existing dll from another path. This is not allowed 
                    // as the existing assembly cannot be modified while Dynamo is active.
                    if (this.Assemblies.Any(x => assemName == x.Assembly.GetName().Name))
                    {
                        MessageBox.Show(string.Format(Resources.PackageDuplicateAssemblyWarning,
                                        dynamoViewModel.BrandingResourceProvider.ProductName),
                                        Resources.PackageDuplicateAssemblyWarningTitle,
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Stop);
                        return; // skip loading assembly
                    }

                    Assemblies.Add(new PackageAssembly()
                    {
                        Assembly = assem,
                        IsNodeLibrary = true // assume is node library when first added
                    });
                }
                else
                {
                    AddAdditionalFile(filename);
                }
            }
            catch (Exception e)
            {
                dynamoViewModel.Model.Logger.Log(e);
            }
        }

        private void UpdateDependencies()
        {
            Dependencies.Clear();
            GetAllDependencies().ToList().ForEach(Dependencies.Add);
        }

        private IEnumerable<PackageDependency> GetAllDependencies()
        {
            var pmExtension = dynamoViewModel.Model.GetPackageManagerExtension();
            var pkgLoader = pmExtension.PackageLoader;

            // all workspaces
            var workspaces = new List<CustomNodeWorkspaceModel>();
            foreach (var def in AllDependentFuncDefs())
            {
                CustomNodeWorkspaceModel ws;
                if (dynamoViewModel.Model.CustomNodeManager.TryGetFunctionWorkspace(
                    def.FunctionId,
                    DynamoModel.IsTestMode,
                    out ws))
                {
                    workspaces.Add(ws);
                }
            }

            // get all of dependencies from custom nodes and additional files
            var allFilePackages =
                workspaces
                    .Select(x => x.FileName)
                    .Union(AdditionalFiles)
                    .Where(pkgLoader.IsUnderPackageControl)
                    .Select(pkgLoader.GetOwnerPackage)
                    .Where(x => x != null)
                    .Where(x => (x.Name != Name && x.AssetID != null && x.AssetID != ""))
                    .Distinct()
                    .Select(x => new PackageDependency(x.AssetID, x.VersionName));

            workspaces = new List<CustomNodeWorkspaceModel>();
            foreach (var def in AllFuncDefs())
            {
                CustomNodeWorkspaceModel ws;
                if (dynamoViewModel.Model.CustomNodeManager.TryGetFunctionWorkspace(
                    def.FunctionId,
                    DynamoModel.IsTestMode,
                    out ws))
                {
                    workspaces.Add(ws);
                }
            }

            // get all of the dependencies from types
            var allTypePackages = workspaces
                .SelectMany(x => x.Nodes)
                .Select(x => x.GetType())
                .Where(pkgLoader.IsUnderPackageControl)
                .Select(pkgLoader.GetOwnerPackage)
                .Where(x => x != null)
                .Where(x => (x.Name != Name && x.AssetID != null && x.AssetID != ""))
                .Distinct()
                .Select(x => new PackageDependency(x.AssetID, x.VersionName));

            var dsFunctionPackages = workspaces
                .SelectMany(x => x.Nodes)
                .OfType<DSFunctionBase>()
                .Select(x => x.Controller.Definition.Assembly)
                .Where(pkgLoader.IsUnderPackageControl)
                .Select(pkgLoader.GetOwnerPackage)
                .Where(x => x != null)
                .Where(x => (x.Name != Name && x.AssetID != null && x.AssetID != ""))
                .Distinct()
                .Select(x => new PackageDependency(x.AssetID, x.VersionName));

            return allFilePackages.Union(allTypePackages).Union(dsFunctionPackages);

        }

        private IEnumerable<CustomNodeDefinition> AllDependentFuncDefs()
        {
            return
                CustomNodeDefinitions.Select(x => x.Dependencies)
                                   .SelectMany(x => x)
                                   .Where(x => !CustomNodeDefinitions.Contains(x))
                                   .Distinct();
        }

        private IEnumerable<CustomNodeDefinition> AllFuncDefs()
        {
            return AllDependentFuncDefs().Union(CustomNodeDefinitions).Distinct();
        }

        /// <summary>
        /// Delegate used to submit the element</summary>
        public void Submit(string assetID)
        {
            System.Windows.Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                var files = BuildPackage();
                try
                {
                    //if buildPackage() returns no files then the package
                    //is empty so we should return
                    if (files == null || files.Count() < 1)
                    {
                        return;
                    }
                    // begin submission
                    var pmExtension = dynamoViewModel.Model.GetPackageManagerExtension();
                    PublishPackageData.AssetID = assetID;
                    fileToUpload = BuildAndZip(PublishPackageData, pmExtension.PackageLoader.DefaultPackagesDirectory, files);
                }
                catch (Exception e)
                {
                    dynamoViewModel.Model.Logger.Log(e);
                }
            }));
        }

        public string UploadFile()
        {
            if (fileToUpload != null)
            {
                DynamoRequest req = new DynamoRequest("files/upload", Method.POST, true, fileToUpload.Name);
                var res = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(req);
                var content = res.content;
                return content.files[0].file_id.ToString();
            }
            return string.Empty;
        }

        public bool CheckMemberPreference()
        {
            DynamoRequest memberReq = new DynamoRequest("members", Method.GET);
            var res = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(memberReq);
            string memberID = res.content.member.id;

            DynamoRequest req = new DynamoRequest("members/" + memberID + "/preferences?namespace=123D", Method.GET);

            var response = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(req);
            var content = response.content;
            if (content.preferences["tou"] == null)
                return false;
            else
                return content.preferences["tou"].accepted;
        }

        public void UpdateMemberPreference()
        {
            DynamoRequest memberReq = new DynamoRequest("members", Method.GET);
            var res = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(memberReq);
            string memberID = res.content.member.id;

            DynamoRequest req = new DynamoRequest("members/" + memberID + "/preferences?namespace=123D&preference_name=tou&preference_value={\"accepted\": true}", Method.PUT);
            var response = dynamoViewModel.Model.GetPackageManagerExtension().PackageManagerClient.ExecuteAndDeserializeDynamoCefRequest(req);
        }

        private IFileInfo BuildAndZip(Package package, string packagesDirectory, IEnumerable<string> files)
        {
            var builder = new PackageDirectoryBuilder(
                new MutatingFileSystem(),
                new CustomNodePathRemapper(dynamoViewModel.Model.CustomNodeManager,
                    DynamoModel.IsTestMode));
            var dir = builder.BuildDirectory(package, packagesDirectory, files);
            return Zip(dir);
        }

        private IFileInfo Zip(IDirectoryInfo directory)
        {
            IFileInfo info;

            try
            {
                info = fileCompressor.Zip(directory);
            }
            catch
            {
                throw new Exception(Dynamo.Properties.Resources.CouldNotCompressFile);
            }

            // the file is stored in a tempt directory, we allow it to get cleaned up by the user later
            if (info.Length > MaximumPackageSize) throw new Exception(Dynamo.Properties.Resources.PackageTooLarge);

            return info;
        }

        // build the package
        private IEnumerable<string> BuildPackage()
        {
            try
            {
                var isNewPackage = PublishPackageData == null;

                if (IsNewVersion)
                    PublishPackageData = new Package("", PackageDetails["asset_name"], PackageDetails["majorVersion"] + "." + PackageDetails["minorVersion"] + "." + PackageDetails["buildVersion"], PackageDetails["license_id"]);
                else
                    PublishPackageData = PublishPackageData ?? new Package("", PackageDetails["asset_name"], PackageDetails["majorVersion"] + "." + PackageDetails["minorVersion"] + "." + PackageDetails["buildVersion"], PackageDetails["license_id"]);

                PublishPackageData.Description = PackageDetails["description"];
                //PublishPackageData.Group = Group;
                PublishPackageData.Keywords = PackageDetails["tags"] != null ? PackageDetails["tags"].Split(' ') : null;
                PublishPackageData.License = PackageDetails["license_id"] != null ? PackageDetails["license_id"] : string.Empty;
                PublishPackageData.SiteUrl = PackageCustomDetails["marketing_url"] != null ? PackageCustomDetails["marketing_url"] : string.Empty;
                PublishPackageData.RepositoryUrl = PackageCustomDetails["repository_url"] != null ? PackageCustomDetails["repository_url"] : string.Empty;

                AppendPackageContents();

                PublishPackageData.Dependencies.Clear();
                GetAllDependencies().ToList().ForEach(PublishPackageData.Dependencies.Add);

                var files = GetAllFiles().ToList();
                var pmExtension = dynamoViewModel.Model.GetPackageManagerExtension();

                if (isNewPackage)
                {
                    pmExtension.PackageLoader.Add(PublishPackageData);
                }

                PublishPackageData.AddAssemblies(Assemblies);
                AppendAssemblyContents(files);

                return files;
            }
            catch (Exception e)
            {
                dynamoViewModel.Model.Logger.Log(e);
            }
            return new string[] { };
        }

        private void AppendAssemblyContents(List<string> files)
        {
            List<string> typeMethods = new List<string>();
            foreach(var assembly in Assemblies)
            {
                if(assembly.IsNodeLibrary)
                {
                    Assembly assem;

                    // we're not sure if this is a managed assembly or not
                    // we try to load it, if it fails - we add it as an additional file
                    var filePath = files.Where(a => a.IndexOf(assembly.Name + ".dll") > 0).FirstOrDefault();
                    if (string.IsNullOrEmpty(filePath))
                        continue;
                    var result = PackageLoader.TryLoadFrom(filePath, out assem);

                    if (!NodeModelAssemblyLoader.ContainsNodeModelSubType(assem))
                    {
                        //Type[] types = assem.GetTypes();
                        //foreach (Type type in types)
                        //{
                        //    if (!type.IsPublic)
                        //    {
                        //        continue;
                        //    }
                        //    var members = type.GetMembers().Where(mem => mem.DeclaringType.Name != "Object").ToList();
                        //    foreach (MemberInfo member in members)
                        //    {
                        //        typeMethods.Add(type.Name + "." + member.Name);
                        //    }
                        //}
                        try
                        {
                            typeMethods.AddRange(dynamoViewModel.Model.LibraryServices.GetNodesFromZeroTouchAssem(assem.Location));
                        }
                        catch (Exception e) {
                        }
                        continue;
                    }

                    if (result)
                    {
                        var nodes = new List<TypeLoadData>();
                        dynamoViewModel.Model.Loader.LoadNodesFromAssembly(assem, dynamoViewModel.Model.Context, nodes, new List<TypeLoadData>());
                        PackageAssemblyNodes.AddRange(nodes);
                    }
                }
            }

            if(typeMethods.Count > 0)
                PublishPackageData.Contents += String.Join(", ", typeMethods);

            PublishPackageData.Contents += String.Join(", ", PackageAssemblyNodes.Select((node) => node.Name));
        }

        private void LoadNodesFromAssembly(Assembly assembly, string context, List<TypeLoadData> nodeModels,
            List<TypeLoadData> migrationTypes)
        {
            if (assembly == null)
                throw new ArgumentNullException("assembly");

            Type[] loadedTypes = null;

            try
            {
                loadedTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                dynamoViewModel.Model.Logger.Log(Dynamo.Properties.Resources.CouldNotLoadTypes);
                dynamoViewModel.Model.Logger.Log(e);
                foreach (var ex in e.LoaderExceptions)
                {
                    dynamoViewModel.Model.Logger.Log(Dynamo.Properties.Resources.DllLoadException);
                    dynamoViewModel.Model.Logger.Log(ex.ToString());
                }
            }
            catch (Exception e)
            {
                dynamoViewModel.Model.Logger.Log(Dynamo.Properties.Resources.CouldNotLoadTypes);
                dynamoViewModel.Model.Logger.Log(e);
            }

            foreach (var t in (loadedTypes ?? Enumerable.Empty<Type>()))
            {
                try
                {
                    //only load types that are in the right namespace, are not abstract
                    //and have the elementname attribute
                    if (!t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                    {
                        //if we are running in revit (or any context other than NONE) use the DoNotLoadOnPlatforms attribute, 
                        //if available, to discern whether we should load this type
                        if (context.Equals(Context.NONE)
                            || !t.GetCustomAttributes<DoNotLoadOnPlatformsAttribute>(false)
                                .SelectMany(attr => attr.Values)
                                .Any(e => e.Contains(context)))
                        {
                            nodeModels.Add(new TypeLoadData(t));
                        }
                    }

                    
                }
                catch (Exception e)
                {
                    dynamoViewModel.Model.Logger.Log(String.Format(Dynamo.Properties.Resources.FailedToLoadType, assembly.FullName, t.FullName));
                    dynamoViewModel.Model.Logger.Log(e);
                }
            }
        }

        private void AppendPackageContents()
        {
            //PublishPackageData.Contents = String.Join(", ", GetAllNodeNameDescriptionPairs().Select((pair) => pair.Item1 + " - " + pair.Item2));
            PublishPackageData.Contents = String.Join(", ", GetAllNodeNameDescriptionPairs().Select((pair) => pair.Item1));
        }

        private IEnumerable<Tuple<string, string>> GetAllNodeNameDescriptionPairs()
        {
            var pmExtension = dynamoViewModel.Model.GetPackageManagerExtension();
            var pkgLoader = pmExtension.PackageLoader;

            var workspaces = new List<CustomNodeWorkspaceModel>();
            foreach (var def in AllFuncDefs())
            {
                CustomNodeWorkspaceModel ws;
                if (dynamoViewModel.Model.CustomNodeManager.TryGetFunctionWorkspace(
                    def.FunctionId,
                    DynamoModel.IsTestMode,
                    out ws))
                {
                    workspaces.Add(ws);
                }
            }

            // collect the name-description pairs for every custom node
            return
                workspaces
                    .Where(
                        p =>
                            (pkgLoader.IsUnderPackageControl(p.FileName) && pkgLoader.GetOwnerPackage(p.FileName).Name == Name)
                                || !pmExtension.PackageLoader.IsUnderPackageControl(p.FileName))
                    .Select(
                        x =>
                            new Tuple<string, string>(
                            x.Name,
                            !String.IsNullOrEmpty(x.Description)
                                ? x.Description
                                : Wpf.Properties.Resources.MessageNoNodeDescription));
        }

        private IEnumerable<string> GetAllFiles()
        {
            // get all function defs
            var allFuncs = AllFuncDefs().ToList();

            // all workspaces
            var workspaces = new List<CustomNodeWorkspaceModel>();
            foreach (var def in allFuncs)
            {
                CustomNodeWorkspaceModel ws;
                if (dynamoViewModel.Model.CustomNodeManager.TryGetFunctionWorkspace(
                    def.FunctionId,
                    DynamoModel.IsTestMode,
                    out ws))
                {
                    workspaces.Add(ws);
                }
            }

            // make sure workspaces are saved
            var unsavedWorkspaceNames =
                workspaces.Where(ws => ws.HasUnsavedChanges || ws.FileName == null).Select(ws => ws.Name).ToList();
            if (unsavedWorkspaceNames.Any())
            {
                throw new Exception(Wpf.Properties.Resources.MessageUnsavedChanges0 +
                                    String.Join(", ", unsavedWorkspaceNames) +
                                    Wpf.Properties.Resources.MessageUnsavedChanges1);
            }

            var pmExtension = dynamoViewModel.Model.GetPackageManagerExtension();
            // omit files currently already under package control
            var files =
                workspaces.Select(f => f.FileName)
                    .Where(
                        p =>
                            (pmExtension.PackageLoader.IsUnderPackageControl(p)
                                && (pmExtension.PackageLoader.GetOwnerPackage(p).Name == Name)
                                || !pmExtension.PackageLoader.IsUnderPackageControl(p)));

            // union with additional files
            files = files.Union(AdditionalFiles);
            files = files.Union(Assemblies.Select(x => x.Assembly.Location));

            return files;
        }

        public static PackageManagerViewModel FromLocalPackage(DynamoViewModel dynamoViewModel, Package l, PackageManagerViewModel pkgMgrViewModel=null)
        {
            var defs = new List<CustomNodeDefinition>();

            foreach (var x in l.LoadedCustomNodes)
            {
                CustomNodeDefinition def;
                if (dynamoViewModel.Model.CustomNodeManager.TryGetFunctionDefinition(
                    x.FunctionId,
                    DynamoModel.IsTestMode,
                    out def))
                {
                    defs.Add(def);
                }
            }

            var vm = pkgMgrViewModel != null ? pkgMgrViewModel : new PackageManagerViewModel(dynamoViewModel, dynamoViewModel.Model.GetPackageManagerExtension().PackageLoader, "publish");

            //vm.PublishCompCefHelper = new PublishCefHelper(dynamoViewModel, dynamoViewModel.Model.GetPackageManagerExtension().PackageLoader, vm)
            //{
            vm.PublishCompCefHelper.Group = l.Group;
            vm.PublishCompCefHelper.Description = l.Description;
            vm.PublishCompCefHelper.Keywords = l.Keywords != null ? String.Join(" ", l.Keywords) : "";
            vm.PublishCompCefHelper.CustomNodeDefinitions = defs;
            vm.PublishCompCefHelper.Name = l.Name;
            vm.PublishCompCefHelper.RepositoryUrl = l.RepositoryUrl ?? "";
            vm.PublishCompCefHelper.SiteUrl = l.SiteUrl ?? "";
            vm.PublishCompCefHelper.License = l.License;
            vm.PublishCompCefHelper.PackageDetails = l;
            vm.PublishCompCefHelper.PublishPackageData = l;
            //};

            // add additional files
            l.EnumerateAdditionalFiles();
            foreach (var file in l.AdditionalFiles)
            {
                vm.PublishCompCefHelper.AdditionalFiles.Add(file.Model.FullName);
                vm.PublishCompCefHelper.FilesToUpload.Add(file.Model.FullName);
            }

            var nodeLibraryNames = l.Header.node_libraries;

            // load assemblies into reflection only context
            foreach (var file in l.EnumerateAssemblyFilesInBinDirectory())
            {
                Assembly assem;
                var result = PackageLoader.TryReflectionOnlyLoadFrom(file, out assem);

                if (result == AssemblyLoadingState.Success || result == AssemblyLoadingState.AlreadyLoaded)
                {
                    var isNodeLibrary = nodeLibraryNames == null || nodeLibraryNames.Contains(assem.FullName);
                    vm.PublishCompCefHelper.Assemblies.Add(new PackageAssembly()
                    {
                        IsNodeLibrary = isNodeLibrary,
                        Assembly = assem
                    });
                    vm.PublishCompCefHelper.FilesToUpload.Add(assem.FullName);
                }
                else
                {
                    // if it's not a .NET assembly, we load it as an additional file
                    vm.PublishCompCefHelper.AdditionalFiles.Add(file);
                    vm.PublishCompCefHelper.FilesToUpload.Add(file);
                }
            }

            if (l.VersionName == null) return vm;

            var parts = l.VersionName.Split('.');
            if (parts.Count() != 3) return vm;

            vm.PublishCompCefHelper.MajorVersion = parts[0];
            vm.PublishCompCefHelper.MinorVersion = parts[1];
            vm.PublishCompCefHelper.BuildVersion = parts[2];
            vm.PublishCompCefHelper.version = l.VersionName;
            vm.PublishCompCefHelper.PublishPackageData.AssetID = l.AssetID;
            vm.PublishCompCefHelper.AssetID = l.AssetID;
            return vm;
        }

        public void PublishLocally()
        {
            var publishPath = GetPublishFolder();
            if (string.IsNullOrEmpty(publishPath))
                return;

            var files = BuildPackage();

            try
            {
                //if buildPackage() returns no files then the package
                //is empty so we should return
                if (files == null || files.Count() < 1)
                {
                    return;
                }
                var script = string.Format("window['message'] = 'Copying'");
                CefBrowser.GetMainFrame().ExecuteJavaScriptAsync(script);
                
                // begin publishing to local directory
                var remapper = new CustomNodePathRemapper(dynamoViewModel.Model.CustomNodeManager,
                    DynamoModel.IsTestMode);
                var builder = new PackageDirectoryBuilder(new MutatingFileSystem(), remapper);
                builder.BuildDirectory(PublishPackageData, publishPath, files);

                script = string.Format("window['message'] = 'Published Locally'");
                CefBrowser.GetMainFrame().ExecuteJavaScriptAsync(script);
            }
            catch (Exception e)
            {
                //ErrorString = e.Message;
                dynamoViewModel.Model.Logger.Log(e);
            }
            finally
            {
                //Uploading = false;
            }
        }

        private string GetPublishFolder()
        {
            var pathManager = dynamoViewModel.Model.PathManager as PathManager;
            var setting = dynamoViewModel.PreferenceSettings;

            var args = new PackagePathEventArgs
            {
                Path = pathManager.DefaultPackagesDirectory
            };

            OnRequestShowFileDialog(this, args);

            if (args.Cancel)
                return string.Empty;

            var folder = args.Path;

            if (!IsDirectoryWritable(folder))
            {
                //ErrorString = String.Format(Resources.FolderNotWritableError, folder);
                return string.Empty;
            }

            var pkgSubFolder = Path.Combine(folder, PathManager.PackagesDirectoryName);

            var index = pathManager.PackagesDirectories.IndexOf(folder);
            var subFolderIndex = pathManager.PackagesDirectories.IndexOf(pkgSubFolder);

            // This folder is not in the list of package folders.
            // Add it to the list as the default
            if (index == -1 && subFolderIndex == -1)
            {
                setting.CustomPackageFolders.Insert(0, folder);
            }
            else
            {
                // This folder has a package subfolder that is in the list.
                // Make the subfolder the default
                if (subFolderIndex != -1)
                {
                    index = subFolderIndex;
                    folder = pkgSubFolder;
                }

                var temp = setting.CustomPackageFolders[index];
                setting.CustomPackageFolders[index] = setting.CustomPackageFolders[0];
                setting.CustomPackageFolders[0] = temp;

            }

            return folder;
        }

        private bool IsDirectoryWritable(string dirPath, bool throwIfFails = false)
        {
            try
            {
                using (var fs = File.Create(
                    Path.Combine(
                        dirPath,
                        Path.GetRandomFileName()
                    ),
                    1,
                    FileOptions.DeleteOnClose)
                )
                { }
                return true;
            }
            catch
            {
                if (throwIfFails)
                    throw;
                else
                    return false;
            }
        }

        #region "Version Data Properties"
        public string version { get { return PublishPackageData.VersionName; } set { } }
        public string contents { get { return PublishPackageData.Contents; } }
        public bool contains_binaries { get { return PublishPackageData.ContainsBinaries; } }

        public string node_libraries
        {
            get
            {
                if (PublishPackageData.NodeLibraries != null && PublishPackageData.NodeLibraries.ToList().Count > 0)
                    return String.Join("|", PublishPackageData.NodeLibraries.Select(a => a.FullName).ToList());
                else
                    return string.Empty;
            }
        }
        public string package_dependencies
        {
            get
            {
                if (PublishPackageData.Dependencies != null && PublishPackageData.Dependencies.ToList().Count > 0)
                    return String.Join(",", PublishPackageData.Dependencies.Select(a => a.name + "|" + a.version).ToList());
                else
                    return string.Empty;
            }
        }
        #endregion

        #region "Publish Package Information"
        public string Name { get; set; }

        public string Group { get; set; }

        /// <summary>
        /// Description property </summary>
        /// <value>
        /// The description to be uploaded </value>
        public string Description { get; set; }

        /// <summary>
        /// Keywords property </summary>
        /// <value>
        /// A string of space-delimited keywords</value>
        private string _Keywords = "";
        public string Keywords
        {
            get { return _Keywords; }
            set
            {
                if (_Keywords != value)
                {
                    value = value.Replace(',', ' ').ToLower().Trim();
                    var options = RegexOptions.None;
                    var regex = new Regex(@"[ ]{2,}", options);
                    value = regex.Replace(value, @" ");

                    _Keywords = value;
                    KeywordList = value.Split(' ').Where(x => x.Length > 0).ToList();
                }
            }
        }

        /// <summary>
        /// KeywordList property </summary>
        /// <value>
        /// A list of keywords, usually produced by parsing Keywords</value>
        public List<string> KeywordList { get; set; }

        /// <summary>
        /// FullVersion property </summary>
        /// <value>
        /// The major, minor, and build version joined into one string</value>
        public string FullVersion
        {
            get { return MajorVersion + "." + MinorVersion + "." + BuildVersion; }
        }

        /// <summary>
        /// MinorVersion property </summary>
        /// <value>
        /// The second element of the version</value>
        private string _MinorVersion = "";
        public string MinorVersion
        {
            get { return _MinorVersion; }
            set
            {
                if (_MinorVersion != value)
                {
                    int val;
                    if (!Int32.TryParse(value, out val) || value == "") return;
                    if (value.Length != 1) value = value.TrimStart(new char[] { '0' });
                    _MinorVersion = value;

                }
            }
        }

        /// <summary>
        /// BuildVersion property </summary>
        /// <value>
        /// The third element of the version</value>
        private string _BuildVersion = "";
        public string BuildVersion
        {
            get { return _BuildVersion; }
            set
            {
                if (_BuildVersion != value)
                {
                    int val;
                    if (!Int32.TryParse(value, out val) || value == "") return;
                    if (value.Length != 1) value = value.TrimStart(new char[] { '0' });
                    _BuildVersion = value;
                }
            }
        }

        /// <summary>
        /// MajorVersion property </summary>
        /// <value>
        /// The first element of the version</value>
        private string _MajorVersion = "";
        public string MajorVersion
        {
            get { return _MajorVersion; }
            set
            {
                if (_MajorVersion != value)
                {
                    int val;
                    if (!Int32.TryParse(value, out val) || value == "") return;
                    if (value.Length != 1) value = value.TrimStart(new char[] { '0' });
                    _MajorVersion = value;
                }
            }
        }


        /// <summary>
        /// License property </summary>
        /// <value>
        /// The license for the package </value>
        private string _license = "";
        public string License
        {
            get { return _license; }
            set
            {
                if (_license != value)
                {
                    _license = value;
                }
            }
        }

        /// <summary>
        /// SiteUrl property </summary>
        /// <value>
        /// The website for the package</value>
        private string _siteUrl = "";
        public string SiteUrl
        {
            get { return _siteUrl; }
            set
            {
                if (_siteUrl != value)
                {
                    _siteUrl = value;
                }
            }
        }

        /// <summary>
        /// RepositoryUrl property </summary>
        /// <value>
        /// The repository url for the package</value>
        private string _repositoryUrl = "";
        public string RepositoryUrl
        {
            get { return _repositoryUrl; }
            set
            {
                if (_repositoryUrl != value)
                {
                    _repositoryUrl = value;
                }
            }
        }

        public string AssetID { get; set; }


        #endregion
    }
}
