using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    public sealed class UnityPackageDownloadImporter : AssetImporter
    {
        public IEnumerator IndexOnline(Action callback)
        {
            List<AssetInfo> packages = AI.LoadAssets()
                .Where(info =>
                    info.AssetSource == Asset.Source.AssetStorePackage
                    && !info.Exclude
                    && info.ParentId <= 0
                    && !info.IsAbandoned && (!info.IsIndexed || info.CurrentState == Asset.State.SubInProcess) && !string.IsNullOrEmpty(info.OfficialState)
                    && !info.IsDownloaded)
                .ToList();

            for (int i = 0; i < packages.Count; i++)
            {
                if (CancellationRequested) break;
                AssetInfo info = packages[i];

                MainCount = packages.Count;
                SetProgress(info.GetDisplayName(), i + 1);

                if (!CanDownload(info)) continue;

                // trigger already next one in background
                AssetInfo nextInfo = i < packages.Count - 1 ? packages[i + 1] : null;
                if (nextInfo != null && CanDownload(nextInfo) && !nextInfo.IsDownloading())
                {
                    nextInfo.PackageDownloader.Download(true);
                }

                yield return DownloadAsset(info);
                if (CancellationRequested) break;
                if (!info.IsDownloaded) continue;

                UnityPackageImporter unityPackageImporter = new UnityPackageImporter();
                AI.Actions.RegisterRunningAction(ActionHandler.ACTION_ASSET_STORE_CACHE_INDEX, unityPackageImporter, "Indexing downloaded package");
                unityPackageImporter.HandlePackage(true, AI.DeRel(info.Location), i);
                Task task = unityPackageImporter.IndexDetails(info.AssetId);
                yield return new WaitWhile(() => !task.IsCompleted);
                unityPackageImporter.FinishProgress();

                // remove again
                yield return RemoveDownload(info.ToAsset());

                info.Refresh();
            }

            callback?.Invoke();
        }
    }
}