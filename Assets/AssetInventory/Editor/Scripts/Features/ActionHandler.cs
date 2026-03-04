using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using static AssetInventory.UpdateAction;

namespace AssetInventory
{
    public class ActionHandler
    {
        public event Action OnActionsDone;
        public event Action OnActionsInitialized;

        public const string ACTION_DEFAULT_NAME = "-Default-";
        public const string ACTION_ASSET_STORE_PURCHASES = "AssetStorePurchases";
        public const string ACTION_ASSET_STORE_DETAILS = "AssetStoreDetails";
        public const string ACTION_ASSET_STORE_CACHE_SCAN = "AssetStoreCacheScan";
        public const string ACTION_ASSET_STORE_CACHE_INDEX = "AssetStoreCacheIndex";
        public const string ACTION_SUB_PACKAGES_INDEX = "IndexSubPackages";
        public const string ACTION_SUB_ARCHIVES_INDEX = "IndexSubArchives";
        public const string ACTION_PACKAGE_CACHE_INDEX = "PackageCacheIndex";
        public const string ACTION_FOLDERS_INDEX = "FoldersIndex";
        public const string ACTION_MEDIA_FOLDERS_INDEX = "MediaFoldersIndex";
        public const string ACTION_ARCHIVE_FOLDERS_INDEX = "ArchiveFoldersIndex";
        public const string ACTION_PACKAGE_FOLDERS_INDEX = "PackageFoldersIndex";
        public const string ACTION_DEVPACKAGE_FOLDERS_INDEX = "DevPackageFoldersIndex";
        public const string ACTION_ASSET_MANAGER_INDEX = "AssetManagerIndex";
        public const string ACTION_ASSET_MANAGER_COLLECTION_INDEX = "AssetManagerCollectionIndex";
        public const string ACTION_ASSET_STORE_DOWNLOADS = "AssetStoreDownloads";
        public const string ACTION_COLOR_INDEX = "ColorIndexer";
        public const string ACTION_BACKUP = "Backup";
        public const string ACTION_AI_CAPTIONS = "AICaptions";
        public const string ACTION_FIND_FREE = "ClaimFreeAssets";
        public const string ACTION_PREVIEWS_RECREATE = "RecreatePreviews";
        public const string ACTION_PREVIEWS_RESTORE = "RestorePreviews";
        public const string ACTION_USER = "UserAction-";

        public const string AI_ACTION_LOCK = AI.DEFINE_SYMBOL + "_ACTION_LOCK";

        private const string AI_ACTION_SETUP_DONE = AI.DEFINE_SYMBOL + "_SETUP_DONE";
        internal const string AI_ACTION_ACTIVE = AI.DEFINE_SYMBOL + "_ACTION_ACTIVE_";
        internal const string AI_CURRENT_STEP = AI.DEFINE_SYMBOL + "_CURRENT_STEP_";

        // global cancellation request
        public bool CancellationRequested { get; set; }

        public bool ActionsInProgress
        {
            get
            {
                return Actions.Any(a => (a.IsRunning() || a.scheduled) && !a.nonBlocking);
            }
        }
        public bool AnyActionsInProgress
        {
            get
            {
                return Actions.Any(a => a.IsRunning());
            }
        }
        public List<UpdateAction> Actions = new List<UpdateAction>();
        public List<CustomAction> UserActions = new List<CustomAction>();
        public List<ActionStep> ActionSteps = new List<ActionStep>();

        internal DateTime LastActionUpdate { get; private set; }

        private int _curState;
        private bool _initDone;

