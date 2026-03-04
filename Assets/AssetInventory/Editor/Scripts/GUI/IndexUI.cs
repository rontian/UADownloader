using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
#if UNITY_2020_1_OR_NEWER
using UnityEditor.PackageManager;
#endif
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AssetInventory
{
    public partial class IndexUI : BasicEditorUI
    {
        private const float CHECK_INTERVAL = 5;

        private readonly Dictionary<string, string> _staticPreviews = new Dictionary<string, string>
        {
            {"cs", "cs Script Icon"},
            {"php", "TextAsset Icon"},
            {"cg", "TextAsset Icon"},
            {"cginc", "TextAsset Icon"},
            {"js", "d_Js Script Icon"},
            {"prefab", "d_Prefab Icon"},
            {"png", "d_RawImage Icon"},
            {"jpg", "d_RawImage Icon"},
            {"gif", "d_RawImage Icon"},
            {"tga", "d_RawImage Icon"},
            {"tiff", "d_RawImage Icon"},
            {"ico", "d_RawImage Icon"},
            {"bmp", "d_RawImage Icon"},
            {"fbx", "d_PrefabModel Icon"},
            {"dll", "dll Script Icon"},
            {"meta", "MetaFile Icon"},
            {"unity", "d_SceneAsset Icon"},
            {"asset", "EditorSettings Icon"},
            {"txt", "TextScriptImporter Icon"},
            {"md", "TextScriptImporter Icon"},
            {"doc", "TextScriptImporter Icon"},
            {"docx", "TextScriptImporter Icon"},
            {"pdf", "TextScriptImporter Icon"},
            {"rtf", "TextScriptImporter Icon"},
            {"readme", "TextScriptImporter Icon"},
            {"chm", "TextScriptImporter Icon"},
            {"compute", "ComputeShader Icon"},
            {"shader", "Shader Icon"},
            {"shadergraph", "Shader Icon"},
            {"shadersubgraph", "Shader Icon"},
            {"mat", "d_Material Icon"},
            {"wav", "AudioImporter Icon"},
            {"mp3", "AudioImporter Icon"},
            {"ogg", "AudioImporter Icon"},
            {"xml", "UxmlScript Icon"},
            {"html", "UxmlScript Icon"},
            {"uss", "UssScript Icon"},
            {"css", "StyleSheet Icon"},
            {"json", "StyleSheet Icon"},
            {"exr", "d_ReflectionProbe Icon"}
        };

        private enum ChangeImpact
        {
            None,
            ReadOnly,
            Write
        }

        internal static string[] assetFields =
        {
            "Asset/AssetRating", "Asset/AssetSource", "Asset/Backup", "Asset/BIRPCompatible", "Asset/CompatibilityInfo", "Asset/CurrentState", "Asset/CurrentSubState", "Asset/Description", "Asset/DisplayCategory", "Asset/DisplayName", "Asset/DisplayPublisher", "Asset/ETag", "Asset/Exclude",
            "Asset/FirstRelease", "Asset/ForeignId", "Asset/HDRPCompatible", "Asset/Hotness", "Asset/Hue", "Asset/Id", "Asset/IsHidden", "Asset/IsLatestVersion", "Asset/KeepExtracted", "Asset/KeyFeatures", "Asset/Keywords", "Asset/LastOnlineRefresh", "Asset/LastRelease", "Asset/LatestVersion",
            "Asset/License", "Asset/LicenseLocation", "Asset/Location", "Asset/OriginalLocation", "Asset/OriginalLocationKey", "Asset/PackageDependencies", "Asset/PackageSize", "Asset/PackageSource", "Asset/ParentId", "Asset/PriceCny", "Asset/PriceEur", "Asset/PriceUsd",
            "Asset/PublisherId", "Asset/PurchaseDate", "Asset/RatingCount", "Asset/Registry", "Asset/ReleaseNotes", "Asset/Repository", "Asset/Requirements", "Asset/Revision", "Asset/SafeCategory", "Asset/SafeName",
            "Asset/SafePublisher", "Asset/Slug", "Asset/SupportedUnityVersions", "Asset/UpdateStrategy", "Asset/UploadId", "Asset/URPCompatible", "Asset/UseAI", "Asset/Version",
            "AssetFile/AssetId", "AssetFile/FileName", "AssetFile/FileVersion", "AssetFile/FileStatus", "AssetFile/Guid", "AssetFile/Height", "AssetFile/Hue", "AssetFile/Id", "AssetFile/Length", "AssetFile/Path", "AssetFile/PreviewState", "AssetFile/Size", "AssetFile/SourcePath", "AssetFile/Type", "AssetFile/Width",
            "Tag/Color", "Tag/FromAssetStore", "Tag/Id", "Tag/Name",
            "TagAssignment/Id", "TagAssignment/TagId", "TagAssignment/TagTarget", "TagAssignment/TagTargetId"
        };

        private List<Tag> _tags;
        private string[] _assetNames;
        private string[] _tagNames;
        private string[] _publisherNames;
        private string[] _colorOptions;
        private string[] _categoryNames;
        private string[] _types;
        private string[] _resultSizes;
        private string[] _sortFields;
        private string[] _searchFields;
        private string[] _tileTitle;
        private string[] _dependencyOptions;
        private string[] _previewOptions;
        private string[] _doubleClickOptions;
        private string[] _packageSortOptions;
        private string[] _groupByOptions;
        private string[] _packageListingOptions;
        private string[] _imageTypeOptions;
        private GUIContent[] _packageListingOptionsShort;
        private GUIContent[] _packageViewOptions;
        private string[] _deprecationOptions;
        private string[] _srpOptions;
        private string[] _maintenanceOptions;
        private string[] _importDestinationOptions;
        private string[] _importStructureOptions;
        private string[] _assetCacheLocationOptions;
        private string[] _expertSearchFields;
        private string[] _currencyOptions;
        private string[] _logOptions;
        private string[] _blipOptions;
        private string[] _aiBackendOptions;

        private int _lastTab = -1;
        private string _newTag;
        private int _lastMainProgress;
        private string _importFolder;
        private bool _blockingInProgress;

        private string[] _pvSelection;
        private string _pvSelectedPath;
        private string _pvSelectedFolder;
        private bool _pvSelectionChanged;
        private List<AssetInfo> _pvSelectedAssets;
        private int _packageCount;
        private int _packageFileCount;
        private int _availablePackageUpdates;
        private int _activePackageDownloads;

        private int _purchasedAssetsCount;
        private List<AssetInfo> _assets;
        private int _indexedPackageCount;
        private int _indexablePackageCount;
        private int _aiPackageCount;
        private int _backupPackageCount;

        private static int _scriptsReloaded;
        private bool _requireAssetTreeRebuild;
        private bool _requireReportTreeRebuild;
        private ChangeImpact _requireLookupUpdate;
        private bool _requireSearchUpdate;
        private bool _requireSearchSelectionUpdate;
        private bool _searchSelectionChangedManually;
        private DateTime _lastCheck;
        private Rect _tagButtonRect;
        private Rect _tag2ButtonRect;
        private Rect _connectButtonRect;
        private bool _initDone;
        private bool _updateAvailable;
        private AssetDetails _onlineInfo;
        private bool _allowLogic;
        private Editor _previewEditor;

        private bool _searchHandlerAdded;
        private bool _selectionHandlerAdded;

        private void Awake()
        {
            Init();
        }

        private void Init()
        {
            if (_initDone) return;
            _initDone = true;

            _fixedSearchTypeIdx = -1;
            AI.Init();
            InitFolderControl();

            _blockingInProgress = false;

            if (_requireLookupUpdate == ChangeImpact.None) _requireLookupUpdate = ChangeImpact.ReadOnly;
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;

            _ = CheckForToolUpdates();
            _ = CheckForAssetUpdates();
        }

        private void OnEnable()
        {
            EditorApplication.update += UpdateLoop;
            AI.Actions.OnActionsDone += OnActionsDone;
            AI.Actions.OnActionsInitialized += OnActionsInitialized;
            AI.OnPackageImageLoaded += OnPackageImageLoaded;
            AI.OnPackagesUpdated += OnPackagesUpdated;
            Tagging.OnTagsChanged += OnTagsChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneOpened += OnSceneLoaded;
            ImportUI.OnImportDone += OnImportDone;
            RemovalUI.OnUninstallDone += OnImportDone;
            MaintenanceUI.OnMaintenanceDone += OnMaintenanceDone;
            UpgradeUtil.OnUpgradeDone += OnMaintenanceDone;
            AssetStore.OnPackageListUpdated += OnPackageListUpdated;
            AssetDatabase.importPackageCompleted += ImportCompleted;
            AssetDownloaderUtils.OnDownloadFinished += OnDownloadFinished;
#if UNITY_2020_1_OR_NEWER
            Events.registeredPackages += OnRegisteredPackages;
#endif
#if UNITY_6000_3_OR_NEWER
            DragAndDrop.AddDropHandlerV2(OnSceneDrop);
            DragAndDrop.AddDropHandlerV2(OnHierarchyDrop);
            DragAndDrop.AddDropHandlerV2(OnProjectBrowserDrop);
            DragAndDrop.AddDropHandlerV2(OnInspectorDrop);
#elif UNITY_2021_2_OR_NEWER
            DragAndDrop.AddDropHandler(OnSceneDrop);
            DragAndDrop.AddDropHandler(OnHierarchyDrop);
            DragAndDrop.AddDropHandler(OnProjectBrowserDrop);
            DragAndDrop.AddDropHandler(OnInspectorDrop);
#endif
            if (_usageCalculationInProgress && _usageCalculation == null) _usageCalculationInProgress = false; // process was interrupted
            _pvSelection = null;
            _initDone = false;

            AI.StopAudio();
            AssetStore.FillBufferOnDemand(true);
            if (!searchMode) SuggestOptimization();
            if (workspaceMode) InitWorkspace();

            // have to go through preliminary title as OnEnable is called before setting any additional properties
            if (!titleContent.text.Contains("Picker")) AI.StartCacheObserver(); // expensive operation, only do when UI is visible
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateLoop;
            AI.Actions.OnActionsDone -= OnActionsDone;
            AI.Actions.OnActionsInitialized -= OnActionsInitialized;
            AI.OnPackageImageLoaded -= OnPackageImageLoaded;
            AI.OnPackagesUpdated -= OnPackagesUpdated;
            Tagging.OnTagsChanged -= OnTagsChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.sceneOpened -= OnSceneLoaded;
            ImportUI.OnImportDone -= OnImportDone;
            RemovalUI.OnUninstallDone -= OnImportDone;
            MaintenanceUI.OnMaintenanceDone -= OnMaintenanceDone;
            UpgradeUtil.OnUpgradeDone -= OnMaintenanceDone;
            AssetStore.OnPackageListUpdated -= OnPackageListUpdated;
            AssetDatabase.importPackageCompleted -= ImportCompleted;
            AssetDownloaderUtils.OnDownloadFinished -= OnDownloadFinished;
#if UNITY_2020_1_OR_NEWER
            Events.registeredPackages -= OnRegisteredPackages;
#endif
#if UNITY_6000_3_OR_NEWER
            DragAndDrop.RemoveDropHandlerV2(OnSceneDrop);
            DragAndDrop.RemoveDropHandlerV2(OnHierarchyDrop);
            DragAndDrop.RemoveDropHandlerV2(OnProjectBrowserDrop);
            DragAndDrop.RemoveDropHandlerV2(OnInspectorDrop);
#elif UNITY_2021_2_OR_NEWER
            DragAndDrop.RemoveDropHandler(OnSceneDrop);
            DragAndDrop.RemoveDropHandler(OnHierarchyDrop);
            DragAndDrop.RemoveDropHandler(OnProjectBrowserDrop);
            DragAndDrop.RemoveDropHandler(OnInspectorDrop);
#endif
            AI.StopAudio();
            AI.StopCacheObserver();

            // delay as otherwise there will be Dispose() errors
            EditorApplication.delayCall += () =>
            {
                if (_previewEditor != null) DestroyImmediate(_previewEditor);
            };

            // Cancel any ongoing operations
            _textureLoading?.Cancel();
            _textureLoading2?.Cancel();
            _textureLoading3?.Cancel();

            // Dispose CancellationTokenSource objects to prevent memory leaks
            _textureLoading?.Dispose();
            _textureLoading2?.Dispose();
            _textureLoading3?.Dispose();
            _extraction?.Dispose();
        }

        private void UpdateLoop()
        {
            SearchUpdateLoop();
        }

        private void SuggestOptimization()
        {
            // check if last optimization (stored as "yyyy-MM-dd HH:mm:ss" string) was more than a month ago
            AppProperty lastOptimization = DBAdapter.DB.Find<AppProperty>("LastOptimization");
            if (lastOptimization == null || string.IsNullOrWhiteSpace(lastOptimization.Value) || !DateTime.TryParse(lastOptimization.Value, out DateTime lastOpt))
            {
                OptimizeDatabase(true);
                return;
            }
            if ((DateTime.Now - lastOpt).TotalDays < AI.Config.dbOptimizationPeriod) return;

            // check if last optimization request (stored as "yyyy-MM-dd HH:mm:ss" string) was more than a day ago
            AppProperty lastOptRequest = DBAdapter.DB.Find<AppProperty>("LastOptimizationRequest");
            if (lastOptRequest == null || (DateTime.TryParse(lastOptRequest.Value, out DateTime lastOptReq) && (DateTime.Now - lastOptReq).TotalDays > AI.Config.dbOptimizationReminderPeriod))
            {
                lastOptRequest = new AppProperty("LastOptimizationRequest", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                DBAdapter.DB.InsertOrReplace(lastOptRequest);

                if (EditorUtility.DisplayDialog("Asset Inventory Maintenance", "It is recommended to optimize the database regularly to ensure fast search results. Should it be done now?", "OK", "Not Now"))
                {
                    OptimizeDatabase();
                }
            }
        }

        private void OnPackagesUpdated()
        {
            _requireLookupUpdate = ChangeImpact.Write;
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;
        }

        private void OnMaintenanceDone()
        {
            _searches = null;
            _requireLookupUpdate = ChangeImpact.Write;
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;
        }

        private void OnDownloadFinished(int foreignId)
        {
            _requireAssetTreeRebuild = true;
            if (AI.Config.tab == 0 && _selectedEntry != null && _selectedEntry.ForeignId == foreignId)
            {
                _selectedEntry.Refresh();
                _selectedEntry.PackageDownloader?.RefreshState();
            }
        }

        private async void OnPackageImageLoaded(Asset asset)
        {
            AssetInfo info = _assets?.FirstOrDefault(a => a.Id == asset.Id);
            if (info == null) return;

            await AssetUtils.LoadPackageTexture(info);
            _requireAssetTreeRebuild = true;
        }

        private void OnSceneLoaded(Scene scene, OpenSceneMode mode)
        {
            // otherwise previews will be empty
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;
        }

        private void ImportCompleted(string packageName)
        {
            OnImportDone();
        }

#if UNITY_2020_1_OR_NEWER
        private void OnRegisteredPackages(PackageRegistrationEventArgs obj)
        {
            OnImportDone();
        }
#endif

        private void OnImportDone()
        {
            AssetStore.GatherProjectMetadata();

            _requireLookupUpdate = ChangeImpact.ReadOnly;
            _requireAssetTreeRebuild = true;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            AI.StopAudio();

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // will crash editor otherwise
                _textureLoading?.Cancel();
                _textureLoading2?.Cancel();
                _textureLoading3?.Cancel();
            }

            // UI will have lost all preview textures during play mode
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _requireSearchUpdate = true;
                _requireAssetTreeRebuild = true;
            }
        }

        private void ReloadLookups(bool force = true)
        {
            if (AI.DEBUG_MODE) Debug.LogWarning("Reload Lookups");

            _requireLookupUpdate = ChangeImpact.None;
            _resultSizes = new[] {"-all-", string.Empty, "10", "25", "50", "100", "250", "500", "1000", "1500", "2000", "2500", "3000", "4000", "5000"};
            _searchFields = new[] {"Asset Path", "File Name"};
            _sortFields = new[] {"Asset Path", "File Name", "Size", "Type", "Length", "Width", "Height", "Color", "Category", "Last Updated", "Rating", "#Reviews", string.Empty, "-unsorted (fast)-"};
            _packageSortOptions = Enum.GetNames(typeof (AssetTreeViewControl.Columns)).Select(StringUtils.CamelCaseToWords).ToArray();
            _groupByOptions = new[] {"-none-", string.Empty, "Category", "Publisher", "Tag", "State", "Location"};
            _colorOptions = new[] {"-all-", "matching"};
            _tileTitle = new[] {"-Intelligent-", string.Empty, "Asset Path", "File Name", "File Name without Extension", "AI Caption or File Name", string.Empty, "None"};
            _dependencyOptions = new[] {"-never-", "Upon Selection"};
            _previewOptions = new[] {"-all-", string.Empty, "Only With Preview", "Only Without Preview"};
            _doubleClickOptions = new[] {"-none-", string.Empty, "Import + Add to Scene", "Import", "Open"};
            _packageListingOptions = new[] {"-all-", "-all except registry packages-", "Only Asset Store Packages", "Only Registry Packages", "Only Custom Packages", "Only Media Folders", "Only Archives", "Only Asset Manager"};
            _packageListingOptionsShort = new[] {new GUIContent("All", ""), new GUIContent("No Reg", _packageListingOptions[1]), new GUIContent("Store", _packageListingOptions[2]), new GUIContent("Reg", _packageListingOptions[3]), new GUIContent("Cust", _packageListingOptions[4]), new GUIContent("Media", _packageListingOptions[5]), new GUIContent("Arch", _packageListingOptions[6]), new GUIContent("AM", _packageListingOptions[7])};
            _packageViewOptions = new[] {UIStyles.IconContent("VerticalLayoutGroup Icon", "d_VerticalLayoutGroup Icon", "|List"), UIStyles.IconContent("GridLayoutGroup Icon", "d_GridLayoutGroup Icon", "|Grid")};
            _deprecationOptions = new[] {"-all-", "Exclude Deprecated", "Show Only Deprecated"};
            _srpOptions = new[] {"-all-", "-current-", string.Empty, "BIRP", "URP", "HDRP"};
            _maintenanceOptions = new[] {"-all-", "Update Available", "Outdated in Unity Cache", "Disabled by Unity", "Custom Asset Store Link", "Indexed", "Not Indexed", "Custom Registry", "Downloaded", "Downloading", "Not Downloaded", "Duplicate", "Marked for Backup", "Not Marked for Backup", "Marked for AI", "Not Marked for AI", "Deleted", "Excluded", "With Sub-Packages", "Incompatible Packages", "Fixable Incompatibilities", "Unfixable Incompatibilities"};
            _importDestinationOptions = new[] {"Into Folder Selected in Project View", "Into Assets Root", "Into Specific Folder"};
            _importStructureOptions = new[] {"All Files Flat in Target Folder", "Keep Original Folder Structure"};
            _assetCacheLocationOptions = new[] {"Automatic", "Custom Folder"};
            _currencyOptions = new[] {"EUR", "USD", "CNY"};
            _logOptions = new[] {"Media Downloads", "Image Resizing", "Audio Parsing", "Package Parsing", "Custom Actions", "Preview Creation"};
            _blipOptions = new[] {"Small (1Gb)", "Large (1.8Gb)"};
            _aiBackendOptions = new[] {"Blip", "Ollama"};
            _imageTypeOptions = new List<string> {"-all-", string.Empty}.Concat(TextureNameSuggester.suffixPatterns.Keys.Select(StringUtils.CamelCaseToWords)).ToArray();
            _expertSearchFields = new List<string> {"-Add Field-", string.Empty}.Concat(assetFields).ToArray();

            UpdateStatistics(force);
            AssetStore.FillBufferOnDemand();

            _assetNames = AI.ExtractAssetNames(_assets, true);
            _publisherNames = AI.ExtractPublisherNames(_assets);
            _categoryNames = AI.ExtractCategoryNames(_assets);
            _tagNames = AI.ExtractTagNames(_tags);
            _purchasedAssetsCount = AI.CountPurchasedAssets(_assets);

            _types = AI.LoadTypes();
            if (!string.IsNullOrWhiteSpace(fixedSearchType))
            {
                _fixedSearchTypeIdx = Array.IndexOf(_types, fixedSearchType);
            }
        }

        [DidReloadScripts(2)]
        private static void DidReloadScripts()
        {
            _scriptsReloaded++;
        }

        public override void OnGUI()
        {
            base.OnGUI();

            if (_scriptsReloaded > 0)
            {
                _requireAssetTreeRebuild = true;
                _requireReportTreeRebuild = true;
                _requireSearchUpdate = true;
                _requireLookupUpdate = ChangeImpact.Write; // DateTime not serialized properly, so we have to reload everything
                _scriptsReloaded--;
                _calculatingFolderSizes = false;
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("The Asset Inventory is not available during play mode.", MessageType.Info);
                return;
            }

            _allowLogic = Event.current.type == EventType.Layout; // nothing must be changed during repaint

            if (DBAdapter.DBError != null)
            {
                EditorGUILayout.HelpBox("The database could not be opened. It is probably corrupted. If you just installed the tool this might be caused by the database being on a network drive where syncing did not work properly. Delete and retry is the best option in that case.", MessageType.Error);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Database Location: {DBAdapter.GetDBPath()}");
                if (GUILayout.Button("Open", GUILayout.ExpandWidth(false)))
                {
                    EditorUtility.RevealInFinder(DBAdapter.GetDBPath());
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Error", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"--> {DBAdapter.DBError}", EditorStyles.wordWrappedLabel);

                EditorGUILayout.Space();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Retry", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT), GUILayout.ExpandWidth(false)))
                {
                    DBAdapter.Close();
                    AI.ReInit();
                }
                if (GUILayout.Button("Delete Database & Retry", GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT), GUILayout.ExpandWidth(false)))
                {
                    DBAdapter.Close();
                    File.Delete(DBAdapter.GetDBPath());
                    AI.ReInit();
                }
                GUILayout.EndHorizontal();

                return;
            }

            if (AI.UICustomizationMode)
            {
                GUILayout.BeginHorizontal("box");
                EditorGUILayout.HelpBox("UI customization mode is active. Define which elements should be visible by default (green) and which only in advanced mode (red) when using the eye icon or holding CTRL. Yellow sections can be moved up and down.", MessageType.Warning);
                if (GUILayout.Button("Stop Customizing", UIStyles.mainButton, GUILayout.ExpandWidth(false)))
                {
                    AI.UICustomizationMode = false;
                }
                GUILayout.EndHorizontal();
            }

            Init(); // in some docking scenarios OnGUI is called before Awake

            // check for config errors
            if (AI.ConfigErrors.Count > 0)
            {
                EditorGUILayout.HelpBox("Configuration errors detected. These need to be fixed to proceed.", MessageType.Error);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Config Location: {AI.UsedConfigLocation}");
                if (GUILayout.Button("Open", GUILayout.ExpandWidth(false)))
                {
                    EditorUtility.RevealInFinder(AI.UsedConfigLocation);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Errors", EditorStyles.boldLabel);
                foreach (string error in AI.ConfigErrors)
                {
                    EditorGUILayout.LabelField($"--> {error}");
                }
                EditorGUILayout.Space();
                if (GUILayout.Button("Reload Settings", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT), GUILayout.ExpandWidth(false)))
                {
                    AI.ReInit();
                }
                return;
            }

            if (AI.UpgradeUtil.LongUpgradeRequired)
            {
                AI.UpgradeUtil.DrawUpgradeRequired();
                return;
            }

            if (_assets == null) UpdateStatistics(false);
            _importFolder = DetermineImportFolder();

            if (DragDropAvailable()) HandleDragDrop();

            if (_requireLookupUpdate != ChangeImpact.None || _resultSizes == null || _resultSizes.Length == 0)
            {
                ReloadLookups(_requireLookupUpdate == ChangeImpact.Write || _requireLookupUpdate == ChangeImpact.None);
            }
            if (_allowLogic)
            {
                if (_lastTileSizeChange != DateTime.MinValue && (DateTime.Now - _lastTileSizeChange).TotalMilliseconds > 300f)
                {
                    if (AI.Config.tileText == 0) _requireSearchUpdate = true; // only update search results if tile size influences displayed text
                    _lastTileSizeChange = DateTime.MinValue;
                }

                // don't perform more expensive checks every frame
                if ((DateTime.Now - _lastCheck).TotalSeconds > CHECK_INTERVAL)
                {
                    _availablePackageUpdates = _assets.Count(a => a.ParentId == 0 && a.IsUpdateAvailable(_assets, false));
                    _activePackageDownloads = AI.GetObserver().DownloadCount;
                    _lastCheck = DateTime.Now;
                }
            }

            // Check if we need to show setup view
            if (!AI.Config.wizardCompleted)
            {
                DrawSetupView();
                return;
            }

            bool isNewTab = false;
            if (!hideMainNavigation)
            {
                isNewTab = DrawToolbar();
                if (isNewTab) AI.StopAudio();
                EditorGUILayout.Space();
            }
            else
            {
                AI.Config.tab = 0;
            }

            if (string.IsNullOrEmpty(CloudProjectSettings.accessToken))
            {
                EditorGUILayout.HelpBox("Asset Store connectivity is currently not possible. Please restart Unity and make sure you are logged in in the Unity hub.", MessageType.Warning);
                EditorGUILayout.Space();
            }

            // centrally handle project view selections since used in multiple views
            CheckProjectViewSelection();

            switch (AI.Config.tab)
            {
                case 0:
                    DrawSearchTab();
                    if (_allowLogic)
                    {
                        if (_requireSearchUpdate && AI.Config.searchAutomatically)
                        {
                            if (!_searchHandlerAdded || EditorApplication.delayCall == null)
                            {
                                _searchHandlerAdded = true;
                                EditorApplication.delayCall += () => PerformSearch(_keepSearchResultPage);
                            }
                        }
                        if (_requireSearchSelectionUpdate)
                        {
                            if (!_selectionHandlerAdded || EditorApplication.delayCall == null)
                            {
                                _selectionHandlerAdded = true;
                                EditorApplication.delayCall += HandleSearchSelectionChanged;
                            }
                        }
                    }
                    break;

                case 1:
                    // will have lost asset tree on reload due to missing serialization
                    if (_requireAssetTreeRebuild) CreateAssetTree();
                    DrawPackagesTab();
                    break;

                case 2:
                    if (_requireReportTreeRebuild) CreateReportTree();
                    DrawReportingTab();
                    break;

                case 3:
                    if (isNewTab) EditorCoroutineUtility.StartCoroutineOwnerless(UpdateStatisticsDelayed());
                    DrawSettingsTab();
                    break;

                case 4:
                    DrawAboutTab();
                    break;
            }
        }

        private string DetermineImportFolder()
        {
            // determine import targets
            switch (AI.Config.importDestination)
            {
                case 0:
                    return _pvSelectedFolder;

                case 2:
                    return AI.Config.importFolder;

                default:
                    return "Assets";

            }
        }

        private void CheckProjectViewSelection()
        {
            if (_pvSelection != null && Selection.assetGUIDs != null && _pvSelection.SequenceEqual(Selection.assetGUIDs))
            {
                _pvSelectionChanged = false;
                return;
            }

            _pvSelection = Selection.assetGUIDs;
            string oldPvSelectedPath = _pvSelectedPath;
            _pvSelectedPath = null;
            if (_pvSelection != null && _pvSelection.Length > 0)
            {
                _pvSelectedPath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
                if (_pvSelectedPath.StartsWith("Packages"))
                {
                    _pvSelectedPath = null;
                    _pvSelectedFolder = null;
                }
                else
                {
                    _pvSelectedFolder = Directory.Exists(_pvSelectedPath) ? _pvSelectedPath : Path.GetDirectoryName(_pvSelectedPath);
                    if (!string.IsNullOrWhiteSpace(_pvSelectedFolder)) _pvSelectedFolder = _pvSelectedFolder.Replace('/', Path.DirectorySeparatorChar);
                }
            }
            _pvSelectionChanged = oldPvSelectedPath != _pvSelectedPath;
            if (_pvSelectionChanged) _pvSelectedAssets = null;
        }

        private bool DrawToolbar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            List<string> strings = new List<string>
            {
                "Search",
                "Packages",
                "Reporting",
                "Settings" + (AI.Actions.AnyActionsInProgress ? " (indexing)" : "")
            };
            AI.Config.tab = GUILayout.Toolbar(AI.Config.tab, strings.ToArray(), GUILayout.Height(32), GUILayout.MinWidth(500));

            bool newTab = EditorGUI.EndChangeCheck();
            if (newTab && !hideMainNavigation) AI.SaveConfig();

            GUILayout.FlexibleSpace();
            int iconSize = 18;
            if (_updateAvailable && _onlineInfo != null && GUILayout.Button(UIStyles.Content($"v{_onlineInfo.version.name} available!", $"Released {_onlineInfo.version.publishedDate}"), EditorStyles.linkLabel))
            {
                Application.OpenURL(AI.ASSET_STORE_LINK);
            }
            if (_activePackageDownloads > 0 && GUILayout.Button(EditorGUIUtility.IconContent("Loading", $"|{_activePackageDownloads} Downloads Active"), EditorStyles.label, GUILayout.Width(iconSize), GUILayout.Height(iconSize)))
            {
                AI.Config.tab = 1;
                _selectedMaintenance = MaintenanceOption.Downloading;
                _requireAssetTreeRebuild = true;
                _packageInspectorTab = 1;
                AI.SaveConfig();
            }
            UILine("toolbar.showupdates", () =>
            {
                if (_availablePackageUpdates > 0 && GUILayout.Button(UIStyles.IconContent("preAudioLoopOff", "Update-Available", $"|{_availablePackageUpdates} Updates Available"), EditorStyles.label, GUILayout.Width(iconSize), GUILayout.Height(iconSize)))
                {
                    ShowPackageMaintenance(MaintenanceOption.UpdateAvailable);
                }
            });
            UILine("toolbar.toggleadvanced", () =>
            {
                if (GUILayout.Button(UIStyles.IconContent(AI.Config.hideAdvanced ? "animationvisibilitytoggleoff" : "animationvisibilitytoggleon", AI.Config.hideAdvanced ? "d_animationvisibilitytoggleoff" : "d_animationvisibilitytoggleon", "|Visibility of Advanced Features" + (AI.Config.hideAdvanced ? " - Hold CTRL to show temporarily" : "")), EditorStyles.label, GUILayout.Width(iconSize), GUILayout.Height(iconSize)))
                {
                    AI.Config.hideAdvanced = !AI.Config.hideAdvanced;
                    AI.SaveConfig();
                }
            });
            UILine("toolbar.togglecustomization", () =>
            {
                if (GUILayout.Button(UIStyles.IconContent("CustomTool", "d_CustomTool", "|Toggle UI Customization"), EditorStyles.label, GUILayout.Width(iconSize), GUILayout.Height(iconSize)))
                {
                    AI.UICustomizationMode = !AI.UICustomizationMode;
                }
            });
            UILine("toolbar.toggleabout", () =>
            {
                if (GUILayout.Button(EditorGUIUtility.IconContent("_Help", "|About"), EditorStyles.label, GUILayout.Width(iconSize), GUILayout.Height(iconSize)))
                {
                    if (_lastTab >= 0)
                    {
                        AI.Config.tab = _lastTab;
                    }
                    else
                    {
                        _lastTab = AI.Config.tab;
                        AI.Config.tab = 4;
                    }
                }
            });
            if (AI.Config.tab < 4) _lastTab = -1;
            GUILayout.EndHorizontal();

            return newTab;
        }

        private void ShowPackageMaintenance(MaintenanceOption maintenanceOption)
        {
            AI.Config.tab = 1;
            _selectedMaintenance = maintenanceOption;
            _requireAssetTreeRebuild = true;
            _packageInspectorTab = 1;
            AI.SaveConfig();
        }

        private void ShowInterstitial()
        {
            if (EditorUtility.DisplayDialog("Your Support Counts", "This message will only appear once. Thanks for using Asset Inventory! I hope you enjoy using it.\n\n" +
                    "Developing a rather ground-braking asset like this as a solo-dev requires a huge amount of time and work.\n\n" +
                    "Please consider leaving a review and spreading the word. This is so important on the Asset Store and is the only way to make asset development viable.\n\n"
                    , "Leave Review", "Maybe Later"))
            {
                Application.OpenURL(AI.ASSET_STORE_LINK);
            }
        }

        private void GatherTreeChildren(int id, List<AssetInfo> result, TreeModel<AssetInfo> treeModel)
        {
            AssetInfo info = treeModel.Find(id);
            if (info == null) return;

            if (info.Id > 0) result.Add(info);
            if (info.HasChildren)
            {
                foreach (TreeElement subInfo in info.Children)
                {
                    GatherTreeChildren(subInfo.TreeId, result, treeModel);
                }
            }
        }

        private void HandleTagShortcuts()
        {
            if ((Event.current.modifiers & EventModifiers.Alt) != 0)
            {
                // Handle ALT+[0-9,a-z] shortcuts
                if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode keyCode = Event.current.keyCode;
                    string keyStr = keyCode.ToString().ToLower();

                    // Convert Alpha1-Alpha9 to 1-9
                    if (keyStr.StartsWith("alpha")) keyStr = keyStr.Substring(5);

                    // Only process single character keys (letters or numbers)
                    if (keyStr.Length == 1 && char.IsLetterOrDigit(keyStr[0]))
                    {
                        // Find tag with matching hotkey
                        List<Tag> tags = Tagging.LoadTags();
                        Tag matchingTag = tags.Find(t => t.Hotkey == keyStr);
                        if (matchingTag != null)
                        {
                            bool isRemoving = (Event.current.modifiers & EventModifiers.Shift) != 0;
                            if (isRemoving)
                            {
                                // Remove tag from all selected assets that have it
                                switch (AI.Config.tab)
                                {
                                    case 0:
                                        Tagging.RemoveAssetAssignments(_sgrid.selectionItems, matchingTag.Name, true);
                                        CalculateSearchBulkSelection();
                                        break;

                                    case 1:
                                        Tagging.RemovePackageAssignments(_selectedTreeAssets, matchingTag.Name, true);
                                        break;
                                }
                            }
                            else
                            {
                                // Add tag to all selected assets that don't have it
                                switch (AI.Config.tab)
                                {
                                    case 0:
                                        Tagging.AddAssignments(_sgrid.selectionItems, matchingTag.Name, TagAssignment.Target.Asset, true);
                                        CalculateSearchBulkSelection();
                                        break;

                                    case 1:
                                        Tagging.AddAssignments(_selectedTreeAssets, matchingTag.Name, TagAssignment.Target.Package, true);
                                        break;
                                }
                            }

                            _requireAssetTreeRebuild = true;
                            Event.current.Use();
                        }
                    }
                }
            }
        }

        private CancellationToken InitBlockingToken()
        {
            _blockingInProgress = true;
            InitBlocking();
            return _extraction.Token;
        }

        private void DisposeBlocking()
        {
            _extraction?.Dispose();
            _blockingInProgress = false;
        }

        private void InitBlocking()
        {
            if (_extraction != null && !_extraction.IsDisposed()) _extraction?.Cancel();
            _extraction = new CancellationTokenSource();
        }

        private async Task CheckForToolUpdates()
        {
            _updateAvailable = false;

            await Task.Delay(2000); // let remainder of window initialize first
            if (string.IsNullOrEmpty(CloudProjectSettings.accessToken)) return;

            _onlineInfo = await AssetStore.RetrieveAssetDetails(AI.ASSET_STORE_ID, null, true);
            if (_onlineInfo == null) return;

            _updateAvailable = new SemVer(_onlineInfo.version.name) > new SemVer(AI.VERSION);
        }

        private async Task CheckForAssetUpdates()
        {
            await Task.Delay(2500); // let remainder of window initialize first

            if (AI.Config.autoRefreshPurchases)
            {
                if (AI.Config.lastPurchasesUpdate != DateTime.MinValue && (DateTime.Now - AI.Config.lastPurchasesUpdate).TotalHours < AI.Config.purchasesRefreshPeriod)
                {
                    // no need to check again
                }
                else
                {
                    AI.Config.lastPurchasesUpdate = DateTime.Now;
                    AI.SaveConfig();

                    await AI.Actions.RunAction(ActionHandler.ACTION_ASSET_STORE_PURCHASES);
                }
            }

            if (AI.Config.autoRefreshMetadata)
            {
                if (AI.Config.lastMetadataUpdate != DateTime.MinValue && (DateTime.Now - AI.Config.lastMetadataUpdate).TotalHours < AI.Config.metadataTimeout)
                {
                    // no need to check again
                }
                else
                {
                    AI.Config.lastMetadataUpdate = DateTime.Now;
                    AI.SaveConfig();

                    await AI.Actions.RunAction(ActionHandler.ACTION_ASSET_STORE_DETAILS);
                }
            }
        }

        private void CreateDebugReport()
        {
            string reportFile = Path.Combine(AI.GetStorageFolder(), "DebugReport.log");
            File.WriteAllText(reportFile, AI.CreateDebugReport());
            EditorUtility.RevealInFinder(reportFile);
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}