using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class ArchiveImporter : AssetImporter
    {
        private const int BREAK_INTERVAL = 30;

        public async Task Run(FolderSpec spec)
        {
            if (string.IsNullOrEmpty(spec.location)) return;

            string[] files = IOUtils.GetFiles(spec.GetLocation(true), new[] {"*.zip", "*.rar", "*.7z"}, SearchOption.AllDirectories).ToArray();

            MainCount = files.Length;
            for (int i = 0; i < files.Length; i++)
            {
                if (CancellationRequested) break;
                if (i % BREAK_INTERVAL == 0) await Task.Yield(); // let editor breath in case many files are already indexed
                await AI.Cooldown.Do();

                string package = files[i];
                if (IsIgnoredPath(package, true)) continue;

                // check for multipart archives and skip if not the first part
                // zip will have zip.001, rar will have .r00, .r01, .r02, etc. but can also have .part1.rar, .part2.rar, etc.
                if (!IOUtils.IsFirstArchiveVolume(package)) continue;

                Asset asset = HandlePackage(package);
                if (asset == null) continue;

                SetProgress(package + " (" + EditorUtility.FormatBytes(asset.PackageSize) + ")", i + 1);

                await Task.Yield();
                await IndexPackage(asset, spec);
                await Task.Yield();

                if (CancellationRequested) break; // don't store tags on cancellation

                ApplyPackageTags(spec, asset);
            }
        }

        private Asset HandlePackage(string package, Asset parent = null, AssetFile subPackage = null)
        {
            Asset asset = new Asset();
            if (parent == null)
            {
                asset.SetLocation(AI.MakeRelative(package));
            }
            else
            {
                // package inherits nearly everything from parent
                asset.CopyFrom(parent);
                asset.ForeignId = 0; // will otherwise override metadata when syncing with store
                asset.ParentId = parent.Id;

                string relPackage = $"{parent.Location}{Asset.SUB_PATH}{subPackage.Path}";
                asset.SetLocation(relPackage);
            }
            asset.SafeName = Path.GetFileNameWithoutExtension(package);
            asset.DisplayName = StringUtils.CamelCaseToWords(asset.SafeName.Replace("_", " ")).Trim();

            // remove left-overs from multi-part archives
            if (asset.DisplayName.ToLowerInvariant().EndsWith(".part1")) asset.DisplayName = asset.DisplayName.Substring(0, asset.DisplayName.Length - 6);
            asset.AssetSource = Asset.Source.Archive;

            Asset existing = DBAdapter.DB.Table<Asset>().FirstOrDefault(a => a.Location == asset.Location);

            long size; // determine late for performance, especially with many exclusions
            FileInfo fInfo;
            if (existing != null)
            {
                if (existing.Exclude) return null;

                fInfo = new FileInfo(package);
                size = fInfo.Length;
                if (existing.CurrentState == Asset.State.Done && existing.PackageSize == size && existing.Location == asset.Location) return null;

                asset = existing;
            }
            else
            {
                fInfo = new FileInfo(package);
                size = fInfo.Length;
            }
            asset.PackageSize = size;
            asset.LastRelease = fInfo.LastWriteTime;
            Persist(asset);

            // optional preview image in a png file next to the package
            string assetPreviewFile = asset.GetLocation(true) + ".icon.png";
            if (File.Exists(assetPreviewFile))
            {
                string targetDir = Path.Combine(AI.GetPreviewFolder(), asset.Id.ToString());
                string targetFile = Path.Combine(targetDir, "a-" + asset.Id + Path.GetExtension(assetPreviewFile));
                Directory.CreateDirectory(targetDir);
                File.Copy(assetPreviewFile, targetFile, true);
            }

            ApplyOverrides(asset);

            return asset;
        }

        public async Task IndexDetails(Asset asset)
        {
            FolderSpec importSpec = GetDefaultImportSpec();
            importSpec.createPreviews = true; // TODO: derive from additional folder settings
            await IndexPackage(asset, importSpec);
        }

        private async Task IndexPackage(Asset asset, FolderSpec spec)
        {
            string tempPath = await AI.ExtractAsset(asset);
            if (string.IsNullOrEmpty(tempPath))
            {
                Debug.LogError($"{asset} could not be indexed due to issues extracting it: {asset.Location}");
                return;
            }

            FolderSpec importSpec = GetDefaultImportSpec();
            importSpec.location = IOUtils.ToShortPath(tempPath);
            importSpec.createPreviews = spec.createPreviews;
            importSpec.excludedExtensions = spec.excludedExtensions;

            MediaImporter mediaImporter = new MediaImporter();
            AI.Actions.RegisterRunningAction(ActionHandler.ACTION_MEDIA_FOLDERS_INDEX, mediaImporter, "Updating media folder index");
            await mediaImporter.Index(importSpec, asset, true, true);
            mediaImporter.FinishProgress();

            MarkDone(asset);
        }

        public async Task ProcessSubArchives(Asset asset, List<AssetFile> subArchives)
        {
            // index sub-packages while extracted
            if (AI.Config.indexSubPackages && subArchives.Count > 0)
            {
                CurrentMain = "Indexing sub-archives";
                MainCount = subArchives.Count;
                for (int i = 0; i < subArchives.Count; i++)
                {
                    if (CancellationRequested) break;

                    SetProgress(subArchives[i].FileName, i + 1);

                    AssetFile subArchive = subArchives[i];
                    string path = await AI.EnsureMaterializedAsset(asset, subArchive);
                    if (path == null)
                    {
                        Debug.LogError($"Could materialize sub-archive '{subArchive.Path}' for '{asset.DisplayName}'");
                        continue;
                    }
                    Asset subAsset = HandlePackage(path, asset, subArchive);
                    if (subAsset == null) continue;

                    // index immediately
                    FolderSpec importSpec = GetDefaultImportSpec();
                    importSpec.createPreviews = true; // TODO: derive from additional folder settings

                    await IndexPackage(subAsset, importSpec);
                    subAsset.CurrentState = Asset.State.Done;
                    Persist(subAsset);
                }
            }
        }
    }
}