        public void Init(bool force = false)
        {
            if (_initDone && !force) return;
            _initDone = true;

            Actions = new List<UpdateAction>();
            EnsureStatesInitialized();

            Actions.Add(new UpdateAction {key = ACTION_ASSET_STORE_PURCHASES, name = "Fetch Asset Store Purchases", description = "Refreshes purchases from Unity Asset Store and adds these as packages (without indexing the content yet).", phase = Phase.Pre, nonBlocking = true});
            Actions.Add(new UpdateAction {key = ACTION_ASSET_STORE_DETAILS, name = "Fetch Asset Store Details", description = "Downloads metadata for packages in the index like publisher and pricing information as well as screenshots.", phase = Phase.Pre, supportsForce = true, allowParallel = true, nonBlocking = true});

            Actions.Add(new UpdateAction {key = ACTION_ASSET_STORE_CACHE_SCAN, name = "Scan Asset Store Cache", description = "Add found or changed packages to package catalog and queue without indexing the contents yet.", phase = Phase.Index, supportsForce = true, nonBlocking = true});
            Actions.Add(new UpdateAction {key = ACTION_ASSET_STORE_CACHE_INDEX, name = "Index Store Assets", description = "The main source for the asset index. Will scan the Unity Asset Store cache of already downloaded items and index these.", phase = Phase.Index});
            Actions.Add(new UpdateAction {key = ACTION_ASSET_STORE_DOWNLOADS, name = "Download & Index New Asset Store Packages", description = "Download uncached items from the Asset Store for indexing. Will delete them again afterwards if not selected otherwise below. Attention: downloading an item will revoke the right to easily return it through the Asset Store.", phase = Phase.Index});
            Actions.Add(new UpdateAction {key = ACTION_PACKAGE_CACHE_INDEX, name = "Index Package Cache", description = "Will index registry packages like from the Unity registry or custom registries and Github.", phase = Phase.Index});
            Actions.Add(new UpdateAction {key = ACTION_FOLDERS_INDEX, name = "Index Additional Folders", description = "Will scan all folders listed under additional folders for packages, media files and more to add to the index. Put all your texture and audio libraries as well as humble bundle, Synty and other assets there.", phase = Phase.Index});
            Actions.Add(new UpdateAction {key = ACTION_ASSET_MANAGER_INDEX, name = "Index Unity Asset Manager", description = "Activate if you have assets stored in Unity Asset Manager in the cloud to make them searchable as well.", phase = Phase.Index});

            Actions.Add(new UpdateAction {key = ACTION_COLOR_INDEX, name = "Extract Colors", description = "Will make assets searchable by color. Relies on existing preview images.", phase = Phase.Post, nonBlocking = true});
            Actions.Add(new UpdateAction {key = ACTION_BACKUP, name = "Create Backups", description = "Store downloaded assets in a separate folder", phase = Phase.Post, nonBlocking = true});
            Actions.Add(new UpdateAction {key = ACTION_AI_CAPTIONS, name = "Create AI Captions", description = "Will use AI to create an automatic caption of what is visible in each individual asset file using the existing preview images. Once indexed this will yield potentially much better search results.", phase = Phase.Post, nonBlocking = true});

            // custom actions created by user, must be created before hidden actions
            InitUserActions();

            // hidden actions, triggered manually or from other actions
            Actions.Add(new UpdateAction {key = ACTION_SUB_PACKAGES_INDEX, name = "Index Sub-Packages", phase = Phase.Index, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_SUB_ARCHIVES_INDEX, name = "Index Sub-Archives", phase = Phase.Index, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_PREVIEWS_RECREATE, name = "Recreate Previews", phase = Phase.Independent, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_PREVIEWS_RESTORE, name = "Restore Previews", phase = Phase.Independent, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_ASSET_MANAGER_COLLECTION_INDEX, name = "Index Unity Asset Manager Collections", phase = Phase.Index, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_PACKAGE_FOLDERS_INDEX, name = "Index Asset Packages in Folders", phase = Phase.Index, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_DEVPACKAGE_FOLDERS_INDEX, name = "Index Dev Package in Folders", phase = Phase.Index, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_ARCHIVE_FOLDERS_INDEX, name = "Index Archives in Folders", phase = Phase.Index, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_MEDIA_FOLDERS_INDEX, name = "Index Media in Folders", phase = Phase.Index, hidden = true});
            Actions.Add(new UpdateAction {key = ACTION_FIND_FREE, name = "Find Free Bundled Assets from already Purchased Assets", description = "Some asset authors, especially when purchasing bundles, grant free or cheaper access to related assets. This is not immediately visible in the store and assets can remain unclaimed. Run this action to get a list of candidates to check for free or reduced prices. Running forced will open all results in browser tabs.", phase = Phase.Independent, supportsForce = true, nonBlocking = true, hidden = true});

            AppProperty lastIndexUpdate = DBAdapter.DB.Find<AppProperty>("LastIndexUpdate");
            LastActionUpdate = lastIndexUpdate != null ? DateTime.Parse(lastIndexUpdate.Value, DateTimeFormatInfo.InvariantInfo) : DateTime.MinValue;

            OnActionsInitialized?.Invoke();
        }

