using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class OrphanedPreviewFoldersValidator : Validator
    {
        public OrphanedPreviewFoldersValidator()
        {
            Type = ValidatorType.FileSystem;
            Name = "Orphaned Preview Folders";
            Description = "Scans the file system for preview folders that are not referenced anymore.";
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
            if (!Directory.Exists(AI.GetPreviewFolder())) return result;

            string[] folders = Directory.GetDirectories(AI.GetPreviewFolder());

            // gather existing assets for faster processing
            List<AssetInfo> assets = AI.LoadAssets();

            int progress = 0;
            int count = folders.Length;
            int progressId = MetaProgress.Start("Gathering orphaned preview folders");

            foreach (string folder in folders)
            {
                progress++;
                MetaProgress.Report(progressId, progress, count, folder);
                if (CancellationRequested) break;
                if (progress % 50 == 0) await Task.Yield();

                if (int.TryParse(Path.GetFileName(folder), out int assetId))
                {
                    if (!assets.Any(a => a.AssetId == assetId)) result.Add(folder);
                }
                else
                {
                    // non-numeric folders are always considered orphaned
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
            int progressId = MetaProgress.Start("Removing orphaned preview folders");

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
