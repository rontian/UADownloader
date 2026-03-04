using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if !ASSET_INVENTORY_NOAUDIO
using JD.EditorAudioUtils;
#endif
using Newtonsoft.Json;
#if !UNITY_2021_2_OR_NEWER
using Unity.SharpZipLib.Zip;
#endif
using UnityEditor;
using UnityEditor.Callbacks;
#if USE_URP_CONVERTER
using UnityEditor.Rendering.Universal;
#endif
using UnityEngine;
using ErrorEventArgs = Newtonsoft.Json.Serialization.ErrorEventArgs;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetInventory
{
    public static class AI
    {
        public const string VERSION = "3.6.1";
        public const string DEFINE_SYMBOL = "ASSET_INVENTORY";
        public const string DEFINE_SYMBOL_OLLAMA = DEFINE_SYMBOL + "_OLLAMA";
        public const string DEFINE_SYMBOL_HIDE_AI = DEFINE_SYMBOL + "_HIDE_AI";
        public const string DEFINE_SYMBOL_HIDE_BROWSER = DEFINE_SYMBOL + "_HIDE_BROWSER";

        internal const string HOME_LINK = "https://www.wetzold.com/tool";
        internal const string DISCORD_LINK = "https://discord.com/invite/uzeHzEMM4B";
        internal const string ASSET_STORE_FOLDER_NAME = "Asset Store-5.x";
        internal const string TEMP_FOLDER = "_AssetInventoryTemp";
        internal const int ASSET_STORE_ID = 308224;
        internal const string SEPARATOR = "-~-";
        internal const string TAG_START = "[";
        internal const string TAG_END = "]";
        internal static readonly bool DEBUG_MODE = false;
        internal const string AFFILIATE_ID = "1100l3Bzsf";
        internal const string AFFILIATE_PARAM = "aid=" + AFFILIATE_ID;
        internal const string ASSET_STORE_LINK = "https://u3d.as/3sCf?" + AFFILIATE_PARAM;
        internal const string CLOUD_HOME_URL = "https://cloud.unity.com/home/organizations";
        internal const string TUTORIALS_VERSION = "5.0.2";

        private const double CACHE_LIMIT_INTERVAL = 10; // to ensure it is only run every X min
        private const string PARTIAL_INDICATOR = "ai-partial.info";
        private static readonly string[] ConversionExtensions = {"mat", "fbx"};

        // Thread-safe tracking of ongoing extractions to prevent race conditions
        private static readonly Dictionary<int, Task<string>> _ongoingExtractions = new Dictionary<int, Task<string>>();
        private static readonly object _extractionLock = new object();

        public enum AssetGroup
        {
            Audio = 0,
            Images = 1,
            Videos = 2,
            Prefabs = 3,
            Materials = 4,
            Shaders = 5,
            Models = 6,
            Animations = 7,
            Fonts = 8,
            Scripts = 9,
            Libraries = 10,
            Documents = 11
        }

        internal static string UsedConfigLocation { get; private set; }

        public static event Action OnPackagesUpdated;
        public static event Action<Asset> OnPackageImageLoaded;

        private const int MAX_DROPDOWN_ITEMS = 25;
        private const int FOLDER_CACHE_TIME = 60;
        private const string CONFIG_NAME = "AssetInventoryConfig.json";

        private static bool InitDone { get; set; }
        private static UpdateObserver _observer;
        private static string _assetCacheFolder; // do not use timed cache for this as it is used in threads and should not be invalidated if not on main thread
        private static string _configLocation; // do not use timed cache, user will typically not change this in-project
        private static DateTime _lastAssetCacheCheck;
        private static readonly TimedCache<string> _materializeFolder = new TimedCache<string>();
        private static readonly TimedCache<string> _previewFolder = new TimedCache<string>();

        internal static List<RelativeLocation> RelativeLocations
        {
            get
            {
                if (_relativeLocations == null) LoadRelativeLocations();
                return _relativeLocations;
            }
        }
        private static List<RelativeLocation> _relativeLocations;

        internal static List<RelativeLocation> UserRelativeLocations
        {
            get
            {
                if (_userRelativeLocations == null) LoadRelativeLocations();
                return _userRelativeLocations;
            }
        }
        private static List<RelativeLocation> _userRelativeLocations;

        public static ActionHandler Actions
        {
            get
            {
                if (_actions == null) _actions = new ActionHandler();
                return _actions;
            }
        }
        private static ActionHandler _actions;

        public static UpgradeUtil UpgradeUtil
        {
            get
            {
                if (_upgradeUtil == null) _upgradeUtil = new UpgradeUtil();
                return _upgradeUtil;
            }
        }
        private static UpgradeUtil _upgradeUtil;

        public static Cooldown Cooldown
        {
            get
            {
                if (_cooldown == null)
                {
                    _cooldown = new Cooldown(Config.cooldownInterval, Config.cooldownDuration);
                    _cooldown.Enabled = Config.useCooldown;
                }
                return _cooldown;
            }
        }
        private static Cooldown _cooldown;

        public static MemoryObserver MemoryObserver
        {
            get
            {
                if (_memoryObserver == null)
                {
                    _memoryObserver = new MemoryObserver(Config.memoryLimit);
                    _memoryObserver.Enabled = true;
                }
                return _memoryObserver;
            }
        }
        private static MemoryObserver _memoryObserver;

        public static DirectorySizeManager CacheLimiter
        {
            get
            {
                if (_cacheLimiter == null)
                {
                    _cacheLimiter = new DirectorySizeManager(GetMaterializeFolder(), Config.cacheLimit, pathToDelete =>
                    {
                        // folder will contain asset Id and optional version, e.g. "MyAsset-~-12345-~-1.0.0"
                        string[] segments = pathToDelete.Split(SEPARATOR);
                        if (segments.Length < 2) return true; // not a valid path, can be removed
                        if (!int.TryParse(segments[1].Trim(), out int assetId)) return true; // not a valid asset Id, can be removed
                        string version = segments.Length > 2 ? segments.Last() : null;

                        Asset asset = DBAdapter.DB.Find<Asset>(assetId);
                        if (asset != null)
                        {
                            // if version is different, this cache entry can be cleaned up
                            if (version != null && asset.GetSafeVersion() != version) return true;

                            if (asset.KeepExtracted ||
                                asset.CurrentState == Asset.State.InProcess ||
                                asset.CurrentState == Asset.State.SubInProcess)
                            {
                                return false;
                            }
                        }

                        return true;
                    });
                    _cacheLimiter.Enabled = Config.limitCacheSize;
                }
                return _cacheLimiter;
            }
        }
        private static DirectorySizeManager _cacheLimiter;

        public static AssetInventorySettings Config
        {
            get
            {
                if (_config == null) LoadConfig();
                return _config;
            }
        }
        private static AssetInventorySettings _config;
        internal static readonly List<string> ConfigErrors = new List<string>();
        internal static bool UICustomizationMode { get; set; }

        public static bool ClearCacheInProgress { get; private set; }

        public static Dictionary<AssetGroup, string[]> TypeGroups { get; } = new Dictionary<AssetGroup, string[]>
        {
            {AssetGroup.Audio, new[] {"wav", "mp3", "ogg", "aiff", "aif", "mod", "it", "s3m", "xm", "flac"}},
            {
                AssetGroup.Images,
                new[]
                {
                    "png", "jpg", "jpeg", "bmp", "tga", "tif", "tiff", "psd", "svg", "webp", "ico", "gif", "hdr", "iff", "pict"
                }
            },
            {AssetGroup.Videos, new[] {"avi", "asf", "dv", "m4v", "mov", "mp4", "mpg", "mpeg", "ogv", "vp8", "webm", "wmv"}},
            {AssetGroup.Prefabs, new[] {"prefab"}},
            {AssetGroup.Materials, new[] {"mat", "physicmaterial", "physicsmaterial", "sbs", "sbsar", "cubemap"}},
            {AssetGroup.Shaders, new[] {"shader", "shadergraph", "shadersubgraph", "compute", "raytrace"}},
            {AssetGroup.Models, new[] {"fbx", "obj", "blend", "dae", "3ds", "dxf", "max", "c4d", "mb", "ma"}},
            {AssetGroup.Animations, new[] {"anim"}},
            {AssetGroup.Fonts, new[] {"ttf", "otf"}},
            {AssetGroup.Scripts, new[] {"cs", "php", "py", "js", "lua"}},
            {AssetGroup.Libraries, new[] {"zip", "rar", "7z", "unitypackage", "so", "bundle", "dll", "jar"}},
            {AssetGroup.Documents, new[] {"md", "doc", "docx", "txt", "json", "rtf", "pdf", "htm", "html", "readme", "xml", "chm", "csv"}}
        };

        internal static Dictionary<string, string[]> FilterRestriction { get; } = new Dictionary<string, string[]>
        {
            {"Length", new[] {"Audio", "Videos"}},
            {"Width", new[] {"Images", "Videos"}},
            {"Height", new[] {"Images", "Videos"}},
            {"ImageType", new[] {"Images"}}
        };

        private static Queue<Asset> _extractionQueue = new Queue<Asset>();
        private static Tuple<Asset, Task> _currentExtraction;
        private static int _extractionProgress;

        [DidReloadScripts(1)]
        private static void AutoInit()
        {
            // this will be run after a recompile so keep to a minimum, e.g. ensure third party tools can work
            EditorApplication.delayCall += () => Init();
            EditorApplication.update += UpdateLoop;
        }

        private static void UpdateLoop()
        {
            if (_extractionQueue.Count > 0)
            {
                if (_extractionProgress == 0) _extractionProgress = MetaProgress.Start("Package Extraction");
                if (_currentExtraction == null || _currentExtraction.Item2.IsCompleted)
                {
                    Asset next = _extractionQueue.Dequeue();
                    MetaProgress.Report(_extractionProgress, 1, _extractionQueue.Count, next.DisplayName);

                    Task task = EnsureMaterializedAsset(next);
                    _currentExtraction = new Tuple<Asset, Task>(next, task);
                }
            }
            else if (_extractionProgress > 0)
            {
                MetaProgress.Remove(_extractionProgress);
                _extractionProgress = 0;
            }
        }

        internal static void ReInit()
        {
            InitDone = false;
            LoadConfig();
            Init();
        }

        public static void Init(bool secondTry = false, bool force = false)
        {
            if (InitDone && !force) return;

            ThreadUtils.Initialize();
            SetupDefines();

            _assetCacheFolder = null;
            _configLocation = null;
            _materializeFolder.Clear();
            _previewFolder.Clear();

            string folder = GetStorageFolder();
            if (!Directory.Exists(folder))
            {
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception e)
                {
                    if (secondTry)
                    {
                        Debug.LogError($"Could not create storage folder for database in default location '{folder}' as well. Giving up: {e.Message}");
                    }
                    else
                    {
                        Debug.LogError($"Could not create storage folder '{folder}' for database. Reverting to default location: {e.Message}");
                        Config.customStorageLocation = null;
                        SaveConfig();
                        Init(true);
                        return;
                    }
                }
            }
            UnityPreviewGenerator.CleanUp();
            DependencyAnalysis.CleanUp();

            GetAssetCacheFolder(); // cache into main thread since GetConfig is not available from threads
            UpdateSystemData();
            LoadRelativeLocations();

            UpgradeUtil.PerformUpgrades();

            Tagging.LoadAssignments(null, false);
            Metadata.LoadAssignments(null, false);
            AssetStore.FillBufferOnDemand(true);
            Actions.Init(force);

            InitDone = true;

            // Show welcome window once on first install
            if (!Config.welcomeShown)
            {
                Config.welcomeShown = true;
                SaveConfig();
                WelcomeWindow.ShowWindow();
            }
        }

        internal static void SwitchDatabase(string targetFolder)
        {
            DBAdapter.Close();
            AssetUtils.ClearCache();
            Config.customStorageLocation = targetFolder;
            SaveConfig();

            Init(false, true);

            TriggerPackageRefresh();
        }

        internal static void StartCacheObserver()
        {
            GetObserver().Start();
        }

        internal static void StopCacheObserver()
        {
            GetObserver().Stop();
        }

        internal static bool IsObserverActive()
        {
            return GetObserver().IsActive();
        }

        internal static UpdateObserver GetObserver()
        {
            if (_observer == null) _observer = new UpdateObserver(GetAssetCacheFolder(), new[] {"unitypackage", "tmp"});
            return _observer;
        }

        private static void RunCacheLimiter()
        {
            if (!CacheLimiter.Enabled || CacheLimiter.IsRunning) return;
            if ((DateTime.Now - CacheLimiter.LastCheckTime).TotalMinutes < CACHE_LIMIT_INTERVAL) return;

            _ = CacheLimiter.CheckAndClean();
        }

        private static void SetupDefines()
        {
            if (!AssetUtils.HasDefine(DEFINE_SYMBOL)) AssetUtils.AddDefine(DEFINE_SYMBOL);
        }

        private static void UpdateSystemData()
        {
            SystemData data = new SystemData();
            data.Key = SystemInfo.deviceUniqueIdentifier;
            data.Name = SystemInfo.deviceName;
            data.Type = SystemInfo.deviceType.ToString();
            data.Model = SystemInfo.deviceModel;
            data.OS = SystemInfo.operatingSystem;
            data.LastUsed = DateTime.Now;

            try
            {
                DBAdapter.DB.InsertOrReplace(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not update system data: {e.Message}");
            }
        }

        internal static bool IsFileType(string path, AssetGroup typeGroup)
        {
            if (path == null) return false;
            return TypeGroups[typeGroup].Contains(IOUtils.GetExtensionWithoutDot(path).ToLowerInvariant());
        }

        public static string GetStorageFolder()
        {
            if (!string.IsNullOrEmpty(Config.customStorageLocation)) return Path.GetFullPath(Config.customStorageLocation);

            return IOUtils.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AssetInventory");
        }

        public static string GetConfigLocation()
        {
            if (_configLocation != null) return _configLocation;

            // search for local project-specific override first
            string guid = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(CONFIG_NAME)).FirstOrDefault();
            if (guid != null) return AssetDatabase.GUIDToAssetPath(guid);

            // second fallback is environment variable
            string configPath = Environment.GetEnvironmentVariable("ASSETINVENTORY_CONFIG_PATH");
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                // if user already specified json file use that one, otherwise use default name
                if (configPath.ToLowerInvariant().EndsWith(".json")) return configPath;
                return IOUtils.PathCombine(configPath, CONFIG_NAME);
            }

            // finally use from central well-known folder
            _configLocation = IOUtils.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), CONFIG_NAME);

            return _configLocation;
        }

        public static string GetPreviewFolder(string customFolder = null, bool noCache = false, bool createOnDemand = true)
        {
            if (!noCache && _previewFolder.TryGetValue(out string path)) return path;

            string previewPath = null;
            if (customFolder != null) previewPath = IOUtils.PathCombine(customFolder, "Previews");
            if (previewPath == null)
                previewPath = string.IsNullOrWhiteSpace(Config.previewFolder)
                    ? IOUtils.PathCombine(GetStorageFolder(), "Previews")
                    : Config.previewFolder;
            if (createOnDemand) Directory.CreateDirectory(previewPath);

            if (!noCache) _previewFolder.SetValue(previewPath, TimeSpan.FromSeconds(FOLDER_CACHE_TIME));

            return previewPath;
        }

        public static void RefreshPreviewCache()
        {
            _previewFolder.Clear();
        }

        public static string GetBackupFolder(bool createOnDemand = true, string customFolder = null)
        {
            string backupPath;
            if (customFolder != null)
            {
                backupPath = IOUtils.PathCombine(customFolder, "Backups");
            }
            else
            {
                backupPath = string.IsNullOrWhiteSpace(Config.backupFolder)
                    ? IOUtils.PathCombine(GetStorageFolder(), "Backups")
                    : Config.backupFolder;
            }

            if (createOnDemand) Directory.CreateDirectory(backupPath);

            return backupPath;
        }

        public static string GetMaterializeFolder(string customFolder = null, bool noCache = false)
        {
            if (!noCache && _materializeFolder.TryGetValue(out string path)) return path;

            string cachePath = null;
            if (customFolder != null) cachePath = IOUtils.PathCombine(customFolder, "Extracted");
            if (cachePath == null)
            {
                cachePath = string.IsNullOrWhiteSpace(Config.cacheFolder)
                    ? IOUtils.PathCombine(GetStorageFolder(), "Extracted")
                    : Config.cacheFolder;
            }

            if (!noCache) _materializeFolder.SetValue(cachePath, TimeSpan.FromSeconds(FOLDER_CACHE_TIME));

            return cachePath;
        }

        public static string GetMaterializedAssetPath(Asset asset)
        {
            // append the ID to support identically named packages in different locations
            // also append version if available to support different efficient caching without having to delete the whole folder all the time
            string version = asset.GetSafeVersion();
            return IOUtils.ToLongPath(IOUtils.PathCombine(GetMaterializeFolder(),
                asset.SafeName
                + SEPARATOR
                + asset.Id
                + (!string.IsNullOrWhiteSpace(version) ? SEPARATOR + version : "")));
        }

        public static async Task<string> ExtractAsset(Asset asset, AssetFile assetFile = null, bool fileOnly = false, CancellationToken ct = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(asset.GetLocation(true))) return null;

            // make sure parents are extracted first
            string archivePath = IOUtils.ToLongPath(await asset.GetLocation(true, true));
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
            {
                if (asset.ParentId <= 0)
                {
                    Debug.LogError($"Asset has vanished since last refresh and cannot be indexed: {archivePath}");

                    // reflect new state
                    // TODO: consider rel systems 
                    asset.SetLocation(null);
                    DBAdapter.DB.Execute("update Asset set Location=null where Id=?", asset.Id);
                }
                return null;
            }

            string tempPath = GetMaterializedAssetPath(asset);
            string indicator = Path.Combine(tempPath, PARTIAL_INDICATOR);

            // Check available disk space before extraction
            long freeSpace = IOUtils.GetFreeSpace(tempPath);
            long required = asset.PackageSize * 5; // Conservative estimate (5x compression ratio)
            if (freeSpace >= 0 && freeSpace < required)
            {
                Debug.LogError($"Cannot extract '{asset}': Insufficient disk space. " +
                    $"Estimated need: {StringUtils.FormatBytes(required)}, " +
                    $"Available: {StringUtils.FormatBytes(freeSpace)}");
                return null;
            }

            // don't extract again if already done and version is known
            if (!string.IsNullOrWhiteSpace(asset.GetSafeVersion()) && Directory.Exists(tempPath) && !File.Exists(indicator)) return tempPath;

            // Create a unique key for this extraction to prevent race conditions
            int extractionKey = asset.Id;

            // Check if this extraction is already in progress and register our intent
            Task<string> ongoingTask;
            TaskCompletionSource<string> taskCompletionSource = null;
            lock (_extractionLock)
            {
                if (_ongoingExtractions.TryGetValue(extractionKey, out ongoingTask))
                {
                    // Another thread is already extracting this asset
                }
                else
                {
                    // Register our intent to extract by creating a placeholder task
                    taskCompletionSource = new TaskCompletionSource<string>();
                    _ongoingExtractions[extractionKey] = taskCompletionSource.Task;
                }
            }

            // If we found an ongoing task, wait for it outside the lock
            if (ongoingTask != null)
            {
                return await ongoingTask;
            }

            // Create the extraction task and complete our placeholder
            Task<string> extractionTask = Task.Run(async () =>
            {
                try
                {
                    // delete existing cache if interested in whole bundle to make sure everything is there
                    if (assetFile == null || !fileOnly || asset.KeepExtracted) // if only asset file but asset should be kept extracted, then treat as full package
                    {
                        int retries = 0;
                        while (retries < 5 && Directory.Exists(tempPath))
                        {
                            try
                            {
                                await Task.Run(() => Directory.Delete(tempPath, true));
                                break;
                            }
                            catch (Exception)
                            {
                                retries++;
                                await Task.Delay(500);
                            }
                        }
                        if (Directory.Exists(tempPath)) Debug.LogWarning($"Could not remove temporary directory: {tempPath}");

                        try
                        {
                            if (asset.AssetSource == Asset.Source.Archive)
                            {
#if UNITY_2021_2_OR_NEWER
                                if (!await Task.Run(() => IOUtils.ExtractArchive(archivePath, tempPath, ct)))
                                {
                                    // stop here when archive could not be extracted (e.g. path too long, canceled) as otherwise files get removed from index
                                    return null;
                                }
#else
                                if (asset.Location.ToLowerInvariant().EndsWith(".zip"))
                                {
                                    FastZip fastZip = new FastZip();
                                    await Task.Run(() => fastZip.ExtractZip(archivePath, tempPath, null));
                                }
#endif
                            }
                            else
                            {
                                // special handling for Tar as that will throw null errors with SharpCompress
                                await Task.Run(() => TarUtil.ExtractGz(archivePath, tempPath, ct));
                            }

                            // safety delay in case this is a network drive which needs some time to unlock all files
                            await Task.Delay(100);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Could not extract archive '{archivePath}' due to errors. Index results will be partial: {e.Message}");
                            return null;
                        }
                        RunCacheLimiter();

                        return Directory.Exists(tempPath) ? tempPath : null;
                    }

                    // single file only
                    string targetPath = Path.Combine(GetMaterializedAssetPath(asset), assetFile.GetSourcePath(true));
                    if (File.Exists(targetPath)) return targetPath;

                    try
                    {
                        if (asset.AssetSource == Asset.Source.Archive)
                        {
                            // TODO: switch to single file
#if UNITY_2021_2_OR_NEWER
                            await Task.Run(() => IOUtils.ExtractArchive(archivePath, tempPath, ct));
#else
                            if (asset.Location.ToLowerInvariant().EndsWith(".zip"))
                            {
                                FastZip fastZip = new FastZip();
                                await Task.Run(() => fastZip.ExtractZip(archivePath, tempPath, null));
                            }
#endif
                        }
                        else
                        {
                            // special handling for Tar as that will throw null errors with SharpCompress
                            await Task.Run(() => TarUtil.ExtractGzFile(archivePath, assetFile.GetSourcePath(true), tempPath, ct));
                            if (!File.Exists(indicator) && Directory.Exists(tempPath))
                            {
                                File.WriteAllText(indicator, DateTime.Now.ToString(CultureInfo.InvariantCulture));
                            }
                        }

                        // safety delay in case this is a network drive which needs some time to unlock all files
                        await Task.Delay(100);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Could not extract archive '{archivePath}' due to errors: {e.Message}");

                        // Clean up partial extraction to avoid wasting disk space
                        try
                        {
                            if (Directory.Exists(tempPath))
                            {
                                Directory.Delete(tempPath, true);
                            }
                        }
                        catch (Exception cleanupEx)
                        {
                            Debug.LogWarning($"Could not clean up partial extraction at '{tempPath}': {cleanupEx.Message}. Manual cleanup may be required.");
                        }

                        return null;
                    }
                    RunCacheLimiter();

                    return File.Exists(targetPath) ? targetPath : null;
                }
                finally
                {
                    // Clean up the extraction tracking when done
                    lock (_extractionLock)
                    {
                        _ongoingExtractions.Remove(extractionKey);
                    }
                }
            });

            // Complete the placeholder task with the result
            try
            {
                string result = await extractionTask;
                taskCompletionSource.SetResult(result);
                return result;
            }
            catch (Exception ex)
            {
                taskCompletionSource.SetException(ex);
                throw;
            }
        }

        public static bool IsMaterialized(Asset asset, AssetFile assetFile = null)
        {
            // check if currently being extracted
            if (_ongoingExtractions.ContainsKey(asset.Id)) return false;

            if (asset.AssetSource == Asset.Source.Directory || asset.AssetSource == Asset.Source.RegistryPackage)
            {
                if (assetFile != null) return File.Exists(assetFile.GetSourcePath(true));
                return Directory.Exists(asset.GetLocation(true));
            }

            string assetPath = GetMaterializedAssetPath(asset);
            if (asset.AssetSource == Asset.Source.AssetManager)
            {
                if (assetFile == null) return false;
                return Directory.Exists(Path.Combine(assetPath, assetFile.Guid));
            }

            if (assetFile != null) return File.Exists(Path.Combine(assetPath, assetFile.GetSourcePath(true)));

            string indicator = Path.Combine(assetPath, PARTIAL_INDICATOR);
            return Directory.Exists(assetPath) && !File.Exists(indicator);
        }

        public static async Task<string> EnsureMaterializedAsset(AssetInfo info, bool fileOnly = false, CancellationToken ct = default(CancellationToken))
        {
            string targetPath = await EnsureMaterializedAsset(info.ToAsset(), info, fileOnly, ct);
            info.IsMaterialized = IsMaterialized(info.ToAsset(), info);
            return targetPath;
        }

        public static async Task<string> EnsureMaterializedAsset(Asset asset, AssetFile assetFile = null, bool fileOnly = false, CancellationToken ct = default(CancellationToken))
        {
            if (asset.AssetSource == Asset.Source.Directory || asset.AssetSource == Asset.Source.RegistryPackage)
            {
                return File.Exists(assetFile.GetSourcePath(true)) ? assetFile.GetSourcePath(true) : null;
            }

            string targetPath;
            if (asset.AssetSource == Asset.Source.AssetManager)
            {
                if (assetFile == null) return null;

                targetPath = Path.Combine(GetMaterializedAssetPath(asset), assetFile.Guid);
                if (!Directory.Exists(targetPath))
                {
#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
                    CloudAssetManagement cam = await GetCloudAssetManagement();

                    List<string> files = await cam.FetchAssetFromRemote(asset, assetFile, targetPath);
                    if (files == null || files.Count == 0) return null;
                    RunCacheLimiter();
#else
                    return null;
#endif
                }

                // special handling for single files
                List<string> allFiles = await Task.Run(() => Directory.GetFiles(targetPath, "*.*", SearchOption.AllDirectories).ToList());
                if (allFiles.Count == 1) return allFiles[0];

                return targetPath;
            }

            targetPath = GetMaterializedAssetPath(asset);
            if (_ongoingExtractions.TryGetValue(asset.Id, out Task<string> process)) await process;
            if (!Directory.Exists(targetPath) || File.Exists(Path.Combine(targetPath, PARTIAL_INDICATOR)))
            {
                // ensure parent hierarchy is extracted first
                string archivePath = IOUtils.ToLongPath(await asset.GetLocation(true, true));
                if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return null;

                await Task.Run(() => ExtractAsset(asset, assetFile, fileOnly, ct));
                if (!Directory.Exists(targetPath)) return null;
            }

            // race condition protection
            if (_ongoingExtractions.TryGetValue(asset.Id, out Task<string> process2)) await process2;

            if (assetFile != null)
            {
                string sourcePath = Path.Combine(GetMaterializedAssetPath(asset), assetFile.GetSourcePath(true));
                if (!File.Exists(sourcePath))
                {
                    // file is most likely not contained in package anymore
                    Debug.LogError($"File '{assetFile.FileName}' is not contained in this version of the package '{asset}' anymore. Reindexing might solve this.");

                    if (Config.removeUnresolveableDBFiles)
                    {
                        // remove from index
                        Debug.LogError($"Removing from index: {assetFile.FileName}");

                        DBAdapter.DB.Execute("delete from AssetFile where Id=?", assetFile.Id);
                        assetFile.Id = 0;
                    }
                    return null;
                }

                targetPath = Path.Combine(Path.GetDirectoryName(sourcePath), "Content", Path.GetFileName(assetFile.GetPath(true)));
                try
                {
                    if (!File.Exists(targetPath))
                    {
                        string directoryName = Path.GetDirectoryName(targetPath);
                        Directory.CreateDirectory(directoryName);
                        File.Copy(sourcePath, targetPath, true);
                    }

                    string sourceMetaPath = sourcePath + ".meta";
                    string targetMetaPath = targetPath + ".meta";
                    if (File.Exists(sourceMetaPath) && !File.Exists(targetMetaPath)) File.Copy(sourceMetaPath, targetMetaPath, true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Could not extract file. Most likely the target device ran out of space: {e.Message}");
                    return null;
                }
            }

            return targetPath;
        }

        public static async Task CalculateDependencies(AssetInfo info, CancellationToken ct = default(CancellationToken))
        {
            DependencyAnalysis da = new DependencyAnalysis();
            await da.Analyze(info, ct);
        }

        public static List<AssetInfo> LoadAssets()
        {
            string indexedQuery = "SELECT *, Count(*) as FileCount, Sum(af.Size) as UncompressedSize from AssetFile af left join Asset on Asset.Id = af.AssetId group by af.AssetId order by Asset.SafeName COLLATE NOCASE";
            Dictionary<int, AssetInfo> indexedResult = DBAdapter.DB.Query<AssetInfo>(indexedQuery).ToDictionary(a => a.AssetId);

            string allQuery = "SELECT *, Id as AssetId from Asset order by SafeName COLLATE NOCASE";
            List<AssetInfo> result = DBAdapter.DB.Query<AssetInfo>(allQuery);

            // sqlite does not support "right join", therefore merge two queries manually 
            // TODO: it does in this newer version now, upgrade
            result.ForEach(asset =>
            {
                if (indexedResult.TryGetValue(asset.Id, out AssetInfo match))
                {
                    asset.FileCount = match.FileCount;
                    asset.UncompressedSize = match.UncompressedSize;
                }
            });

            InitAssets(result);

            return result;
        }

        internal static void InitAssets(List<AssetInfo> result)
        {
            ResolveParents(result, result);
            GetObserver().SetAll(result);
        }

        internal static void ResolveParents(List<AssetInfo> assets, List<AssetInfo> allAssets)
        {
            if (allAssets == null) return;

            Dictionary<int, AssetInfo> assetDict = allAssets.ToDictionary(a => a.AssetId);

            foreach (AssetInfo asset in assets)
            {
                // copy over additional metadata from allAssets (mostly file count which enables other features)
                if (asset.FileCount == 0 && assetDict.TryGetValue(asset.AssetId, out AssetInfo fullInfo))
                {
                    asset.FileCount = fullInfo.FileCount;
                    asset.UncompressedSize = fullInfo.UncompressedSize;
                }

                if (asset.ParentId > 0 && asset.ParentInfo == null)
                {
                    if (assetDict.TryGetValue(asset.ParentId, out AssetInfo parentInfo))
                    {
                        asset.ParentInfo = parentInfo;
                        if (asset.IsPackage()) parentInfo.ChildInfos.Add(asset);
                    }
                }
            }
        }

        internal static void ResolveChildren(List<AssetInfo> assets, List<AssetInfo> allAssets)
        {
            if (allAssets == null) return;

            foreach (AssetInfo asset in assets)
            {
                asset.ChildInfos = allAssets.Where(a => a.ParentId == asset.AssetId).ToList();
            }
        }

        internal static string[] ExtractAssetNames(IEnumerable<AssetInfo> assets, bool includeIdForDuplicates)
        {
            bool intoSubmenu = Config.groupLists && assets.Count(a => a.FileCount > 0) > MAX_DROPDOWN_ITEMS;
            List<string> result = new List<string> {"-all-"};
            List<AssetEntry> assetEntries = new List<AssetEntry>();

            foreach (AssetInfo asset in assets)
            {
                if (asset.FileCount > 0 && !asset.Exclude)
                {
                    // Use display name when IDs are included
                    string name = includeIdForDuplicates ? asset.GetDisplayName().Replace("/", " ") : asset.SafeName;

                    if (includeIdForDuplicates && asset.SafeName != Asset.NONE)
                    {
                        name = $"{name} [{asset.AssetId}]";
                    }

                    bool isSubPackage = asset.ParentId > 0;
                    string groupKey = intoSubmenu && !asset.SafeName.StartsWith("-")
                        ? name.Substring(0, 1).ToUpperInvariant()
                        : string.Empty;

                    assetEntries.Add(new AssetEntry
                    {
                        Name = name,
                        IsSubPackage = isSubPackage,
                        GroupKey = groupKey
                    });
                }
            }

            // Custom sorting
            assetEntries.Sort((a, b) =>
            {
                int cmp = a.IsSubPackage.CompareTo(b.IsSubPackage); // Non-sub-packages first
                if (cmp != 0) return cmp;

                cmp = string.Compare(a.GroupKey, b.GroupKey, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;

                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            // Building the final list
            if (assetEntries.Count > 0)
            {
                int noneIdx = -1;
                result.Add(string.Empty);
                for (int i = 0; i < assetEntries.Count; i++)
                {
                    AssetEntry entry = assetEntries[i];

                    string displayName;
                    if (intoSubmenu)
                    {
                        if (entry.IsSubPackage)
                        {
                            // Sub-packages under "-Sub- / GroupKey / Name"
                            displayName = "-Sub-/" + entry.GroupKey + "/" + entry.Name;
                        }
                        else
                        {
                            // Non-sub-packages under "GroupKey / Name"
                            displayName = entry.GroupKey + "/" + entry.Name;
                        }
                    }
                    else
                    {
                        if (entry.IsSubPackage)
                        {
                            displayName = "-Sub- " + entry.Name;
                        }
                        else
                        {
                            displayName = entry.Name;
                        }
                    }

                    result.Add(displayName);
                    if (entry.Name == Asset.NONE) noneIdx = result.Count - 1;
                }

                if (noneIdx >= 0)
                {
                    result.RemoveAt(noneIdx);
                    result.Insert(1, Asset.NONE);
                }
            }

            return result.ToArray();
        }

        internal static string[] ExtractTagNames(List<Tag> tags)
        {
            bool intoSubmenu = Config.groupLists && tags.Count > MAX_DROPDOWN_ITEMS;
            List<string> result = new List<string> {"-all-", "-none-", string.Empty};
            result.AddRange(tags
                .Select(a =>
                    intoSubmenu && !a.Name.StartsWith("-")
                        ? a.Name.Substring(0, 1).ToUpperInvariant() + "/" + a.Name
                        : a.Name)
                .OrderBy(s => s));

            if (result.Count == 2) result.RemoveAt(1);

            return result.ToArray();
        }

        internal static string[] ExtractPublisherNames(IEnumerable<AssetInfo> assets)
        {
            bool intoSubmenu =
                Config.groupLists &&
                assets.Count(a => a.FileCount > 0) >
                MAX_DROPDOWN_ITEMS; // approximation, publishers != assets but roughly the same
            List<string> result = new List<string> {"-all-", string.Empty};
            result.AddRange(assets
                .Where(a => a.FileCount > 0)
                .Where(a => !a.Exclude)
                .Where(a => !string.IsNullOrEmpty(a.SafePublisher))
                .Select(a =>
                    intoSubmenu
                        ? a.SafePublisher.Substring(0, 1).ToUpperInvariant() + "/" + a.SafePublisher
                        : a.SafePublisher)
                .Distinct()
                .OrderBy(s => s));

            if (result.Count == 2) result.RemoveAt(1);

            return result.ToArray();
        }

        internal static string[] ExtractCategoryNames(IEnumerable<AssetInfo> assets)
        {
            List<string> result = new List<string> {"-all-", string.Empty};
            result.AddRange(assets
                .Where(a => a.FileCount > 0)
                .Where(a => !a.Exclude)
                .Where(a => !string.IsNullOrEmpty(a.GetDisplayCategory()))
                .Select(a => a.GetDisplayCategory())
                .Distinct()
                .OrderBy(s => s));

            if (result.Count == 2) result.RemoveAt(1);

            return result.ToArray();
        }

        internal static string[] LoadTypes()
        {
            List<string> result = new List<string> {"-all-", string.Empty};

            string query = "SELECT Distinct(Type) from AssetFile where Type not null and Type != \"\" order by Type";
            List<string> raw = DBAdapter.DB.QueryScalars<string>($"{query}");

            List<string> groupTypes = new List<string>();
            foreach (KeyValuePair<AssetGroup, string[]> group in TypeGroups)
            {
                groupTypes.AddRange(group.Value);
                foreach (string type in group.Value)
                {
                    if (raw.Contains(type))
                    {
                        result.Add($"{group.Key}");
                        break;
                    }
                }
            }

            if (Config.showExtensionsList)
            {
                if (result.Last() != "") result.Add(string.Empty);

                // others
                result.AddRange(raw.Where(r => !groupTypes.Contains(r)).Select(type => $"Others/{type}"));

                // all
                result.AddRange(raw.Select(type => $"All/{type}"));
            }

            if (result.Count == 2) result.RemoveAt(1);

            return result.ToArray();
        }

        public static async Task<long> GetCacheFolderSize()
        {
            return await IOUtils.GetFolderSize(GetMaterializeFolder());
        }

        public static async Task<long> GetPersistedCacheSize()
        {
            if (!Directory.Exists(GetMaterializeFolder())) return 0;

            long result = 0;

            List<Asset> keepAssets = DBAdapter.DB.Table<Asset>().Where(a => a.KeepExtracted).ToList();
            List<string> keepPaths = keepAssets.Select(a => IOUtils.ToShortPath(GetMaterializedAssetPath(a)).ToLowerInvariant()).ToList();
            string[] packages = Directory.GetDirectories(GetMaterializeFolder());
            foreach (string package in packages)
            {
                if (!keepPaths.Contains(IOUtils.ToShortPath(package).ToLowerInvariant())) continue;
                result += await IOUtils.GetFolderSize(package);
            }

            return result;
        }

        public static async Task<long> GetBackupFolderSize()
        {
            return await IOUtils.GetFolderSize(GetBackupFolder());
        }

        public static async Task<long> GetPreviewFolderSize()
        {
            return await IOUtils.GetFolderSize(GetPreviewFolder());
        }

        internal static async Task ProcessSubPackages(Asset asset, List<AssetFile> subPackages)
        {
            List<AssetFile> unityPackages = subPackages.Where(p => p.IsUnityPackage()).ToList();
            List<AssetFile> archives = subPackages.Where(p => p.IsArchive()).ToList();

            if (unityPackages.Count > 0)
            {
                UnityPackageImporter unityPackageImporter = new UnityPackageImporter();
                Actions.RegisterRunningAction(ActionHandler.ACTION_SUB_PACKAGES_INDEX, unityPackageImporter, "Indexing sub-packages");
                await unityPackageImporter.ProcessSubPackages(asset, unityPackages);
                unityPackageImporter.FinishProgress();
            }

            if (archives.Count > 0)
            {
                ArchiveImporter archiveImporter = new ArchiveImporter();
                Actions.RegisterRunningAction(ActionHandler.ACTION_SUB_PACKAGES_INDEX, archiveImporter, "Indexing sub-archives");
                await archiveImporter.ProcessSubArchives(asset, archives);
                archiveImporter.FinishProgress();
            }
        }

        public static string GetAssetCacheFolder()
        {
            if (_assetCacheFolder != null && (DateTime.Now - _lastAssetCacheCheck).TotalSeconds < FOLDER_CACHE_TIME) return _assetCacheFolder;

            string result;

            try
            {
                // explicit custom configuration always wins
                if (Config.assetCacheLocationType == 1 && !string.IsNullOrWhiteSpace(Config.assetCacheLocation))
                {
                    result = Config.assetCacheLocation;
                }
                // then try what Unity is reporting itself
                else if (!string.IsNullOrWhiteSpace(AssetStore.GetAssetCacheFolder()))
                {
                    result = AssetStore.GetAssetCacheFolder();
                }
                else
                {
                    // environment variable overrides default location
                    string envPath = StringUtils.GetEnvVar("ASSETSTORE_CACHE_PATH");
                    if (!string.IsNullOrWhiteSpace(envPath))
                    {
                        result = envPath;
                    }
                    else
                    {
                        // custom special location (Unity 2022+) overrides default as well, kept in for legacy compatibility
                        string customLocation = Config.folders.FirstOrDefault(f => f.GetLocation(true).EndsWith(ASSET_STORE_FOLDER_NAME))?.GetLocation(true);
                        if (!string.IsNullOrWhiteSpace(customLocation))
                        {
                            result = customLocation;
                        }
                        else
                        {
#if UNITY_EDITOR_WIN
                            result = IOUtils.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", ASSET_STORE_FOLDER_NAME);
#endif
#if UNITY_EDITOR_OSX
                            result = IOUtils.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Unity", ASSET_STORE_FOLDER_NAME);
#endif
#if UNITY_EDITOR_LINUX
                            result = IOUtils.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local/share/unity3d", ASSET_STORE_FOLDER_NAME);
#endif
                        }
                    }
                }
                result = result?.Replace("\\", "/");

                _lastAssetCacheCheck = DateTime.Now;
                _assetCacheFolder = result;
            }
            catch (Exception)
            {
                return _assetCacheFolder;
            }
            return result;
        }

        public static string GetPackageCacheFolder()
        {
            string result;
            if (Config.packageCacheLocationType == 1 && !string.IsNullOrWhiteSpace(Config.packageCacheLocation))
            {
                result = Config.packageCacheLocation;
            }
            else
            {
#if UNITY_EDITOR_WIN
                result = IOUtils.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "cache", "packages");
#endif
#if UNITY_EDITOR_OSX
                result = IOUtils.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Unity", "cache", "packages");
#endif
#if UNITY_EDITOR_LINUX
                result = IOUtils.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config/unity3d/cache/packages");
#endif
            }
            if (result != null) result = result.Replace("\\", "/");

            return result;
        }

        public static async void ClearCache(Action callback = null)
        {
            ClearCacheInProgress = true;
            try
            {
                string cachePath = GetMaterializeFolder();
                if (Directory.Exists(cachePath))
                {
                    List<Asset> keepAssets = DBAdapter.DB.Table<Asset>().Where(a => a.KeepExtracted).ToList();
                    List<string> keepPaths = keepAssets.Select(a => GetMaterializedAssetPath(a).ToLowerInvariant()).ToList();

                    // go through 1 by 1 to keep persisted packages in the cache
                    string[] packages = Directory.GetDirectories(cachePath);
                    foreach (string package in packages)
                    {
                        if (keepPaths.Contains(package.ToLowerInvariant())) continue;
                        await IOUtils.DeleteFileOrDirectory(package);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not delete full cache directory: {e.Message}");
            }

            ClearCacheInProgress = false;
            callback?.Invoke();
        }

        private static void LoadConfig()
        {
            string configLocation = GetConfigLocation();
            UsedConfigLocation = configLocation;

            if (configLocation == null || !File.Exists(configLocation))
            {
                _config = new AssetInventorySettings();
                return;
            }

            ConfigErrors.Clear();
            _config = JsonConvert.DeserializeObject<AssetInventorySettings>(File.ReadAllText(configLocation), new JsonSerializerSettings
            {
                Error = delegate(object _, ErrorEventArgs args)
                {
                    ConfigErrors.Add(args.ErrorContext.Error.Message);

                    Debug.LogError("Invalid config file format: " + args.ErrorContext.Error.Message);
                    args.ErrorContext.Handled = true;
                }
            });
            if (_config == null) _config = new AssetInventorySettings();
            _config.InitUISections();

            // init folders & ensure all paths are in the correct format
            if (_config.folders == null) _config.folders = new List<FolderSpec>();
            _config.folders.ForEach(f => f.location = f.location?.Replace("\\", "/"));

            // init actions
            if (_config.actionStates == null) _config.actionStates = new List<UpdateActionStates>();

            // templates
            if (_config.templateExportSettings.environments == null) _config.templateExportSettings.environments = new List<TemplateExportEnvironment>();
            if (_config.templateExportSettings.environments.Count == 0) _config.templateExportSettings.environments.Add(new TemplateExportEnvironment());
        }

        public static void SaveConfig()
        {
            if (DEBUG_MODE) Debug.LogWarning("SaveConfig");

            string configFile = GetConfigLocation();
            if (configFile == null) return;

            if (_config.reportingBatchSize > 500) _config.reportingBatchSize = 500; // SQLite cannot handle more than that

            try
            {
                File.WriteAllText(configFile, JsonConvert.SerializeObject(_config, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not persist configuration. It might be locked by another application: {e.Message}");
            }
        }

        public static void ResetConfig()
        {
            DBAdapter.Close(); // in case DB path changes

            _config = new AssetInventorySettings();
            SaveConfig();
            AssetDatabase.Refresh();
        }

        public static void ResetUICustomization()
        {
            _config.ResetAdvancedUI();
            SaveConfig();
        }

        internal static void LoadMedia(AssetInfo info, bool download = true)
        {
            // when already downloading don't trigger again
            if (info.IsMediaLoading()) return;

            info.DisposeMedia();
            if (info.ParentInfo != null)
            {
                LoadMedia(info.ParentInfo, download);
                info.AllMedia = info.ParentInfo.AllMedia;
                info.Media = info.ParentInfo.Media;
                return;
            }

            info.AllMedia = DBAdapter.DB.Query<AssetMedia>("select * from AssetMedia where AssetId=? order by [Order]", info.AssetId).ToList();
            info.Media = info.AllMedia
                .Where(m => m.Type == "main"
                    || m.Type == "screenshot"
                    || m.Type == "youtube"
                    || m.Type == "vimeo"
                    || m.Type == "attachment_video"
                    || m.Type == "attachment_audio"
                    || m.Type == "mixcloud"
                    || m.Type == "soundcloud")
                .ToList();
            if (download) DownloadMedia(info);
        }

        internal static async Task LoadFullMediaOnDemand(AssetInfo info, AssetMedia media)
        {
            // If already loaded or currently loading, skip
            if (media.Texture != null || media.IsDownloading) return;

            // Skip for special types that don't need full media
            if (media.Type == "youtube" || media.Type == "vimeo" || media.Type == "sketchfab" ||
                media.Type == "attachment_audio" || media.Type == "attachment_video" ||
                media.Type == "soundcloud" || media.Type == "mixcloud")
            {
                return;
            }

            // Skip if no URL available
            if (string.IsNullOrWhiteSpace(media.Url)) return;

            string targetFile = info.ToAsset().GetMediaFile(media, GetPreviewFolder(), false);

            // Download if not exists
            if (!File.Exists(targetFile))
            {
                media.IsDownloading = true;
                await AssetUtils.LoadImageAsync(media.Url, targetFile);
                media.IsDownloading = false;
            }

            // Load texture from file
            if (File.Exists(targetFile))
            {
                media.Texture = await LoadTextureWithRoundedCorners(targetFile);
            }
        }

        private static async void DownloadMedia(AssetInfo info)
        {
            List<AssetMedia> files = info.Media.Where(m => !m.IsDownloading).OrderBy(m => m.Order).ToList();

            // Process sequentially to avoid overwhelming the system during scrolling
            foreach (AssetMedia file in files)
            {
                await DownloadMediaFileAsync(info, file);
            }
        }

        private static async Task DownloadMediaFileAsync(AssetInfo info, AssetMedia file)
        {
            if (info.Media == null) return; // happens when cancelled
            if (file.IsDownloading) return;

            // thumbnail
            if (!string.IsNullOrWhiteSpace(file.ThumbnailUrl))
            {
                string thumbnailFile = info.ToAsset().GetMediaThumbnailFile(file, GetPreviewFolder(), false);
                if (!File.Exists(thumbnailFile))
                {
                    if (info.Media == null) return; // happens when cancelled
                    file.IsDownloading = true;
                    await AssetUtils.LoadImageAsync(file.ThumbnailUrl, thumbnailFile);
                    if (info.Media == null) return; // happens when cancelled
                    file.IsDownloading = false;
                }
                if (info.Media != null && !file.IsDownloading && File.Exists(thumbnailFile))
                {
                    file.ThumbnailTexture = await LoadTextureWithRoundedCorners(thumbnailFile);
                }
                else
                {
                    // fallback icon
                    file.ThumbnailTexture = ((Texture2D)EditorGUIUtility.IconContent("d_PlayButton").image).MakeReadable();
                }
            }
            else if (file.Type == "attachment_audio")
            {
                file.ThumbnailTexture = ((Texture2D)EditorGUIUtility.IconContent("audioclip icon").image).MakeReadable();
            }

            // Note: Full media is now loaded on-demand via LoadFullMediaOnDemand() to save memory
        }

        private static async Task<Texture2D> LoadTextureWithRoundedCorners(string filePath)
        {
            Texture2D texture = await AssetUtils.LoadLocalTexture(filePath, false);
            if (texture == null) return null;

            if (AI.Config.mediaCornerRadius > 0)
            {
                Texture2D roundedTexture = texture.WithRoundedCorners(AI.Config.mediaCornerRadius);
                // Dispose of the original texture since we only need the rounded version
                UnityEngine.Object.DestroyImmediate(texture);
                return roundedTexture;
            }

            return texture;
        }

        public static int CountPurchasedAssets(IEnumerable<AssetInfo> assets)
        {
            return assets.Count(a => a.ParentId == 0 && (a.AssetSource == Asset.Source.AssetStorePackage || (a.AssetSource == Asset.Source.RegistryPackage && a.ForeignId > 0)));
        }

        public static void ForgetAssetFile(AssetFile info)
        {
            DBAdapter.DB.Execute("DELETE from AssetFile where Id=?", info.Id);
            DBAdapter.DB.Execute("DELETE from TagAssignment where TagTarget=? and TargetId=?", TagAssignment.Target.Asset, info.Id);
            DBAdapter.DB.Execute("DELETE from MetadataAssignment where MetadataTarget=? and TargetId=?", MetadataAssignment.Target.Asset, info.Id);
        }

        public static Asset ForgetPackage(AssetInfo info, bool removeExclusion = false)
        {
            // delete child packages first
            foreach (AssetInfo childInfo in info.ChildInfos)
            {
                RemovePackage(childInfo, true);
            }

            DBAdapter.DB.Execute("DELETE from AssetFile where AssetId=?", info.AssetId);
            // TODO: remove assetfile tag assignments

            Asset existing = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (existing == null) return null;

            existing.CurrentState = Asset.State.New;
            info.CurrentState = Asset.State.New;
            existing.LastOnlineRefresh = DateTime.MinValue;
            info.LastOnlineRefresh = DateTime.MinValue;
            existing.ETag = null;
            info.ETag = null;
            if (removeExclusion)
            {
                existing.Exclude = false;
                info.Exclude = false;
            }

            DBAdapter.DB.Update(existing);

            return existing;
        }

        public static void RemovePackage(AssetInfo info, bool deleteFiles)
        {
            // delete child packages first
            foreach (AssetInfo childInfo in info.ChildInfos)
            {
                RemovePackage(childInfo, deleteFiles);
            }

            if (deleteFiles && info.ParentId == 0)
            {
                if (File.Exists(info.GetLocation(true)))
                {
                    IOUtils.TryDeleteFile(info.GetLocation(true));
                }
                if (Directory.Exists(info.GetLocation(true)))
                {
                    Task.Run(() => IOUtils.DeleteFileOrDirectory(info.GetLocation(true)));
                }
            }
            string previewFolder = Path.Combine(GetPreviewFolder(), info.AssetId.ToString());
            if (Directory.Exists(previewFolder))
            {
                Task.Run(() => IOUtils.DeleteFileOrDirectory(previewFolder));
            }

            Asset existing = ForgetPackage(info);
            if (existing == null) return;

            DBAdapter.DB.Execute("DELETE from AssetMedia where AssetId=?", info.AssetId);
            DBAdapter.DB.Execute("DELETE from TagAssignment where TagTarget=? and TargetId=?", TagAssignment.Target.Package, info.AssetId);
            DBAdapter.DB.Execute("DELETE from MetadataAssignment where MetadataTarget=? and TargetId=?", MetadataAssignment.Target.Package, info.AssetId);
            DBAdapter.DB.Execute("DELETE from Asset where Id=?", info.AssetId);

            info.ForeignId = 0; // reset foreign ID to avoid online refreshes 
        }

        public static async Task<string> CopyTo(AssetInfo info, string folder, bool withDependencies = false, bool withScripts = false, bool fromDragDrop = false, bool outOfProject = false, bool reimport = false, bool previewMode = false)
        {
            // copy over SRP support reference if required for main file
            AssetInfo workInfo = info;
            if (info.SRPMainReplacement != null)
            {
                workInfo = new AssetInfo()
                    .CopyFrom(workInfo, false)
                    .CopyFrom(info.SRPSupportPackage, info.SRPMainReplacement);
            }

            string sourcePath = await EnsureMaterializedAsset(workInfo);
            bool conversionNeeded = false;
            if (sourcePath == null) return null;

            string finalPath = folder;

            // complex import structure only supported for Unity Packages
            int finalImportStructure = workInfo.AssetSource == Asset.Source.CustomPackage ||
                workInfo.AssetSource == Asset.Source.Archive ||
                workInfo.AssetSource == Asset.Source.RegistryPackage ||
                workInfo.AssetSource == Asset.Source.AssetStorePackage
                    ? Config.importStructure
                    : 0;

            // calculate dependencies on demand
            while (workInfo.DependencyState == AssetInfo.DependencyStateOptions.Calculating) await Task.Yield();
            if (withDependencies && (info.DependencyState == AssetInfo.DependencyStateOptions.Unknown || info.DependencyState == AssetInfo.DependencyStateOptions.Partial))
            {
                await CalculateDependencies(workInfo);
            }

            // override again for single files without dependencies in drag & drop scenario as that feels more natural
            if (fromDragDrop && (workInfo.Dependencies == null || workInfo.Dependencies.Count == 0)) finalImportStructure = 0;

            switch (finalImportStructure)
            {
                case 0:
                    // put into subfolder if multiple files are affected
                    if (withDependencies && workInfo.Dependencies != null && workInfo.Dependencies.Count > 0)
                    {
                        finalPath = Path.Combine(finalPath.RemoveTrailing("."), Path.GetFileNameWithoutExtension(workInfo.FileName)).Trim().RemoveTrailing(".");
                        Directory.CreateDirectory(finalPath);
                    }
                    break;

                case 1:
                    string path = workInfo.Path;
                    if (path.ToLowerInvariant().StartsWith("assets/")) path = path.Substring(7);
                    finalPath = Path.Combine(
                        folder,
                        workInfo.AssetSource == Asset.Source.RegistryPackage && !previewMode ? workInfo.SafeName : "",
                        Path.GetDirectoryName(path));
                    break;
            }

            HashSet<string> importedFiles = new HashSet<string>();
            string targetPath = Path.Combine(finalPath, Path.GetFileName(sourcePath));
            if (previewMode) targetPath = targetPath.Replace("~", ""); // otherwise asset database will not create previews for it
            targetPath = await DoCopyTo(workInfo, sourcePath, targetPath, reimport, outOfProject);
            if (targetPath == null) return null; // error occurred
            importedFiles.Add(targetPath);

            string result = targetPath;
            if (ConversionExtensions.Contains(IOUtils.GetExtensionWithoutDot(targetPath).ToLowerInvariant())) conversionNeeded = true;

            if (withDependencies)
            {
                List<AssetFile> deps = withScripts ? workInfo.Dependencies : workInfo.MediaDependencies;
                if (deps != null)
                {
                    for (int i = 0; i < deps.Count; i++)
                    {
                        if (ConversionExtensions.Contains(IOUtils.GetExtensionWithoutDot(deps[i].FileName).ToLowerInvariant())) conversionNeeded = true;

                        // special handling for Asset Manager assets, as they will bring in dependencies automatically
                        if (workInfo.AssetSource == Asset.Source.AssetManager) continue;

                        // select correct asset from pool
                        Asset asset = workInfo.CrossPackageDependencies.FirstOrDefault(p => p.Id == deps[i].AssetId);
                        if (asset == null)
                        {
                            // if not found this is either the SRP original or the current asset
                            asset = workInfo.SRPSupportPackage == null ? workInfo.ToAsset() : workInfo.SRPOriginalBackup.ToAsset();
                        }

                        sourcePath = await EnsureMaterializedAsset(asset, deps[i]);
                        if (sourcePath != null)
                        {
                            switch (finalImportStructure)
                            {
                                case 0:
                                    targetPath = Path.Combine(finalPath, Path.GetFileName(deps[i].Path));
                                    break;

                                case 1:
                                    string path = deps[i].Path;
                                    string lowerPath = path.ToLowerInvariant();

                                    // Handle both relative paths (assets/...) and absolute paths (.../Assets/...)
                                    // Check if path starts with "assets/" or contains "/assets/" (standalone directory)
                                    int assetsIndex = -1;
                                    if (lowerPath.StartsWith("assets/"))
                                    {
                                        assetsIndex = 0;
                                    }
                                    else
                                    {
                                        int slashIndex = lowerPath.IndexOf("/assets/", StringComparison.OrdinalIgnoreCase);
                                        if (slashIndex >= 0) assetsIndex = slashIndex + 1;
                                    }

                                    if (assetsIndex >= 0)
                                    {
                                        path = path.Substring(assetsIndex + 7); // Skip "assets/"
                                    }

                                    targetPath = Path.Combine(
                                        folder,
                                        asset.AssetSource == Asset.Source.RegistryPackage && !previewMode ? asset.SafeName : "",
                                        path);
                                    break;
                            }

                            AssetInfo depInfo = new AssetInfo().CopyFrom(asset, deps[i]);
                            if (previewMode) targetPath = targetPath.Replace("~", "");
                            targetPath = await DoCopyTo(depInfo, sourcePath, targetPath, reimport, outOfProject);
                            if (targetPath == null) return null; // error occurred
                            importedFiles.Add(targetPath);
                        }
                    }
                }
                else
                {
                    Debug.LogError($"Dependency calculation failed for '{workInfo}'.");
                }
            }
            AssetDatabase.Refresh();

            if (string.IsNullOrEmpty(info.Guid))
            {
                // special case of original index without GUID, fall back to file check only
                if (File.Exists(targetPath)) info.ProjectPath = targetPath;
            }
            else
            {
                info.ProjectPath = AssetDatabase.GUIDToAssetPath(workInfo.Guid);
            }

            if (Config.convertToPipeline && conversionNeeded && info.SRPSupportPackage == null)
            {
                RunURPConverter();
            }

            // do post steps after all files are materialized as otherwise nested prefab operations will fail
            foreach (string file in importedFiles)
            {
                PerformPostImportOperations(file);
            }

            Config.statsImports++;
            SaveConfig();

            return result;
        }

        public static void RunURPConverter()
        {
#if USE_URP_CONVERTER
            if (AssetUtils.IsOnURP())
            {
                try
                {
                    Converters.RunInBatchMode(
                        ConverterContainerId.BuiltInToURP
                        , new List<ConverterId>
                        {
                            ConverterId.Material,
                            ConverterId.ReadonlyMaterial
                        }
                        , ConverterFilter.Inclusive
                    );
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not run URP converter: {e.Message}");
                }
            }
#endif
        }

        private static void PerformPostImportOperations(string path)
        {
            if (!Config.removeLODs) return;

            string type = IOUtils.GetExtensionWithoutDot(path).ToLowerInvariant();
            switch (type)
            {
                case "prefab":
                    if (Config.removeLODs) AssetUtils.RemoveLODGroups(path);
                    break;
            }
        }

        private static async Task<string> DoCopyTo(AssetInfo info, string sourcePath, string targetPath, bool reimport = false, bool outOfProject = false)
        {
            try
            {
                bool isDirectory = Directory.Exists(sourcePath);
                if (!outOfProject && !isDirectory)
                {
                    // don't copy to different location if existing already, override instead
                    string existing = AssetDatabase.GUIDToAssetPath(info.Guid);
                    if (!string.IsNullOrWhiteSpace(existing) && !existing.Contains(TEMP_FOLDER) && File.Exists(existing))
                    {
                        targetPath = existing;
                        if (!reimport || targetPath.StartsWith("Packages/")) return targetPath;
                    }
                }

                targetPath = IOUtils.ToLongPath(AssetUtils.AddProjectRoot(IOUtils.ToShortPath(targetPath)));

                string targetFolder = Path.GetDirectoryName(targetPath);
                Directory.CreateDirectory(targetFolder);

                // special handling for directory assets, e.g. complex Asset Manager assets with dependencies
                if (isDirectory)
                {
                    // copy contents of source path to target path
                    string[] files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);

                    // Record created directories to avoid repeated checks
                    HashSet<string> createdDirs = new HashSet<string>();
                    foreach (string file in files)
                    {
                        string relativePath = file.Substring(sourcePath.Length + 1);
                        string targetFile = Path.Combine(targetFolder, relativePath);
                        string targetFolder2 = Path.GetDirectoryName(targetFile);

                        if (createdDirs.Add(targetFolder2))
                        {
                            Directory.CreateDirectory(targetFolder2);
                        }

                        // Use TryCopyFile for consistency and retry logic
                        if (!await IOUtils.TryCopyFile(file, targetFile, true))
                        {
                            Debug.LogWarning($"Failed to copy file '{file}' to '{targetFile}'");
                        }
                    }
                    return AssetUtils.RemoveProjectRoot(targetPath);
                }

                // this can (very seldom) fail in parallel calls when the file is already in use
                if (!await IOUtils.TryCopyFile(sourcePath, targetPath, true)) throw new Exception("Source file might be locked by another process.");

                string sourceMetaPath = sourcePath + ".meta";
                string targetMetaPath = targetPath + ".meta";
                if (File.Exists(sourceMetaPath))
                {
                    if (!await IOUtils.TryCopyFile(sourceMetaPath, targetMetaPath, true)) throw new Exception("Meta file might be locked by another process.");

                    // adjust meta file to contain asset origin
                    string[] metaContent = File.ReadAllLines(targetMetaPath);
                    if (!metaContent.Any(l => l.StartsWith("AssetOrigin:")))
                    {
                        AssetOrigin origin = info.ToAsset().GetAssetOrigin();
                        string assetPath = targetPath.Replace("\\", "/");
                        try
                        {
                            origin.assetPath = assetPath.Substring(assetPath.IndexOf("Assets/", StringComparison.Ordinal));
                        }
                        catch (Exception e)
                        {
                            if (!outOfProject) Debug.LogError($"Could not determine asset path from '{assetPath}': {e.Message}");
                        }
                        List<string> newMetaContent = new List<string>(metaContent)
                        {
                            "AssetOrigin:",
                            "  serializedVersion: 1",
                            $"  productId: {origin.productId}",
                            $"  packageName: {origin.packageName}",
                            $"  packageVersion: {origin.packageVersion}",
                            $"  assetPath: {origin.assetPath}",
                            $"  uploadId: {origin.uploadId}"
                        };
                        File.WriteAllLines(targetMetaPath, newMetaContent);
                    }
                }

                return AssetUtils.RemoveProjectRoot(targetPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error copying file '{sourcePath}' to '{targetPath}': {e.Message}");
                return null;
            }
        }

        public static async Task PlayAudio(AssetInfo info, CancellationToken ct = default(CancellationToken))
        {
            string targetPath;

            // check if in project already, then skip extraction
            if (info.InProject)
            {
                targetPath = IOUtils.PathCombine(Path.GetDirectoryName(Application.dataPath), info.ProjectPath);
            }
            else
            {
                targetPath = await EnsureMaterializedAsset(info, Config.extractSingleFiles, ct);
                if (targetPath != null && !Config.extractSingleFiles && Config.keepExtractedOnAudio && !info.KeepExtracted)
                {
                    // ensure extraction is set to true for future audio playback
                    SetAssetExtraction(info, true);
                }
            }
#if !ASSET_INVENTORY_NOAUDIO
            EditorAudioUtility.StopAllPreviewClips();
            if (targetPath != null)
            {
                AudioClip clip = await AssetUtils.LoadAudioFromFile(targetPath);

                if (clip != null)
                {
                    EditorAudioUtility.StopAllPreviewClips();
                    EditorAudioUtility.PlayPreviewClip(clip, 0, Config.loopAudio);
                }
            }
#endif
        }

        public static void StopAudio()
        {
#if !ASSET_INVENTORY_NOAUDIO
            EditorAudioUtility.StopAllPreviewClips();
#endif
        }

        internal static void SetAssetExclusion(AssetInfo info, bool exclude)
        {
            Asset asset = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (asset == null) return;

            asset.Exclude = exclude;
            info.Exclude = exclude;

            DBAdapter.DB.Update(asset);
        }

        internal static void SetAssetBackup(AssetInfo info, bool backup, bool invokeUpdate = true)
        {
            Asset asset = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (asset == null) return;

            asset.Backup = backup;
            info.Backup = backup;

            DBAdapter.DB.Update(asset);

            if (invokeUpdate) OnPackagesUpdated?.Invoke();
        }

        internal static void SetAssetAIUse(AssetInfo info, bool useAI, bool invokeUpdate = true)
        {
            Asset asset = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (asset == null) return;

            asset.UseAI = useAI;
            info.UseAI = useAI;

            DBAdapter.DB.Update(asset);

            if (invokeUpdate) OnPackagesUpdated?.Invoke();
        }

        internal static bool ShowAdvanced()
        {
            return !Config.hideAdvanced || Event.current.control;
        }

        internal static void SetVersion(AssetInfo info, string version)
        {
            Asset asset = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (asset == null) return;

            asset.Version = version;
            info.Version = version;

            DBAdapter.DB.Update(asset);
        }

        internal static void SetPackageVersion(AssetInfo info, PackageInfo package)
        {
            Asset asset = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (asset == null) return;

            asset.LatestVersion = package.versions.latestCompatible;
            info.LatestVersion = package.versions.latestCompatible;

            DBAdapter.DB.Update(asset);
        }

        internal static void SetAssetExtraction(AssetInfo info, bool extract)
        {
            Asset asset = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (asset == null) return;

            asset.KeepExtracted = extract;
            info.KeepExtracted = extract;

            if (extract) _extractionQueue.Enqueue(asset);

            DBAdapter.DB.Update(asset);
        }

        internal static void SetAssetUpdateStrategy(AssetInfo info, Asset.Strategy strategy)
        {
            Asset asset = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (asset == null) return;

            asset.UpdateStrategy = strategy;
            info.UpdateStrategy = strategy;

            DBAdapter.DB.Update(asset);
        }

        internal static void LoadRelativeLocations()
        {
            string curSystem = GetSystemId();

            string dataQuery = "SELECT * from RelativeLocation order by Key, Location";
            List<RelativeLocation> locations = DBAdapter.DB.Query<RelativeLocation>($"{dataQuery}").ToList();
            locations.ForEach(l => l.SetLocation(l.Location)); // ensure all paths use forward slashes

            // ensure additional folders don't contain additional unmapped keys (e.g. after database cleanup)
            foreach (FolderSpec spec in Config.folders)
            {
                if (!string.IsNullOrWhiteSpace(spec.relativeKey) && !locations.Any(rl => rl.Key == spec.relativeKey))
                {
                    // self-heal
                    RelativeLocation rel = new RelativeLocation();
                    rel.System = curSystem;
                    rel.Key = spec.relativeKey;
                    DBAdapter.DB.Insert(rel);
                    locations.Add(rel);
                }
            }

            _relativeLocations = locations.Where(l => l.System == curSystem).ToList();

            // add predefined locations
            _relativeLocations.Insert(0, new RelativeLocation("ac", curSystem, GetAssetCacheFolder()));
            _relativeLocations.Insert(1, new RelativeLocation("pc", curSystem, GetPackageCacheFolder()));

            foreach (RelativeLocation location in locations.Where(l => l.System != curSystem))
            {
                // add key as undefined if not there
                if (!_relativeLocations.Any(rl => rl.Key == location.Key))
                {
                    _relativeLocations.Add(new RelativeLocation(location.Key, curSystem, null));
                }

                // add location inside other systems for reference
                RelativeLocation loc = _relativeLocations.First(rl => rl.Key == location.Key);
                if (loc.otherLocations == null) loc.otherLocations = new List<string>();
                loc.otherLocations.Add(location.Location);
            }

            // ensure never null
            _relativeLocations.ForEach(rl =>
            {
                if (rl.otherLocations == null) rl.otherLocations = new List<string>();
            });

            _userRelativeLocations = _relativeLocations.Where(rl => rl.Key != "ac" && rl.Key != "pc").ToList();
        }

        internal static void ConnectToAssetStore(AssetInfo info, AssetDetails details)
        {
            Asset existing = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (existing == null) return;

            existing.ETag = null;
            info.ETag = null;
            existing.ForeignId = int.Parse(details.packageId);
            info.ForeignId = int.Parse(details.packageId);
            existing.LastOnlineRefresh = DateTime.MinValue;
            info.LastOnlineRefresh = DateTime.MinValue;

            DBAdapter.DB.Update(existing);
        }

        internal static void DisconnectFromAssetStore(AssetInfo info, bool removeMetadata)
        {
            Asset existing = DBAdapter.DB.Find<Asset>(info.AssetId);
            if (existing == null) return;

            existing.ForeignId = 0;
            info.ForeignId = 0;

            if (removeMetadata)
            {
                existing.AssetRating = 0;
                info.AssetRating = 0;
                existing.SafePublisher = null;
                info.SafePublisher = null;
                existing.DisplayPublisher = null;
                info.DisplayPublisher = null;
                existing.SafeCategory = null;
                info.SafeCategory = null;
                existing.DisplayCategory = null;
                info.DisplayCategory = null;
                existing.DisplayName = null;
                info.DisplayName = null;
                existing.OfficialState = null;
                info.OfficialState = null;
                existing.PriceCny = 0;
                info.PriceCny = 0;
                existing.PriceEur = 0;
                info.PriceEur = 0;
                existing.PriceUsd = 0;
                info.PriceUsd = 0;
            }

            DBAdapter.DB.Update(existing);
        }

        internal static string CreateDebugReport()
        {
            string result = "Asset Inventory Support Diagnostics\n";
            result += $"\nDate: {DateTime.Now}";
            result += $"\nVersion: {VERSION}";
            result += $"\nUnity: {Application.unityVersion}";
            result += $"\nPlatform: {Application.platform}";
            result += $"\nOS: {Environment.OSVersion}";
            result += $"\nLanguage: {Application.systemLanguage}";

            List<AssetInfo> assets = LoadAssets();
            result += $"\n\n{assets.Count} Packages";
            foreach (AssetInfo asset in assets)
            {
                result += $"\n{asset} ({asset.SafeName}) - {asset.AssetSource} - {asset.GetVersion()}";
            }

            List<Tag> tags = Tagging.LoadTags();
            result += $"\n\n{tags.Count} Tags";
            foreach (Tag tag in tags)
            {
                result += $"\n{tag} ({tag.Id})";
            }

            result += $"\n\n{Tagging.Tags.Count()} Tag Assignments";
            foreach (TagInfo tag in Tagging.Tags)
            {
                result += $"\n{tag})";
            }

            return result;
        }

        internal static string GetSystemId()
        {
            return SystemInfo.deviceUniqueIdentifier; // + "test";
        }

        internal static bool IsRel(string path)
        {
            return path != null && path.StartsWith(TAG_START);
        }

        internal static string GetRelKey(string path)
        {
            return path.Replace(TAG_START, "").Replace(TAG_END, "");
        }

        internal static string DeRel(string path, bool emptyIfMissing = false)
        {
            if (path == null) return null;
            if (!IsRel(path)) return path;

            foreach (RelativeLocation location in RelativeLocations)
            {
                string segment = $"{TAG_START}{location.Key}{TAG_END}";

                if (string.IsNullOrWhiteSpace(location.Location))
                {
                    if (emptyIfMissing && path.Contains(segment))
                    {
                        return null;
                    }
                    continue;
                }

                path = path.Replace(segment, location.Location);
            }

            // check if some rule caught it
            if (IsRel(path) && emptyIfMissing) return null;

            return path;
        }

        internal static string MakeRelative(string path)
        {
            path = IOUtils.ToShortPath(path.Replace("\\", "/"));

            StringBuilder sb = new StringBuilder(path);
            foreach (RelativeLocation location in RelativeLocations)
            {
                if (string.IsNullOrWhiteSpace(location.Location)) continue;

                string oldPath = location.Location;
                if (path.Contains(oldPath))
                {
                    string newPath = $"{TAG_START}{location.Key}{TAG_END}";

                    sb.Replace(oldPath, newPath);
                }
            }

            return sb.ToString();
        }

        internal static AssetInfo GetAssetByPath(string path, Asset asset)
        {
            string query = "SELECT *, AssetFile.Id as Id from AssetFile left join Asset on Asset.Id = AssetFile.AssetId where Lower(AssetFile.Path) = ? and Asset.Id = ?";
            List<AssetInfo> result = DBAdapter.DB.Query<AssetInfo>(query, path.ToLowerInvariant(), asset.Id);

            return result.FirstOrDefault();
        }

        internal static void RegisterSelection(List<AssetInfo> assets)
        {
            GetObserver().SetPrioritized(assets);
        }

        public static void TriggerPackageRefresh()
        {
            OnPackagesUpdated?.Invoke();
        }

        public static void TriggerPackageImageRefresh(Asset asset)
        {
            OnPackageImageLoaded?.Invoke(asset);
        }

        internal static void SetPipelineConversion(bool active)
        {
            Config.convertToPipeline = active;
            SaveConfig();
        }

        public static void OpenStoreURL(string url)
        {
            AskForAffiliate();
            if (Config.useAffiliateLinks) url += $"?{AFFILIATE_PARAM}";
            Application.OpenURL(url);
        }

        internal static void AskForAffiliate()
        {
            if (!Config.askedForAffiliateLinks)
            {
                Config.askedForAffiliateLinks = true;
                Config.useAffiliateLinks = EditorUtility.DisplayDialog("Support Further Development", "When opening links to the Asset Store, Asset Inventory can add a small affiliate parameter to the link. This helps support the future development of Asset Inventory, and has no cost or negative effect on you. You can opt out in settings at any time. Would you like to turn this on?", "Yes", "No");
                SaveConfig();
            }
        }

#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
        private static CloudAssetManagement _cam;
        internal static async Task<CloudAssetManagement> GetCloudAssetManagement()
        {
            await PlatformServices.InitOnDemand();
            if (_cam == null) _cam = new CloudAssetManagement();

            return _cam;
        }
#endif
        private class AssetEntry
        {
            public string Name;
            public bool IsSubPackage;
            public string GroupKey;
        }

        public static void DebugLog(string text)
        {
            Debug.Log("[Asset Inventory] " + text);
        }
    }
}