        private void InitUserActions()
        {
            UserActions = DBAdapter.DB.Query<CustomAction>("select * from CustomAction order by Name");
            foreach (CustomAction action in UserActions)
            {
                Actions.Add(new UpdateAction {key = ACTION_USER + action.Id, name = action.Name, description = action.Description, phase = Phase.Independent});
            }

            ActionSteps = new List<ActionStep>();
            ActionSteps.Add(new CreateFolderStep());
            ActionSteps.Add(new MoveFolderStep());
            ActionSteps.Add(new DeleteFolderStep());
            ActionSteps.Add(new CopyFileStep());
            ActionSteps.Add(new MoveFileStep());
            ActionSteps.Add(new DeleteFileStep());
            ActionSteps.Add(new CompressFolderStep());
            ActionSteps.Add(new ExtractFolderStep());

            ActionSteps.Add(new InstallRegistryPackageByNameStep());
            ActionSteps.Add(new InstallRegistryPackageByPathStep());
            ActionSteps.Add(new InstallPackagesByTagStep());
            ActionSteps.Add(new UninstallRegistryPackageByNameStep());
            ActionSteps.Add(new UninstallFeatureByNameStep());
            ActionSteps.Add(new UpdateRegistryPackageByNameStep());
            ActionSteps.Add(new TMPStep());

            ActionSteps.Add(new HTMLExportStep());
            ActionSteps.Add(new FTPUploadStep());
            ActionSteps.Add(new FTPDeleteStep());

            ActionSteps.Add(new SetProjectPropertyStep());
            ActionSteps.Add(new AddDefineSymbolStep());
            ActionSteps.Add(new RemoveDefineSymbolStep());
            ActionSteps.Add(new AddCompilerArgumentStep());
            ActionSteps.Add(new RemoveCompilerArgumentStep());

            ActionSteps.Add(new RunActionStep());
            ActionSteps.Add(new RunProcessStep());
            ActionSteps.Add(new SetTextVariableStep());
            ActionSteps.Add(new DebugLogStep());
            ActionSteps.Add(new MessageDialogStep());
            ActionSteps.Add(new RestartEditorStep());

            CheckForInterruptedCustomActions();
            CheckForFirstStart();
        }

        private void CheckForFirstStart()
        {
            if (!EditorPrefs.GetBool(AI_ACTION_SETUP_DONE))
            {
                EditorPrefs.SetBool(AI_ACTION_SETUP_DONE, true);

                List<CustomAction> startup = UserActions
                    .Where(a => a.RunMode == CustomAction.Mode.AtInstallation)
                    .ToList();
                if (startup.Count > 0)
                {
                    if (EditorUtility.DisplayDialog("Project Setup",
                            "Asset Inventory was just installed into the project and is about to run the following custom actions which were marked to be run at installation:\n\n"
                            + string.Join("\n\n", startup.Select(a => "*" + a.Name + "*\n" + a.Description)),
                            "Run", "Skip"))
                    {
                        foreach (CustomAction action in startup)
                        {
                            _ = RunUserAction(action);
                        }
                    }
                }
            }
        }

        private async void CheckForInterruptedCustomActions()
        {
            foreach (CustomAction action in UserActions)
            {
                if (EditorPrefs.GetBool(AI_ACTION_ACTIVE + action.Id, false))
                {
                    if (AI.Config.LogCustomActions) Debug.Log($"Found interrupted custom action '{action.Name}'. Waiting for lock removal...");
                    while (EditorPrefs.GetBool(AI_ACTION_LOCK, false))
                    {
                        await Task.Delay(500);
                    }
                    if (AI.Config.LogCustomActions) Debug.Log($"... Done. Resuming custom action '{action.Name}'");

                    _ = RunUserAction(action);

                    return; // Only resume one action at a time to avoid potential conflicts
                }
            }
        }

