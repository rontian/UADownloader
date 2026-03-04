using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;

namespace AssetInventory
{
    public sealed class DevPackageImporter : AssetImporter
    {
        private const int BREAK_INTERVAL = 30;

        public async Task Index(FolderSpec spec)
        {
            if (string.IsNullOrEmpty(spec.location)) return;

            string fullLocation = spec.GetLocation(true);
            bool treatAsUnityProject = spec.detectUnityProjects && AssetUtils.IsUnityProject(fullLocation);
            string[] files = IOUtils.GetFiles(treatAsUnityProject ? Path.Combine(fullLocation, "Assets") : fullLocation, new[] {"package.json"}, SearchOption.AllDirectories).ToArray();

            MainCount = files.Length;
            MainProgress = 1; // small hack to trigger UI update in the end

            for (int i = 0; i < files.Length; i++)
            {
                if (CancellationRequested) break;
                if (i % BREAK_INTERVAL == 0) await Task.Yield(); // let editor breath in case many files are already indexed
                await AI.Cooldown.Do();

                string package = files[i];
                Asset asset = await HandlePackage(package);
                if (asset == null) continue;

                MetaProgress.Report(ProgressId, i + 1, files.Length, package);
                MainCount = files.Length;
                CurrentMain = asset.DisplayName + " (" + EditorUtility.FormatBytes(asset.PackageSize) + ")";
                MainProgress = i + 1;

                await Task.Yield();
                await IndexPackage(asset, spec);
                await Task.Yield();

                if (CancellationRequested) break;

                ApplyPackageTags(spec, asset);
            }
        }

        private async Task<Asset> HandlePackage(string package)
        {
            Package info = PackageImporter.ReadPackageFile(package);
            if (info == null) return null;

            // create asset
            Asset asset = await PackageImporter.CreateAsset(info, package, PackageSource.Local);
            if (asset == null) return null;

            // handle tags
            bool tagsChanged = PackageImporter.ApplyTags(asset, info, false);

            asset.CurrentState = Asset.State.InProcess;
            UpdateOrInsert(asset);

            if (tagsChanged)
            {
                Tagging.LoadTags();
                Tagging.LoadAssignments();
            }

            return asset;
        }

        public async Task IndexDetails(Asset asset)
        {
            MainCount = 1;
            CurrentMain = "Indexing dev package";

            FolderSpec importSpec = GetDefaultImportSpec();
            importSpec.createPreviews = true; // TODO: derive from additional folder settings
            await IndexPackage(asset, importSpec);
        }

        private async Task IndexPackage(Asset asset, FolderSpec spec)
        {
            FolderSpec importSpec = GetDefaultImportSpec();
            importSpec.location = asset.GetLocation(true);
            importSpec.createPreviews = spec.createPreviews;

            MediaImporter mediaImporter = new MediaImporter();
            mediaImporter.WithProgress("Updating files index");
            await mediaImporter.Index(importSpec, asset, false, true);

            MarkDone(asset);
        }
    }
}