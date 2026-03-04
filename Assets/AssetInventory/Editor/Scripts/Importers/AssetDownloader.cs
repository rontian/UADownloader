using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace AssetInventory
{
    public sealed class AssetDownloader
    {
        private const int HEADER_CACHE_PERIOD = 600;
        private const int START_GRACE_PERIOD = 5;
        private const int DOWNLOAD_STUCK_TIMEOUT = 30;

        public enum State
        {
            Initializing,
            Unavailable,
            Unknown,
            Downloading,
            Paused,
            Downloaded,
            UpdateAvailable
        }

        public DateTime lastRefresh = DateTime.MinValue;

        private AssetInfo _asset;
        private bool _expectFullDownload;
        private readonly AssetDownloadState _assetState = new AssetDownloadState();

        // caching
        private readonly TimedCache<int> _headerCache = new TimedCache<int>();

        public AssetDownloader(AssetInfo asset)
        {
            _asset = asset.GetRoot();
            AssetDownloaderUtils.OnDownloadSuccessful += OnDownloadSuccessful;
            AssetDownloaderUtils.OnDownloadFinished += OnDownloadFinished;
        }

        public AssetInfo GetAsset()
        {
            return _asset;
        }

        public void SetAsset(AssetInfo asset)
        {
            _asset = asset.GetRoot();
        }

        private void OnDownloadSuccessful(int foreignId)
        {
            if (_asset.ForeignId != foreignId) return;

            // update early in assumption it worked, reindexing will correct it if necessary
            _asset.Version = _asset.LatestVersion;
            DBAdapter.DB.Execute("update Asset set CurrentSubState=0, Version=? where Id=?", _asset.LatestVersion, _asset.AssetId);
            _asset.Refresh();
            _asset.PackageDownloader?.RefreshState();
        }

        private void OnDownloadFinished(int foreignId)
        {
            if (_asset.ForeignId != foreignId) return;

            // would be good to do but this will fill up the recycle bin on Windows, let Unity manage this
            // if (_expectFullDownload) _assetState.DeleteTempFiles();
        }

        public bool IsDownloadSupported()
        {
#if UNITY_2020_1_OR_NEWER
            return true;
#else
            // loading assembly will fail below 2020
            return false;
#endif
        }

        public AssetDownloadState GetState()
        {
            return _assetState;
        }

        public void RefreshState()
        {
            lastRefresh = DateTime.Now;
            _headerCache.Clear();

            try
            {
                CheckState();
            }
            catch (Exception)
            {
                // can happen if Unity just finished downloading, checkAssetDownload can trigger an exception then from Unity side, usually ok to ignore, do not change state yet
                // Debug.LogError($"Error checking download state for '{_asset.GetDisplayName()}': {e.Message}\n{e.StackTrace}. This may occur with network drives or locked files.");
                // _assetState.SetState(State.Unknown);
                return;
            }

            // TODO: do whenever file changes, not here?
            if (_assetState.state == State.Downloading)
            {
                _assetState.bytesTotal = _asset.PackageSize;
                if (_assetState.bytesTotal > 0) _assetState.progress = (float)_assetState.bytesDownloaded / _assetState.bytesTotal;
            }
        }

        private void CheckState()
        {
            string targetFile = _asset.GetCalculatedLocation();
            if (targetFile == null)
            {
                _assetState.SetState(State.Unknown);
                return;
            }

            string folder = Path.GetDirectoryName(targetFile);
            if (string.IsNullOrEmpty(folder))
            {
                Debug.LogWarning($"Invalid target folder for asset '{_asset.GetDisplayName()}'");
                _assetState.SetState(State.Unknown);
                return;
            }

            // see if any progress file is there
            FileInfo curFileInfo = null;
            FileInfo dlFileInfo = null;
            FileInfo redlFileInfo = null;

            try
            {
                _assetState.downloadFile = Path.Combine(folder, $".{_asset.SafeName}-{_asset.ForeignId}.tmp");
                _assetState.reDownloadFile = Path.Combine(folder, $".{_asset.SafeName}-content__{_asset.ForeignId}.tmp");
                _assetState.downloadInfoFile = _assetState.downloadFile + ".json";
                _assetState.reDownloadInfoFile = _assetState.reDownloadFile + ".json";

                if (File.Exists(_assetState.downloadFile)) dlFileInfo = new FileInfo(_assetState.downloadFile);
                if (File.Exists(_assetState.reDownloadFile)) redlFileInfo = new FileInfo(_assetState.reDownloadFile);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error accessing download files for '{_asset.GetDisplayName()}': {e.Message}");
                _assetState.SetState(State.Unknown);
                return;
            }

            // Unity sometimes uses the redl file also for initial downloading (6000.0.2f1)
            if (dlFileInfo != null && redlFileInfo != null)
            {
                // both files exist, check which one is newer
                curFileInfo = dlFileInfo.LastWriteTime > redlFileInfo.LastWriteTime ? dlFileInfo : redlFileInfo;
            }
            else if (dlFileInfo != null)
            {
                curFileInfo = dlFileInfo;
            }
            else if (redlFileInfo != null)
            {
                curFileInfo = redlFileInfo;
            }

            if (curFileInfo != null)
            {
                try
                {
                    curFileInfo.Refresh(); // to ensure also data on network drives is up-to-date
                    _assetState.curDownloadFile = curFileInfo.FullName;
                    _assetState.curDownloadInfoFile = curFileInfo.FullName + ".json";
                    _assetState.bytesDownloaded = curFileInfo.Length;
                    _assetState.lastDownloadChange = curFileInfo.LastWriteTime;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Error accessing download progress file for '{_asset.GetDisplayName()}': {e.Message}. This may happen with network drives.");
                    _assetState.SetState(State.Unknown);
                    return;
                }

                // check if paused
                bool isUnityDownloading = IsUnityDownloading();
                if (!isUnityDownloading && File.Exists(_assetState.downloadInfoFile))
                {
                    _assetState.SetState(State.Paused);
                    return;
                }
                _assetState.SetState(State.Downloading);

                if (!isUnityDownloading && DateTime.Now - _assetState.lastDownloadChange > TimeSpan.FromSeconds(DOWNLOAD_STUCK_TIMEOUT))
                {
                    // if no change after a while, assume download is stuck
                    // try to clean up left over tmp files which are likely broken
                    _assetState.DeleteTempFiles();
                    _assetState.SetState(State.Unavailable);
                }
                return;
            }

            // give started downloads some time to settle
            if (_assetState.state == State.Downloading && DateTime.Now - _assetState.lastStateChange < TimeSpan.FromSeconds(START_GRACE_PERIOD)) return;

            bool exists = File.Exists(targetFile);

            // check if package actually contains content for this asset
            if (exists)
            {
                int id = 0;
                if (_headerCache.TryGetValue(out int cachedId))
                {
                    id = cachedId;
                }
                else
                {
                    AssetHeader header = UnityPackageImporter.ReadHeader(targetFile, true);
                    if (header != null && int.TryParse(header.id, out int parsedId))
                    {
                        id = parsedId;
                    }
                    _headerCache.SetValue(id, TimeSpan.FromSeconds(HEADER_CACHE_PERIOD));
                }
                if (id > 0 && id != _asset.ForeignId)
                {
                    _assetState.SetState(State.Unavailable);
                    return;
                }
            }

            // update database location once file is downloaded
            string assetLocation = _asset.GetLocation(true);
            if (exists && string.IsNullOrEmpty(assetLocation))
            {
                _asset.SetLocation(targetFile);
                _asset.Refresh();

                // work directly on db to make sure it's the latest state
                DBAdapter.DB.Execute("update Asset set Location=? where Id=?", _asset.Location, _asset.AssetId);
                _assetState.SetState(State.Downloaded);
                return;
            }

            exists = exists || (!string.IsNullOrEmpty(assetLocation) && File.Exists(assetLocation));
            _assetState.SetState(exists ? (_asset.IsUpdateAvailable() ? State.UpdateAvailable : State.Downloaded) : State.Unavailable);
        }

        public void Download(bool expectFullDownload)
        {
            if (_asset.ParentId > 0) return; // safety check as this might otherwise lead to cache duplications
            if (!IsDownloadSupported()) return;

            // Check available disk space before starting download
            string targetFile = _asset.GetCalculatedLocation();
            if (targetFile != null)
            {
                string folder = Path.GetDirectoryName(targetFile);
                long freeSpace = IOUtils.GetFreeSpace(folder);
                long required = _asset.PackageSize * 2; // 2x for safety margin

                if (freeSpace >= 0 && freeSpace < required)
                {
                    Debug.LogError($"Cannot download '{_asset.GetDisplayName()}': Insufficient disk space. " +
                        $"Required: {StringUtils.FormatBytes(required)}, " +
                        $"Available: {StringUtils.FormatBytes(freeSpace)}");
                    _assetState.SetState(State.Unavailable);
                    return;
                }
            }

            _expectFullDownload = expectFullDownload;

            Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
            Type asc = assembly.GetType("UnityEditor.AssetStoreUtils");
            MethodInfo download = asc.GetMethod("Download", BindingFlags.Public | BindingFlags.Static);
            if (download == null)
            {
                Debug.LogError("Download method not found in UnityEditor.AssetStoreUtils. This is unexpected and hints to an incompatibility you should report to the asset author.");
                return;
            }
            Type downloadDone = assembly.GetType("UnityEditor.AssetStoreUtils+DownloadDoneCallback");
            Delegate onDownloadDone = Delegate.CreateDelegate(downloadDone, typeof (AssetDownloaderUtils), "OnDownloadDone");

            DownloadState dls = new DownloadState
            {
                download = new DownloadStateDetails
                {
                    url = _asset.OriginalLocation,
                    key = _asset.OriginalLocationKey
                }
            };
            bool doResume = false;
            if (_assetState.state == State.Paused && File.Exists(_assetState.downloadInfoFile))
            {
                // Use ReadAllTextWithShare to avoid locking download state files
                DownloadState existingDls = JsonConvert.DeserializeObject<DownloadState>(IOUtils.ReadAllTextWithShare(_assetState.downloadInfoFile));
                doResume = existingDls != null && existingDls.download.key == dls.download.key && existingDls.download.url == dls.download.url;
            }
            string json = JsonConvert.SerializeObject(dls);

            _assetState.SetState(State.Downloading);
            _assetState.bytesTotal = _asset.PackageSize;
            _assetState.bytesDownloaded = 0;

            string key = _asset.ForeignId.ToString();
            ThreadUtils.InvokeOnMainThread(download, null, new object[]
            {
                key, _asset.OriginalLocation,
                new[] {_asset.SafePublisher, _asset.SafeCategory, _asset.SafeName},
                _asset.OriginalLocationKey, json, doResume, onDownloadDone
            });

            _asset.Refresh(true);
        }

        public async void PauseDownload(bool fullAbort)
        {
            Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
            Type asc = assembly.GetType("UnityEditor.AssetStoreUtils");
            MethodInfo abortDownloadMethod = asc.GetMethod("AbortDownload", BindingFlags.Public | BindingFlags.Static);

            // this will not really abort but simply stop at the current state which can be resumed later
            abortDownloadMethod?.Invoke(null, new object[] {new[] {_asset.SafePublisher, _asset.SafeCategory, _asset.SafeName}});

            if (fullAbort)
            {
                // let Unity close the files
                await Task.Delay(2000);

                // delete tmp files
                _assetState.DeleteTempFiles();

                AI.TriggerPackageRefresh();
            }
        }

        public bool IsUnityDownloading()
        {
            if (!IsDownloadSupported()) return false;

            Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
            Type asc = assembly.GetType("UnityEditor.AssetStoreUtils");
            MethodInfo checkAssetDownload = asc.GetMethod("CheckDownload", BindingFlags.Public | BindingFlags.Static);

            string key = _asset.ForeignId.ToString();
            string result = (string)checkAssetDownload?.Invoke(null, new object[] {key, _asset.OriginalLocation, new[] {_asset.SafePublisher, _asset.SafeCategory, _asset.SafeName}, _asset.OriginalLocationKey});
            if (string.IsNullOrEmpty(result)) return false;

            DownloadState state = JsonConvert.DeserializeObject<DownloadState>(result);

            return state.inProgress;
        }
    }

    public sealed class AssetDownloadState
    {
        public AssetDownloader.State state { get; private set; } = AssetDownloader.State.Initializing;
        public long bytesDownloaded;
        public long bytesTotal;
        public float progress;
        public DateTime lastStateChange;
        public DateTime lastDownloadChange;

        // file names
        public string curDownloadFile;
        public string curDownloadInfoFile;
        public string downloadFile;
        public string downloadInfoFile;
        public string reDownloadFile;
        public string reDownloadInfoFile;

        public void SetState(AssetDownloader.State newState)
        {
            state = newState;
            lastStateChange = DateTime.Now;
        }

        public void DeleteTempFiles()
        {
            try
            {
                if (File.Exists(curDownloadFile)) File.Delete(curDownloadFile);
                if (File.Exists(curDownloadInfoFile)) File.Delete(curDownloadInfoFile);
            }
            catch (Exception e)
            {
                Debug.Log($"Could not delete temp file {curDownloadFile}: {e.Message}. This is unusual but not a problem. If the file is on a network share it could also mean Unity is still downloading the file. In that case running update again after a while will index it normally. Continuing with next package for now.");
            }
        }

        public override string ToString()
        {
            return $"Asset Download State '{state}' ({progress})";
        }
    }

    public static class AssetDownloaderUtils
    {
        public static event Action<int> OnDownloadSuccessful;
        public static event Action<int> OnDownloadFinished;

        public static void OnDownloadDone(string package_id, string message, int bytes, int total)
        {
            int foreignId = int.Parse(package_id);

            if (message == "ok")
            {
                OnDownloadSuccessful?.Invoke(foreignId);
            }
            else
            {
                Debug.LogError($"Error downloading asset {package_id}: {message}");
            }
            OnDownloadFinished?.Invoke(foreignId);
        }
    }
}