        private void SetDefaultStates(int idx)
        {
            List<UpdateActionState> actionStates = new List<UpdateActionState>();
            actionStates.Add(new UpdateActionState {key = ACTION_ASSET_STORE_PURCHASES, enabled = true});
            actionStates.Add(new UpdateActionState {key = ACTION_ASSET_STORE_DETAILS, enabled = true});
            actionStates.Add(new UpdateActionState {key = ACTION_ASSET_STORE_CACHE_SCAN, enabled = true});
            actionStates.Add(new UpdateActionState {key = ACTION_ASSET_STORE_CACHE_INDEX, enabled = true});
            actionStates.Add(new UpdateActionState {key = ACTION_ASSET_STORE_DOWNLOADS, enabled = true});
            actionStates.Add(new UpdateActionState {key = ACTION_MEDIA_FOLDERS_INDEX, enabled = true});
            actionStates.Add(new UpdateActionState {key = ACTION_COLOR_INDEX, enabled = true});
            actionStates.Add(new UpdateActionState {key = ACTION_FOLDERS_INDEX, enabled = true});

            if (AI.Config.actionStates.Count == 0)
            {
                AI.Config.actionStates.Add(new UpdateActionStates {name = ACTION_DEFAULT_NAME, actions = actionStates});
            }
            else
            {
                AI.Config.actionStates[idx] = new UpdateActionStates {name = ACTION_DEFAULT_NAME, actions = actionStates};
            }
        }

        public List<UpdateAction> GetRunningActions()
        {
            return Actions.Where(a => a.progress != null && a.progress.Any(p => p.IsRunning()))
                .OrderBy(p => p.progress.Last().StartedAt)
                .ToList();
        }

        public DateTime GetFirstActionStart()
        {
            return GetRunningActions().Select(x => x.progress.First().StartedAt).Min();
        }

        public async Task RunAction(string action, bool force = false)
        {
            UpdateAction updateAction = Actions.FirstOrDefault(a => a.key == action);
            if (updateAction == null)
            {
                Debug.LogError($"Action '{action}' not found");
                return;
            }
            await RunAction(updateAction, force);
        }

        public async Task RunAction(UpdateAction action, bool force = false)
        {
            await RunActions(new List<UpdateAction> {action}, force);
        }

        public bool RegisterRunningAction(string action, ActionProgress runner, string caption = null)
        {
            UpdateAction updateAction = Actions.FirstOrDefault(a => a.key == action);
            if (updateAction == null)
            {
                Debug.LogError($"Action '{action}' not found");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(caption)) runner.WithProgress(caption);
            updateAction.progress.Add(runner);
            updateAction.MarkStarted();

            return true;
        }

        public async void RunActions(bool force = false)
        {
            if (!AI.Config.quickIndexingDone)
            {
                // no downloads on very first run to show quick results
                await RunActions(Actions
                    .Where(a => IsActive(a) && a.key != ACTION_ASSET_STORE_DOWNLOADS)
                    .ToList(), force);
            }
            else
            {
                await RunActions(Actions.Where(IsActive).ToList(), force);
            }
        }

