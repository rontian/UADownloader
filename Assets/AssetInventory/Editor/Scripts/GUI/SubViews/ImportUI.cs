using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CodeStage.PackageToFolder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AssetInventory
{
    public sealed class ImportUI : EditorWindow
    {
        public static event Action OnImportDone;

        // IDs (foreignId field) of assets that do NOT support installation into a custom target folder
        // This is usually the case for assets that install files via custom scripts or have hardcoded paths
        // The list might not be complete, please report any missing ones to the developer
        private static readonly HashSet<int> _noCustomTargetFolderForeignIds = new HashSet<int>
        {
            267512, // Mighty Maps
            185959, // Task Atlas
            307257, // DevTasks - Offline Project Manager
            291626, // DevTrails - Developer Statistics Made Easy
            277112, // Clipper Pro - The Ultimate Clipboard
        };

        private List<AssetInfo> _assets;
        private List<AssetInfo> _missingPackages;
        private Action _callback;
        private Vector2 _scrollPos;
        private string _customFolder;
        private string _customFolderRel;
        private bool _running;
        private bool _cancellationRequested;
        private AddRequest _addRequest;
        private AssetInfo _curInfo;
        private int _assetPackageCount;
        private bool _unattended;
        private int _queueCount;
        private bool _interactive;
        private string _lockPref;

        public static ImportUI ShowWindow()
        {
            ImportUI window = GetWindow<ImportUI>("Import Wizard");
            window.minSize = new Vector2(450, 200);

            return window;
        }

        public void OnEnable()
        {
            AssetDatabase.importPackageStarted += ImportStarted;
            AssetDatabase.importPackageCompleted += ImportCompleted;
            AssetDatabase.importPackageCancelled += ImportCancelled;
            AssetDatabase.importPackageFailed += ImportFailed;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        public void OnDisable()
        {
            AssetDatabase.importPackageStarted -= ImportStarted;
            AssetDatabase.importPackageCompleted -= ImportCompleted;
            AssetDatabase.importPackageCancelled -= ImportCancelled;
            AssetDatabase.importPackageFailed -= ImportFailed;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
        }

        private void OnBeforeAssemblyReload()
        {
            // right now not any state to persist actually, Unity will serialize the whole view correctly
        }

        private void OnAfterAssemblyReload()
        {
            if (_running)
            {
                // means there was an import active which triggered a recompile, so let's continue
                BulkImportAssets(_interactive, false);
            }
        }

        public void Init(List<AssetInfo> assets, bool unattended = false, Action callback = null, bool noCustomFolder = false, string lockPref = null)
        {
            _callback = callback;
            _unattended = unattended;
            _lockPref = lockPref;

            _assets = assets.Where(a => a.ParentId == 0)
                .OrderByDescending(a => a.AssetSource).ThenBy(a => a.GetDisplayName())
                .ToArray().ToList(); // break direct reference so that package list refresh does not clear import state

            // check if only sub-packages were selected, this is a valid scenario
            if (_assets.Count == 0)
            {
                _assets = assets.Where(a => a.ParentId > 0)
                    .OrderByDescending(a => a.AssetSource).ThenBy(a => a.GetDisplayName())
                    .ToArray().ToList(); // break direct reference so that package list refresh does not clear import state
            }
            _assetPackageCount = _assets.Count(info => info.AssetSource != Asset.Source.RegistryPackage);

            if (noCustomFolder)
            {
                ClearCustomFolder();
            }
            else
            {
                // use configured target folder from settings if set
                if (AI.Config.importDestination == 2 && !string.IsNullOrWhiteSpace(AI.Config.importFolder))
                {
                    _customFolderRel = AI.Config.importFolder;
                    _customFolder = Application.dataPath + _customFolderRel.Substring("Assets".Length);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(_customFolder))
                    {
                        _customFolderRel = IOUtils.MakeProjectRelative(_customFolder);
                    }
                }
            }

            // check for non-existing downloads first
            _missingPackages = new List<AssetInfo>();
            _queueCount = 0;
            foreach (AssetInfo info in _assets)
            {
                if (info.SafeName == Asset.NONE) continue;
                if (!info.IsDownloaded)
                {
                    info.ImportState = AssetInfo.ImportStateOptions.Missing;
                    _missingPackages.Add(info);
                }
                else
                {
                    info.ImportState = AssetInfo.ImportStateOptions.Queued;
                    _queueCount++;
                }
            }

            if (_unattended) BulkImportAssets(false, false);
        }

        private void Update()
        {
            if (_assets == null) return;

            // refresh list after downloads finish
            foreach (AssetInfo info in _assets)
            {
                if (info.PackageDownloader == null) continue;
                if (info.ImportState == AssetInfo.ImportStateOptions.Missing)
                {
                    AssetDownloadState state = info.PackageDownloader.GetState();
                    switch (state.state)
                    {
                        case AssetDownloader.State.Downloaded:
                            info.Refresh();
                            Init(_assets);
                            break;
                    }
                }
            }
        }

        private void ImportFailed(string packageName, string errorMessage)
        {
            AssetInfo info = FindAsset(packageName);
            if (info == null) return;

            info.ImportState = AssetInfo.ImportStateOptions.Failed;
            _assets.First(a => a.AssetId == info.AssetId).ImportState = info.ImportState;

            Debug.LogError($"Import of '{packageName}' failed: {errorMessage}");
        }

        private void ImportCancelled(string packageName)
        {
            AssetInfo info = FindAsset(packageName);
            if (info == null) return;

            info.ImportState = AssetInfo.ImportStateOptions.Cancelled;
            _assets.First(a => a.AssetId == info.AssetId).ImportState = info.ImportState;
        }

        private void ImportCompleted(string packageName)
        {
            AssetInfo info = FindAsset(packageName);
            if (info == null)
            {
                // Unity 2023+ will return an empty packageName for some reason
                // since we can assume only one import happens at a time, we can just mark the current importing one as done
                info = _assets.FirstOrDefault(a => a.ImportState == AssetInfo.ImportStateOptions.Importing);
                if (info == null) return;
            }

            info.ImportState = AssetInfo.ImportStateOptions.Imported;
            _assets.First(a => a.AssetId == info.AssetId).ImportState = info.ImportState;
        }

        private void ImportStarted(string packageName)
        {
            AssetInfo info = FindAsset(packageName);
            if (info == null) return;

            info.ImportState = AssetInfo.ImportStateOptions.Importing;
            _assets.First(a => a.AssetId == info.AssetId).ImportState = info.ImportState;
        }

        private AssetInfo FindAsset(string packageName)
        {
            return _assets?.Find(info => info.SafeName == packageName || info.GetLocation(true) == packageName + ".unitypackage" || info.GetLocation(true) == packageName);
        }

        public void OnGUI()
        {
            EditorGUILayout.Space();
            if (_assets == null || _assets.Count == 0)
            {
                EditorGUILayout.HelpBox("Select packages in the Asset Inventory for importing first.", MessageType.Info);
                return;
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel, GUILayout.Width(85));
            EditorGUILayout.LabelField(_assets.Count.ToString());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Folder", EditorStyles.boldLabel, GUILayout.Width(85));
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(_customFolderRel) ? "-default-" : _customFolderRel, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Select...", GUILayout.ExpandWidth(false))) SelectTargetFolder();
            if (!string.IsNullOrWhiteSpace(_customFolder) && GUILayout.Button("Clear", GUILayout.ExpandWidth(false)))
            {
                ClearCustomFolder();
            }
            GUILayout.EndHorizontal();

            // Hint if some items do not support custom folders
            if (!string.IsNullOrWhiteSpace(_customFolderRel) && _assets.Any(a => IsCustomFolderUnsupported(a)))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox("Some selected items do not support custom target folders and will be installed to the default location.", MessageType.Info);
            }

            if (_missingPackages.Count > 0)
            {
                EditorGUILayout.Space();
                if (_queueCount > 0)
                {
                    EditorGUILayout.HelpBox($"{_missingPackages.Count} packages have not been downloaded yet and will be skipped.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("The packages have not been downloaded yet. No import possible until done so.", MessageType.Warning);
                }
            }

            EditorGUILayout.Space(10);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
            bool gatheringVersions = false;
            foreach (AssetInfo info in _assets)
            {
                if (info.SafeName == Asset.NONE) continue;

                GUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Toggle(info.IsDownloaded, GUILayout.Width(20));
                EditorGUI.EndDisabledGroup();
                if (info.AssetSource == Asset.Source.RegistryPackage)
                {
                    if (info.TargetPackageVersion() != null)
                    {
                        EditorGUILayout.LabelField(new GUIContent($"{info.GetDisplayName()} - {info.TargetPackageVersion()}", info.SafeName));
                    }
                    else
                    {
                        EditorGUILayout.LabelField(new GUIContent($"{info.GetDisplayName()} - checking", info.SafeName));
                        gatheringVersions = true;
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(new GUIContent(info.GetDisplayName(), info.GetLocation(true)));
                }
                
                GUILayout.FlexibleSpace();
                if (info.ImportState == AssetInfo.ImportStateOptions.Missing)
                {
                    if (info.IsAbandoned)
                    {
                        EditorGUILayout.LabelField(UIStyles.Content("Unavailable", "Package got disabled on the Asset Store and is no longer available for download."), GUILayout.Width(80));
                    }
                    else
                    {
                        AI.GetObserver().Attach(info);
                        AssetDownloadState state = info.PackageDownloader.GetState();
                        switch (state.state)
                        {
                            case AssetDownloader.State.Unavailable:
                                if (info.PackageDownloader.IsDownloadSupported() && GUILayout.Button("Download", GUILayout.Width(80)))
                                {
                                    info.PackageDownloader.Download(true);
                                }
                                break;

                            case AssetDownloader.State.Downloading:
                                EditorGUILayout.LabelField(Mathf.RoundToInt(state.progress * 100f) + "%", GUILayout.Width(80));
                                break;
                        }
                    }
                }
                else
                {
                    // Mark items with warning icon when custom target folder is selected but unsupported
                    if (!string.IsNullOrWhiteSpace(_customFolderRel) && IsCustomFolderUnsupported(info))
                    {
                        GUILayout.Label(EditorGUIUtility.IconContent("console.warnicon@2x", "|This package does not support installation into a custom target folder. It will be installed to the default location."), GUILayout.Width(20), GUILayout.Height(20));
                    }                
                    EditorGUILayout.LabelField(info.ImportState.ToString(), GUILayout.Width(70));
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            EditorGUILayout.Space();

            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_running || gatheringVersions);
            if (GUILayout.Button(UIStyles.Content("Import Automatically", "Import without any further interaction or confirmation"), UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                BulkImportAssets(false, true);
            }
            EditorGUI.BeginDisabledGroup(_assetPackageCount == 0);
            if (GUILayout.Button(UIStyles.Content("Import Interactive...", "Open the Unity import wizard for each asset to be imported, allowing to fine-tune each import"), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                BulkImportAssets(true, true);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.EndDisabledGroup();
            if (_running && GUILayout.Button("Cancel All", GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                _cancellationRequested = true; // will not always work if there was a recompile in between
                _running = false;
            }
            GUILayout.EndHorizontal();
        }

        private void ClearCustomFolder()
        {
            _customFolder = null;
            _customFolderRel = null;
        }

        private void SelectTargetFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("Select target folder in your project", _customFolder, "");
            if (string.IsNullOrEmpty(folder)) return;

            if (folder.Replace("\\", "/").ToLowerInvariant().StartsWith(Application.dataPath.Replace("\\", "/").ToLowerInvariant()))
            {
                _customFolder = folder;
                _customFolderRel = IOUtils.MakeProjectRelative(folder);
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "The target folder must be inside your current Unity project.", "OK");
            }
        }

        private async void BulkImportAssets(bool interactive, bool resetState)
        {
            _interactive = interactive;
            if (!string.IsNullOrWhiteSpace(_lockPref)) EditorPrefs.SetBool(_lockPref, true);

            if (resetState)
            {
                _assets
                    .Where(a => a.ImportState == AssetInfo.ImportStateOptions.Cancelled || a.ImportState == AssetInfo.ImportStateOptions.Failed)
                    .ForEach(a => a.ImportState = AssetInfo.ImportStateOptions.Queued);
            }

            // importing will be set if there was a recompile during an ongoing import
            IEnumerable<AssetInfo> importQueue = _assets.Where(a => a.ImportState == AssetInfo.ImportStateOptions.Queued || a.ImportState == AssetInfo.ImportStateOptions.Importing)
                .Where(a => a.SafeName != Asset.NONE)
                .Where(a => a.IsDownloaded).ToList();

            bool allDone;
            if (importQueue.Any())
            {
                _running = true;
                _cancellationRequested = false;

                if (!string.IsNullOrWhiteSpace(_customFolder))
                {
                    _customFolderRel = IOUtils.MakeProjectRelative(_customFolder);
                    Directory.CreateDirectory(_customFolder);
                }

                if (interactive)
                {
                    // phase 1: all that can be imported in one go (registry, archives)
                    await DoBulkImport(importQueue.Where(a => a.AssetSource == Asset.Source.Archive || a.AssetSource == Asset.Source.RegistryPackage), false, false);

                    // phase 2: all the remaining
                    await DoBulkImport(importQueue.Where(a => a.AssetSource != Asset.Source.Archive && a.AssetSource != Asset.Source.RegistryPackage), true, false);
                }
                else
                {
                    await DoBulkImport(importQueue, false, true);
                }
                allDone = importQueue.All(a => a.ImportState == AssetInfo.ImportStateOptions.Imported);
                _running = false;
            }
            else
            {
                allDone = true;
            }

            // TODO: check if there are support packages and import those
            if (AI.Config.convertToPipeline) AI.RunURPConverter();

            OnImportDone?.Invoke();

            // custom one-time callback handler
            _callback?.Invoke();
            _callback = null;
            if (!string.IsNullOrWhiteSpace(_lockPref)) EditorPrefs.DeleteKey(_lockPref);

            if (_unattended || allDone) Close();
        }

        private async Task DoBulkImport(IEnumerable<AssetInfo> queue, bool interactive, bool allAutomatic)
        {
            if (!interactive) AssetDatabase.StartAssetEditing(); // will cause progress UI to stay on top and not close anymore if used in interactive
            try
            {
                foreach (AssetInfo info in queue)
                {
                    _curInfo = info;

                    if (info.ImportState != AssetInfo.ImportStateOptions.Importing || !interactive)
                    {
                        info.ImportState = AssetInfo.ImportStateOptions.Importing;

                        string archivePath = await info.GetLocation(true, true);
                        if (info.AssetSource == Asset.Source.RegistryPackage)
                        {
                            _addRequest = ImportPackage(info, info.TargetPackageVersion());
                            if (_addRequest == null) continue;

                            EditorApplication.update += AddProgress;
                        }
                        else if (info.AssetSource == Asset.Source.Archive)
                        {
#if UNITY_2021_2_OR_NEWER
                            // extract directly to target folder
                            string relFolder = _customFolderRel;
                            if (!string.IsNullOrWhiteSpace(relFolder) && IsCustomFolderUnsupported(info)) relFolder = null;
                            string targetPath = Path.Combine(relFolder ?? "Assets", info.GetDisplayName());
                            await Task.Run(() => IOUtils.ExtractArchive(archivePath, targetPath));
                            info.ImportState = Directory.Exists(targetPath) ? AssetInfo.ImportStateOptions.Imported : AssetInfo.ImportStateOptions.Failed;
#else
                        info.ImportState = AssetInfo.ImportStateOptions.Failed;
#endif
                        }
                        else
                        {
                            object[] files = GetPackageAssetList(archivePath, out Type contentType);
                            if (interactive)
                            {
                                // check if there are changes at all since otherwise dialog will stay and not throw events
                                if (!PackageHasChanges(archivePath, files, contentType))
                                {
                                    info.ImportState = AssetInfo.ImportStateOptions.Imported;
                                    continue;
                                }
                            }

                            string actualRelFolder = _customFolderRel;
                            if (!string.IsNullOrWhiteSpace(actualRelFolder))
                            {
                                // Do not override path for packages that don't support custom folders
                                if (IsCustomFolderUnsupported(info))
                                {
                                    actualRelFolder = null;
                                }
                                else
                                {
                                    // check if any item already exists in the project, as that will most likely make rewriting the path fail
                                    bool updateMode = false;
                                    foreach (object item in files)
                                    {
                                        bool exists = (bool)contentType.GetField("exists").GetValue(item);
                                        if (exists)
                                        {
                                            actualRelFolder = null;
                                            updateMode = true;
                                            break;
                                        }
                                    }
                                    if (updateMode)
                                    {
                                        Debug.Log("Parts of the package are already imported. Skipping path change to custom location as that might cause invalid import directories to be created.");
                                    }
                                }
                            }

                            // launch directly or intercept package resolution to tweak paths
                            object assetOrigin = info.ToAsset().GetUnityAssetOrigin();
                            if (string.IsNullOrWhiteSpace(actualRelFolder))
                            {
                                AssetStore.ImportPackage(archivePath, interactive, assetOrigin);
                            }
                            else
                            {
                                // check if package has any dependencies
                                // automatic dialog will not pop up in case of custom import path override
                                bool regPackagesChanged = false;
                                Dictionary<string, string> dependencies = GetRegistryDependencies(files, contentType);
                                if (dependencies != null && dependencies.Count > 0)
                                {
                                    foreach (KeyValuePair<string, string> dep in dependencies)
                                    {
                                        Debug.Log($"Adding package dependency '{dep.Key}@{dep.Value}' for '{info}'");

                                        _addRequest = Client.Add($"{dep.Key}@{dep.Value}");
                                        if (_addRequest == null) continue;

                                        while (!_addRequest.IsCompleted) await Task.Delay(25);
                                        if (_addRequest.Status == StatusCode.Success) regPackagesChanged = true;
                                    }
                                }

                                Package2Folder.ImportPackageToFolder(archivePath, actualRelFolder, interactive, assetOrigin);

                                if (regPackagesChanged)
                                {
#if UNITY_2020_3_OR_NEWER
                                    Client.Resolve(); // only needed if registry packages were imported during asset packages import as dependencies
#endif
                                }
                            }
                        }
                    }

                    // wait until done
                    while (!_cancellationRequested && info.ImportState == AssetInfo.ImportStateOptions.Importing)
                    {
                        await Task.Delay(25);
                    }

                    if (info.ImportState == AssetInfo.ImportStateOptions.Importing) info.ImportState = AssetInfo.ImportStateOptions.Queued;
                    if (_cancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error importing packages: {e.Message}");
            }

            // handle potentially pending imports and put them back in the queue
            _assets.ForEach(info =>
            {
                if (info.ImportState == AssetInfo.ImportStateOptions.Importing) info.ImportState = AssetInfo.ImportStateOptions.Queued;
            });

            if (!interactive) AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
#if UNITY_2020_3_OR_NEWER
            Client.Resolve();
#endif

            // wait for all processes to finish
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                await Task.Delay(25);
            }
        }

        private Dictionary<string, string> GetRegistryDependencies(object[] items, Type type)
        {
            if (items == null) return null;

            // ignore registry packages
            Regex regex = new Regex(@"^Assets/[^/]+/package\.json$", RegexOptions.Compiled);

            for (int i = 0; i < items.Length; i++)
            {
                string path = (string)type.GetField("exportedAssetPath").GetValue(items[i]);

                // performance check against obvious mismatches
                if (string.IsNullOrWhiteSpace(path) || path.Length < 20 || path[0] != 'A') continue;

                if (regex.IsMatch(path))
                {
                    string sourceFolder = (string)type.GetField("sourceFolder").GetValue(items[i]);
                    string sourceFile = Path.Combine(sourceFolder, "asset");
                    Package package = JsonConvert.DeserializeObject<Package>(File.ReadAllText(sourceFile));

                    return package?.dependencies;
                }
            }

            return null;
        }

        private bool PackageHasChanges(string packageFile, object[] items, Type type)
        {
            try
            {
                if (items == null || CountPackageChanges(items, type) == 0)
                {
                    Debug.Log($"No changes detected for '{packageFile}', skipping import.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not determine import state of '{packageFile}', proceeding with import: {e.Message}");
            }

            return true;
        }

        private static object[] GetPackageAssetList(string packageFile, out Type contentType)
        {
            object[] items = null;

            Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
            Type packageUtility = assembly.GetType("UnityEditor.PackageUtility");
            MethodInfo extractAndPrepareAssetList = packageUtility.GetMethod("ExtractAndPrepareAssetList", BindingFlags.Public | BindingFlags.Static);
            object itemsObj = extractAndPrepareAssetList?.Invoke(null, new object[] {packageFile, null, null});
            if (itemsObj != null) items = (object[])itemsObj;
            contentType = assembly.GetType("UnityEditor.ImportPackageItem");

            return items;
        }

        private int CountPackageChanges(object[] items, Type type)
        {
            if (items.Length == 0) return 0;

            int result = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (!(bool)type.GetField("isFolder").GetValue(items[i]) && (bool)type.GetField("assetChanged").GetValue(items[i])) result++;
            }

            return result;
        }

        private static bool IsCustomFolderUnsupported(AssetInfo info)
        {
            if (info == null) return false;
            if (info.ForeignId <= 0) return false;
            return _noCustomTargetFolderForeignIds.Contains(info.ForeignId);
        }

        private static AddRequest ImportPackage(AssetInfo info, string version)
        {
            AddRequest result;
            AddRegistry(info.Registry);
            switch (info.PackageSource)
            {
                case PackageSource.Git:
                    Repository repo = JsonConvert.DeserializeObject<Repository>(info.Repository);
                    if (repo == null)
                    {
                        Debug.LogError($"Repository for {info} is not maintained.");
                        return null;
                    }
                    if (string.IsNullOrWhiteSpace(repo.revision))
                    {
                        result = Client.Add($"{repo.url}");
                    }
                    else
                    {
                        result = Client.Add($"{repo.url}#{repo.revision}");
                    }
                    break;

                case PackageSource.Local:
                case PackageSource.LocalTarball:
                    result = Client.Add($"file:{info.GetLocation(true)}");
                    break;

                default:
                    result = Client.Add($"{info.SafeName}@{version}");
                    break;
            }

            return result;
        }

        private static void AddRegistry(string registry)
        {
            if (string.IsNullOrEmpty(registry)) return;
            if (registry == Asset.UNITY_REGISTRY) return;
            ScopedRegistry sr = JsonConvert.DeserializeObject<ScopedRegistry>(registry);
            if (sr == null) return;

            string manifestFile = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");

            // do direct JSON manipulation instead of typed approach to be future-safe and don't accidentally remove other data
            JObject content = JObject.Parse(File.ReadAllText(manifestFile));
            JArray registries = (JArray)content["scopedRegistries"];
            if (registries == null)
            {
                registries = new JArray();
                content["scopedRegistries"] = registries;
            }

            // do nothing if already existent
            if (registries.Any(r => r["name"]?.Value<string>() == sr.name && r["url"]?.Value<string>() == sr.url)) return;

            registries.Add(JToken.FromObject(sr));

            File.WriteAllText(manifestFile, content.ToString());
        }

        private void AddProgress()
        {
            if (!_addRequest.IsCompleted) return;

            EditorApplication.update -= AddProgress;

            if (_addRequest.Status == StatusCode.Success)
            {
                _curInfo.ImportState = AssetInfo.ImportStateOptions.Imported;
            }
            else
            {
                _curInfo.ImportState = AssetInfo.ImportStateOptions.Failed;
                Debug.LogError($"Importing {_curInfo} failed: {_addRequest.Error.message}");
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}