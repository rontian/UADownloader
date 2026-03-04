using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    public sealed class FolderImporter : AssetImporter
    {
        public async Task Run(bool force = false)
        {
            List<FolderSpec> folders = AI.Config.folders.Where(f => f.enabled).ToList();
            MainCount = folders.Count;
            for (int i = 0; i < folders.Count; i++)
            {
                if (CancellationRequested) break;

                FolderSpec spec = folders[i];

                SetProgress(spec.location, i + 1);

                if (!Directory.Exists(spec.GetLocation(true)))
                {
                    Debug.LogWarning($"Specified folder to scan for assets does not exist anymore: {spec.location}");
                    continue;
                }

                switch (spec.folderType)
                {
                    case 0:
                        bool hasAssetStoreLayout = Path.GetFileName(spec.GetLocation(true)) == AI.ASSET_STORE_FOLDER_NAME;
                        UnityPackageImporter unityPackageImporter = new UnityPackageImporter();
                        AI.Actions.RegisterRunningAction(ActionHandler.ACTION_PACKAGE_FOLDERS_INDEX, unityPackageImporter, "Updating Unity package index");
                        await unityPackageImporter.IndexRoughLocal(spec, hasAssetStoreLayout, force);

                        if (AI.Config.indexAssetPackageContents)
                        {
                            unityPackageImporter.RestartProgress("Indexing package contents");
                            await unityPackageImporter.IndexDetails();
                        }
                        unityPackageImporter.FinishProgress();
                        break;

                    case 1:
                        MediaImporter mediaImporter = new MediaImporter();
                        AI.Actions.RegisterRunningAction(ActionHandler.ACTION_MEDIA_FOLDERS_INDEX, mediaImporter, "Updating media folder index");
                        await mediaImporter.Index(spec, null, false, false, true);
                        mediaImporter.FinishProgress();
                        break;

                    case 2:
                        ArchiveImporter archiveImporter = new ArchiveImporter();
                        AI.Actions.RegisterRunningAction(ActionHandler.ACTION_ARCHIVE_FOLDERS_INDEX, archiveImporter, "Updating archives index");
                        await archiveImporter.Run(spec);
                        archiveImporter.FinishProgress();
                        break;

                    case 3:
                        DevPackageImporter devPackageImporter = new DevPackageImporter();
                        AI.Actions.RegisterRunningAction(ActionHandler.ACTION_DEVPACKAGE_FOLDERS_INDEX, devPackageImporter, "Updating dev package index");
                        await devPackageImporter.Index(spec);
                        devPackageImporter.FinishProgress();
                        break;

                    default:
                        Debug.LogError($"Unsupported folder scan type: {spec.folderType}");
                        break;
                }
            }
        }
    }
}