        public async Task RunActions(List<UpdateAction> actions, bool force = false)
        {
            CancellationRequested = false;

            AI.Init();

            // refresh registry packages in parallel
            AssetStore.GatherAllMetadata();

            actions.ForEach(a => a.scheduled = true);

            foreach (UpdateAction action in actions)
            {
                if (CancellationRequested) break;
                if (action.hidden) continue; // hidden actions must be started individually
                if (action.IsRunning()) continue;
                action.MarkStarted();

                switch (action.key)
                {
                    case ACTION_ASSET_STORE_PURCHASES:
                        AssetStoreImporter assetStoreImporter = new AssetStoreImporter();
                        assetStoreImporter.WithProgress("Updating purchases");
                        action.progress.Add(assetStoreImporter);
                        AssetPurchases result = await assetStoreImporter.FetchOnlineAssets();
                        assetStoreImporter.FinishProgress();
                        if (result != null) AI.TriggerPackageRefresh();
                        break;

                    case ACTION_ASSET_STORE_DETAILS:
                        FetchAssetDetails(force, 0, false, action);
                        break;

                    case ACTION_ASSET_STORE_CACHE_SCAN:
                        // special handling for normal asset store assets since directory structure yields additional information
                        string assetDownloadCache = AI.GetAssetCacheFolder();
                        if (Directory.Exists(assetDownloadCache))
                        {
                            // check if forced local update is requested after upgrading
                            AppProperty forceLocalUpdate = DBAdapter.DB.Find<AppProperty>("ForceLocalUpdate");
                            if (forceLocalUpdate != null && forceLocalUpdate.Value.ToLowerInvariant() == "true")
                            {
                                force = true;
                                DBAdapter.DB.Delete<AppProperty>("ForceLocalUpdate");
                            }

                            UnityPackageImporter unityPackageImporter = new UnityPackageImporter();
                            unityPackageImporter.WithProgress("Scanning Unity cache");
                            action.progress.Add(unityPackageImporter);
                            await unityPackageImporter.IndexRoughLocal(new FolderSpec(assetDownloadCache), true, force);
                            unityPackageImporter.FinishProgress();
                        }
                        else
                        {
                            Debug.LogWarning($"Could not find the asset download folder: {assetDownloadCache}");
                            EditorUtility.DisplayDialog("Error",
                                $"Could not find the asset download folder: {assetDownloadCache}.\n\nEither nothing was downloaded yet through the Package Manager or you changed the Asset cache location. In the latter case, please configure the new location under Settings.",
                                "OK");
                        }
                        break;

                    case ACTION_ASSET_STORE_CACHE_INDEX:
                        UnityPackageImporter unityPackageImporter2 = new UnityPackageImporter();
                        unityPackageImporter2.WithProgress("Indexing Unity cache");
                        action.progress.Add(unityPackageImporter2);
                        await unityPackageImporter2.IndexDetails();
                        unityPackageImporter2.FinishProgress();
                        break;

                    case ACTION_PACKAGE_CACHE_INDEX:
                        string packageDownloadCache = AI.GetPackageCacheFolder();
                        if (Directory.Exists(packageDownloadCache))
                        {
                            PackageImporter packageImporter = new PackageImporter();
                            packageImporter.WithProgress("Discovering registry packages");
                            action.progress.Add(packageImporter);
                            await packageImporter.IndexRough(packageDownloadCache, true);

                            packageImporter.RestartProgress("Indexing registry packages");
                            await packageImporter.IndexDetails();

                            packageImporter.FinishProgress();
                        }
                        else
                        {
                            Debug.LogWarning($"Could not find the registry package download folder: {packageDownloadCache}");
                            EditorUtility.DisplayDialog("Error",
                                $"Could not find the registry package download folder: {packageDownloadCache}.\n\nEither nothing was downloaded yet through the Package Manager or you changed the Package cache location. In the latter case, please configure the new location under Settings.",
                                "OK");
                        }
                        break;

                    case ACTION_FOLDERS_INDEX:
                        FolderImporter folderImporter = new FolderImporter();
                        folderImporter.WithProgress("Updating additional folders");
                        action.progress.Add(folderImporter);
                        await folderImporter.Run(force);
                        folderImporter.FinishProgress();
                        break;

                    case ACTION_ASSET_STORE_DOWNLOADS:
                        // needs to be started as coroutine due to download triggering which cannot happen outside main thread 
                        bool done = false;
                        UnityPackageDownloadImporter unityDownloadImporter = new UnityPackageDownloadImporter();
                        unityDownloadImporter.WithProgress("Downloading and indexing assets");
                        action.progress.Add(unityDownloadImporter);
                        EditorCoroutineUtility.StartCoroutineOwnerless(unityDownloadImporter.IndexOnline(() => done = true));
                        do
                        {
                            await Task.Delay(100);
                        } while (!done);
                        unityDownloadImporter.FinishProgress();
                        break;

                    case ACTION_ASSET_MANAGER_INDEX:
                        AssetManagerImporter assetManagerImporter = new AssetManagerImporter();
                        assetManagerImporter.WithProgress("Updating Asset Manager index");
                        action.progress.Add(assetManagerImporter);
                        await assetManagerImporter.Run();
                        assetManagerImporter.FinishProgress();
                        break;

                    case ACTION_COLOR_INDEX:
                        ColorImporter colorImporter = new ColorImporter();
                        colorImporter.WithProgress("Extracting color information");
                        action.progress.Add(colorImporter);
                        await colorImporter.Run();
                        colorImporter.FinishProgress();
                        break;

                    case ACTION_AI_CAPTIONS:
                        CaptionCreator captionCreator = new CaptionCreator();
                        captionCreator.WithProgress("Creating AI captions");
                        action.progress.Add(captionCreator);
                        await captionCreator.Run();
                        captionCreator.FinishProgress();
                        break;

                    case ACTION_BACKUP:
                        AssetBackup backup = new AssetBackup();
                        backup.WithProgress("Backing up assets");
                        action.progress.Add(backup);
                        await backup.Run();
                        backup.FinishProgress();
                        break;

                    default:
                        if (action.key.StartsWith(ACTION_USER))
                        {
                            int id = int.Parse(action.key.Split('-').Last());
                            CustomAction ca = DBAdapter.DB.Find<CustomAction>(id);
                            await RunUserAction(ca);
                        }
                        else
                        {
                            Debug.LogError($"No handler found to run action '{action.name}'.");
                        }
                        break;
                }

                // check all actions since also sub-actions might have been started
                actions.ForEach(a => a.CheckStopped());
            }
            actions.ForEach(a => a.scheduled = false);

            // set after initial wizard to capture only a small first portion
            if (!AI.Config.quickIndexingDone)
            {
                AI.Config.quickIndexingDone = true;
                AI.SaveConfig();
            }
            else
            {
                // final pass: start over once if that was the very first time indexing since after all updates are pulled the indexing might crunch additional data
                AppProperty initialIndexingDone = DBAdapter.DB.Find<AppProperty>("InitialIndexingDone");
                if (!CancellationRequested && (initialIndexingDone == null || initialIndexingDone.Value.ToLowerInvariant() != "true"))
                {
                    DBAdapter.DB.InsertOrReplace(new AppProperty("InitialIndexingDone", "true"));
                    await RunActions(actions, true);
                    return;
                }
            }

            LastActionUpdate = DateTime.Now;
            AppProperty lastUpdate = new AppProperty("LastIndexUpdate", LastActionUpdate.ToString(CultureInfo.InvariantCulture));
            DBAdapter.DB.InsertOrReplace(lastUpdate);

            OnActionsDone?.Invoke();
        }

