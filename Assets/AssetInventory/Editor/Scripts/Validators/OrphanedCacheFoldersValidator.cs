using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class OrphanedCacheFoldersValidator : Validator
    {
        public OrphanedCacheFoldersValidator()
        {
            Type = ValidatorType.FileSystem;
            Name = "Orphaned Cache Folders";
            Description = "Scans the file system for cache folders that are not referenced anymore.";
            FixCaption = "Remove";
        }

        public override async Task Validate()
        {
            CurrentState = State.Scanning;
            FileIssues = await GatherOrphanedFolders();
            CurrentState = State.Completed;
        }

        public override async Task Fix()
        {
            CurrentState = State.Fixing;
            await RemoveOrphanedFolders(FileIssues);
            await Validate();
        }

        private async Task<List<string>> GatherOrphanedFolders()
        {
            List<string> result = new List<string>();
            if (!Directory.Exists(AI.GetMaterializeFolder())) return result;

            string[] folders = Directory.GetDirectories(AI.GetMaterializeFolder());

            // gather existing assets for faster processing
            List<AssetInfo> assets = AI.LoadAssets();

            int progress = 0;
            int count = folders.Length;
            int progressId = MetaProgress.Start("Gathering orphaned cache folders");

            foreach (string folder in folders)
            {
                progress++;
                MetaProgress.Report(progressId, progress, count, folder);
                if (CancellationRequested) break;
                if (progress % 50 == 0) await Task.Yield();

                string[] segments = folder.Split(AI.SEPARATOR);
                if (segments.Length < 2)
                {
                    result.Add(folder); // not a valid path, can be removed
                    continue;
                }

                if (!int.TryParse(segments[1].Trim(), out int assetId))
                {
                    // non-numeric folders are always considered orphaned
                    result.Add(folder);
                }

                if (!assets.Any(a => a.AssetId == assetId))
                {
                    // not belonging to any asset
                    result.Add(folder);
                }
            }
            MetaProgress.Remove(progressId);

            return result;
        }

        private async Task RemoveOrphanedFolders(List<string> folders)
        {
            int progress = 0;
            int count = folders.Count;
            int progressId = MetaProgress.Start("Removing orphaned cache folders");

            foreach (string folder in folders)
            {
                progress++;
                MetaProgress.Report(progressId, progress, count, folder);
                if (CancellationRequested) break;
                if (progress % 10 == 0) await Task.Yield();

                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }

            MetaProgress.Remove(progressId);
        }
    }
}