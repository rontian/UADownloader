using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class AssetBackup : ActionProgress
    {
        private Dictionary<int, List<BackupInfo>> _assetVersions;

        public AssetBackup()
        {
            _assetVersions = new Dictionary<int, List<BackupInfo>>();
        }

        private void Refresh()
        {
            _assetVersions = GatherState();
        }

        public static Dictionary<int, List<BackupInfo>> GatherState()
        {
            Dictionary<int, List<BackupInfo>> assetVersions = new Dictionary<int, List<BackupInfo>>();

            string[] packages = Directory.GetFiles(AI.GetBackupFolder(), "*.unitypackage", SearchOption.AllDirectories);
            string[] sep =
            {
                AI.SEPARATOR
            };
            foreach (string package in packages)
            {
                // expected filename format is "foreignId-version
                string fileName = Path.GetFileNameWithoutExtension(package);
                string[] arr = fileName.Split(sep, StringSplitOptions.None);

                // skip packages without leading Id
                if (arr.Length < 3) continue;
                if (!int.TryParse(arr[0], out int id)) continue;
                string version = arr[1];

                if (!assetVersions.ContainsKey(id)) assetVersions.Add(id, new List<BackupInfo>());
                assetVersions[id].Add(new BackupInfo(package, version));
                assetVersions[id] = assetVersions[id].OrderByDescending(v => v.semVersion).ToList();
            }

            return assetVersions;
        }

        public async Task Run()
        {
            await Backup();
            ClearOut();
        }

        public async Task Backup(int assetId = -1)
        {
            Refresh();

            string backupFolder = AI.GetBackupFolder();
            List<Asset> assets = DBAdapter.DB.Table<Asset>()
                .Where(a => a.ForeignId > 0 && a.ParentId == 0 && a.AssetSource != Asset.Source.RegistryPackage && a.Backup && !string.IsNullOrEmpty(a.Version) && !string.IsNullOrEmpty(a.Location))
                .ToList();
            if (assetId > 0) assets = assets.Where(a => a.Id == assetId).ToList();

            MainCount = assets.Count;
            for (int i = 0; i < assets.Count; i++)
            {
                if (CancellationRequested) break;
                await AI.Cooldown.Do();

                Asset asset = assets[i];
                MetaProgress.Report(ProgressId, i + 1, assets.Count, asset.SafeName);

                if (!File.Exists(asset.GetLocation(true))) continue;

                string targetFile = null;
                if (_assetVersions.TryGetValue(asset.ForeignId, out List<BackupInfo> backupInfos))
                {
                    BackupInfo bi = backupInfos.FirstOrDefault(b => b.version == asset.GetSafeVersion());
                    if (bi != null) targetFile = bi.location;
                }
                if (targetFile == null) targetFile = Path.Combine(backupFolder, $"{asset.ForeignId}{AI.SEPARATOR}{asset.GetSafeVersion()}{AI.SEPARATOR}{asset.SafeName}.unitypackage");
                if (!File.Exists(targetFile))
                {
                    CurrentMain = $"{asset.SafeName} ({EditorUtility.FormatBytes(asset.PackageSize)})";
                    MainProgress = i + 1;

                    try
                    {
                        File.Copy(asset.GetLocation(true), targetFile, true);
                        await Task.Yield();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Could not backup '{asset.DisplayName}': {e.Message}");
                    }
                }
            }
            Refresh();
        }

        private void ClearOut()
        {
            foreach (KeyValuePair<int, List<BackupInfo>> pair in _assetVersions)
            {
                // remove patch versions
                if (AI.Config.onlyLatestPatchVersion)
                {
                    for (int i = pair.Value.Count - 1; i >= 1; i--)
                    {
                        if (pair.Value[i].semVersion.OnlyDiffersInPatch(pair.Value[i - 1].semVersion))
                        {
                            Debug.Log($"Removing asset from backup (newer patch versions available): {pair.Value[i].location}");
                            if (!IOUtils.TryDeleteFile(pair.Value[i].location))
                            {
                                Debug.LogWarning($"Could not delete file: {pair.Value[i].location}");
                            }
                            pair.Value.RemoveAt(i);
                        }
                    }
                }

                // finally remove all remaining old ones 
                if (pair.Value.Count > AI.Config.backupsPerAsset)
                {
                    for (int i = pair.Value.Count - 1; i >= AI.Config.backupsPerAsset; i--)
                    {
                        Debug.Log($"Removing asset from backup (limit reached and newer versions available): {pair.Value[i].location}");
                        if (!IOUtils.TryDeleteFile(pair.Value[i].location))
                        {
                            Debug.LogWarning($"Could not delete file: {pair.Value[i].location}");
                        }
                    }
                }
            }
            Refresh();
        }
    }

    public sealed class BackupInfo
    {
        public string location;
        public string version;
        public SemVer semVersion;

        public BackupInfo(string location, string version)
        {
            this.location = location;
            this.version = version;
            semVersion = new SemVer(version);
        }
    }
}