        public async Task RunUserAction(CustomAction ca)
        {
            UpdateAction action = Actions.FirstOrDefault(a => a.key == ACTION_USER + ca.Id);
            if (action == null)
            {
                Debug.LogError($"Could not find a registered custom action '{ca.Name}'.");
                return;
            }

            UserActionRunner customAction = new UserActionRunner();
            customAction.WithProgress($"Running custom action: {ca.Name}");
            action.progress.Add(customAction);
            await customAction.Run(ca);
            customAction.FinishProgress();
        }

        public async void Reindex(AssetInfo info)
        {
            CancellationRequested = false;

            switch (info.AssetSource)
            {
                case Asset.Source.AssetStorePackage:
                case Asset.Source.CustomPackage:
                    UnityPackageImporter unityPackageImporter = new UnityPackageImporter();
                    AI.Actions.RegisterRunningAction(ACTION_ASSET_STORE_CACHE_INDEX, unityPackageImporter, "Indexing package");
                    await unityPackageImporter.IndexDetails(info.Id);
                    unityPackageImporter.FinishProgress();
                    break;

                case Asset.Source.RegistryPackage:
                    await new PackageImporter().IndexDetails(info.Id);
                    break;

                case Asset.Source.Archive:
                    ArchiveImporter archiveImporter = new ArchiveImporter();
                    AI.Actions.RegisterRunningAction(ACTION_ARCHIVE_FOLDERS_INDEX, archiveImporter, "Reindexing archive");
                    await archiveImporter.IndexDetails(info.ToAsset());
                    archiveImporter.FinishProgress();
                    break;

                case Asset.Source.AssetManager:
                    AssetManagerImporter assetManagerImporter = new AssetManagerImporter();
                    AI.Actions.RegisterRunningAction(ACTION_ASSET_MANAGER_INDEX, assetManagerImporter, "Updating Single Asset Manager entry");
                    await assetManagerImporter.Run(info.ToAsset());
                    assetManagerImporter.FinishProgress();
                    break;

                case Asset.Source.Directory:
                    FolderSpec spec = AI.Config.folders.FirstOrDefault(f => f.location == info.Location && f.folderType == info.GetFolderSpecType());
                    if (spec != null)
                    {
                        MediaImporter mediaImporter = new MediaImporter();
                        AI.Actions.RegisterRunningAction(ACTION_MEDIA_FOLDERS_INDEX, mediaImporter, "Updating files index");
                        await mediaImporter.Index(spec);
                        mediaImporter.FinishProgress();
                    }
                    break;

                default:
                    Debug.LogError($"Unsupported asset source of '{info.GetDisplayName()}' for index refresh: {info.AssetSource}");
                    break;
            }

            OnActionsDone?.Invoke();
        }

        public async void FetchAssetDetails(bool forceUpdate = false, int assetId = 0, bool skipEvents = false, UpdateAction action = null)
        {
            AssetStoreImporter assetStoreImporter = new AssetStoreImporter();
            assetStoreImporter.WithProgress("Updating package details");
            action?.progress.Add(assetStoreImporter);

            // set skipEvents if update changed significant data, like version or name
            if (assetId > 0)
            {
                if (await assetStoreImporter.FetchAssetsDetails(forceUpdate, assetId, forceUpdate)) skipEvents = false;
            }
            else
            {
                List<Asset> itemsToUpdate = await assetStoreImporter.FetchAssetUpdates(forceUpdate);
                if (!CancellationRequested)
                {
                    if (itemsToUpdate != null)
                    {
                        if (await assetStoreImporter.FetchAssetsDetails(itemsToUpdate, true, true)) skipEvents = false;
                    }
                    else
                    {
                        Debug.Log("New method for fetching asset details did not work, falling back to full scan.");
                        if (await assetStoreImporter.FetchAssetsDetails(forceUpdate, 0, forceUpdate)) skipEvents = false;
                    }
                }
            }
            assetStoreImporter.FinishProgress();

            // skip in optional update scenarios like when user selects something in the tree to avoid hick-ups 
            if (!skipEvents) AI.TriggerPackageRefresh();
        }

        public bool IsActive(string actionKey)
        {
            return IsActive(Actions.FirstOrDefault(a => a.key == actionKey));
        }

        public bool IsActive(UpdateAction action)
        {
            EnsureStatesInitialized();

            UpdateActionState state = AI.Config.actionStates[_curState]?.actions?.FirstOrDefault(x => x.key == action.key);
            return state?.enabled ?? false;
        }

        private void EnsureStatesInitialized()
        {
            if (AI.Config.actionStates == null || AI.Config.actionStates.Count == 0)
            {
                AI.Config.actionStates = new List<UpdateActionStates>();
                SetDefaultStates(0);
            }
            AI.Config.actionStates[0].name = ACTION_DEFAULT_NAME;
        }

        public void SetAllActive(bool enabled)
        {
            foreach (UpdateAction action in Actions)
            {
                SetActive(action, enabled);
            }
        }

        public void SetActive(string actionKey, bool enabled)
        {
            SetActive(Actions.FirstOrDefault(a => a.key == actionKey), enabled);
        }

        public void SetActive(UpdateAction action, bool enabled)
        {
            UpdateActionState state = AI.Config.actionStates[_curState].actions.FirstOrDefault(x => x.key == action.key);
            if (state == null)
            {
                AI.Config.actionStates[_curState].actions.Add(new UpdateActionState {key = action.key, enabled = enabled});
            }
            else
            {
                state.enabled = enabled;
            }
        }

        public void SetDefaultActive()
        {
            SetDefaultStates(_curState);
        }

        public void CancelAll()
        {
            CancellationRequested = true;

            GetRunningActions().ForEach(a => a.progress?.ForEach(p => p.Cancel()));
        }

        public bool CreateBackups
        {
            get => IsActive(ACTION_BACKUP);
            set => SetActive(ACTION_BACKUP, value);
        }

        public bool CreateAICaptions
        {
            get => IsActive(ACTION_AI_CAPTIONS);
            set => SetActive(ACTION_AI_CAPTIONS, value);
        }

        public bool ExtractColors
        {
            get => IsActive(ACTION_COLOR_INDEX);
            set => SetActive(ACTION_COLOR_INDEX, value);
        }

        public bool DownloadAssets
        {
            get => IsActive(ACTION_ASSET_STORE_DOWNLOADS);
            set => SetActive(ACTION_ASSET_STORE_DOWNLOADS, value);
        }

        public bool IndexPackageCache
        {
            get => IsActive(ACTION_PACKAGE_CACHE_INDEX);
            set => SetActive(ACTION_PACKAGE_CACHE_INDEX, value);
        }

        public bool IndexAssetManager
        {
            get => IsActive(ACTION_ASSET_MANAGER_INDEX);
            set => SetActive(ACTION_ASSET_MANAGER_INDEX, value);
        }
    }
}