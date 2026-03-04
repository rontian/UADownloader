using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
#if UNITY_2021_2_OR_NEWER
#if UNITY_EDITOR_WIN && NET_4_6
using System.Drawing;
#else
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
#endif
#endif
using UnityEngine;

namespace AssetInventory
{
    public sealed class UnityPackageImporter : AssetImporter
    {
        private const string QUICK_INDEX_PREFERENCE = "synty";
        private const int QUICK_INDEX_COUNT = 3;

        private static readonly Regex CamelCaseRegex = new Regex("([a-z])([A-Z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SyntyNameRegex = new Regex(
            @"^(?<group>[^_]+)_(?<name>.+?)(?=_Unity_)_Unity_(?<minVersion>\d+_\d+(?:_\d+)?)_v(?<version>\d+_\d+(?:_\d+)?)\.unitypackage$",
            RegexOptions.Compiled
        );

        public async Task IndexRoughLocal(FolderSpec spec, bool fromAssetStore, bool force = false)
        {
            string[] packages = await Task.Run(() => Directory.GetFiles(spec.GetLocation(true), "*.unitypackage", SearchOption.AllDirectories));

            bool tagsChanged = false;
            MainCount = packages.Length;
            for (int i = 0; i < packages.Length; i++)
            {
                if (CancellationRequested) break;

                string package = packages[i].Replace("\\", "/");
                if (IsIgnoredPath(package, false)) continue;

                MetaProgress.Report(ProgressId, i + 1, packages.Length, package);
                if (i % 50 == 0) await Task.Yield(); // let editor breath

                Asset asset = HandlePackage(fromAssetStore, package, i, force);
                if (asset == null) continue;

                if (ApplyPackageTags(spec, asset, fromAssetStore)) tagsChanged = true;
            }
            if (tagsChanged) Tagging.LoadAssignments();
        }

        internal Asset HandlePackage(bool fromAssetStore, string package, int currentIndex, bool force = false, Asset parent = null, AssetFile subPackage = null)
        {
            package = package.Replace("\\", "/");
            string relPackage = AI.MakeRelative(package);

            // create asset and add additional information from file system
            Asset asset = new Asset();
            if (parent == null)
            {
                try
                {
                    asset.SafeName = Path.GetFileNameWithoutExtension(package);
                }
                catch (Exception e)
                {
                    // can happen when there are illegal characters in path
                    Debug.LogError($"Could not determine package name from '{package}': {e.Message}");
                    return null;
                }
                asset.SetLocation(relPackage);
                if (fromAssetStore)
                {
                    asset.AssetSource = Asset.Source.AssetStorePackage;
                    DirectoryInfo dirInfo = new DirectoryInfo(Path.GetDirectoryName(package));
                    asset.SafeCategory = dirInfo.Name;
                    asset.SafePublisher = dirInfo.Parent.Name;
                    if (string.IsNullOrEmpty(asset.DisplayCategory))
                    {
                        asset.DisplayCategory = CamelCaseRegex.Replace(asset.SafeCategory, "$1/$2").Trim();
                    }
                }
                else
                {
                    asset.AssetSource = Asset.Source.CustomPackage;
                    asset.CurrentSubState = Asset.SubState.None; // remove potentially incorrect flags again
                }
            }
            else
            {
                // package inherits nearly everything from parent
                asset.CopyFrom(parent);

                asset.ForeignId = 0; // will otherwise override metadata when syncing with store
                asset.ParentId = parent.Id;
                if (asset.AssetSource != Asset.Source.AssetStorePackage) asset.AssetSource = Asset.Source.CustomPackage;
                asset.SafeName = Path.GetFileNameWithoutExtension(package);
                asset.DisplayName = StringUtils.CamelCaseToWords(asset.SafeName.Replace("_", " ")).Trim();

                SetHeuristicPipelineCompatibility(asset);

                relPackage = $"{parent.Location}{Asset.SUB_PATH}{subPackage.Path}";
                asset.SetLocation(relPackage);
            }

            // try to read contained upload details
            AssetHeader header = ReadHeader(package, true);
            if (header != null && int.TryParse(header.id, out int id))
            {
                asset.ForeignId = id;
            }

            // skip unchanged 
            Asset existing = Fetch(asset);
            long size; // determine late for performance, especially with many exclusions
            FileInfo fInfo;
            if (existing != null)
            {
                if (existing.Exclude) return null;

                fInfo = new FileInfo(package);
                size = fInfo.Length;
                if (!force && existing.CurrentState == Asset.State.Done && existing.PackageSize == size && existing.Location == relPackage) return existing;

                if (string.IsNullOrEmpty(existing.SafeCategory)) existing.SafeCategory = asset.SafeCategory;
                if (string.IsNullOrEmpty(existing.DisplayCategory)) existing.DisplayCategory = asset.DisplayCategory;
                if (string.IsNullOrEmpty(existing.SafePublisher)) existing.SafePublisher = asset.SafePublisher;
                if (string.IsNullOrEmpty(existing.SafeName)) existing.SafeName = asset.SafeName;

                asset = existing;
            }
            else
            {
                fInfo = new FileInfo(package);
                size = fInfo.Length;
                if (AI.Config.excludeByDefault) asset.Exclude = true;
                if (AI.Config.extractByDefault) asset.KeepExtracted = true;
                if (AI.Config.captionByDefault) asset.UseAI = true;
                if (AI.Config.backupByDefault) asset.Backup = true;

                if (header == null)
                {
                    // detect special Synty file name patterns
                    // <GROUP>_<NAME>_Unity_<MINVERSION>_v<VERSION>.unitypackage
                    // e.g. POLYGON_Starter_Unity_2021_3_v1_0_1.unitypackage
                    if (TryParseSyntyFilename(Path.GetFileName(package), out string group, out string name, out string minVersion, out string version))
                    {
                        asset.DisplayName = group + " " + name.Replace("_", " ").Trim();
                        asset.SupportedUnityVersions = minVersion.Replace("_", ".");
                        asset.Version = version.Replace("_", ".");
                    }
                }
            }

            // update progress only if really doing work to save refresh time in UI
            CurrentMain = IOUtils.GetFileName(package);
            MainProgress = currentIndex + 1;

            ApplyHeader(header, asset);

            Asset.State previousState = asset.CurrentState;
            if (!force || existing == null) asset.CurrentState = Asset.State.InProcess;
            asset.SetLocation(relPackage);
            asset.PackageSize = size;
            if (parent != null)
            {
                asset.LastRelease = parent.LastRelease;
                asset.LastUpdate = parent.LastUpdate;
            }
            else
            {
                if (asset.ForeignId <= 0 || asset.LastRelease == DateTime.MinValue) asset.LastRelease = fInfo.LastWriteTime;
            }
            if (previousState != asset.CurrentState) asset.ETag = null; // force rechecking of download metadata
            Persist(asset);

            // check for package overrides after persisting to get Id (for potential tag references)
            ApplyOverrides(asset);

            return asset;
        }

        public static void SetHeuristicPipelineCompatibility(Asset asset)
        {
            // reset pipelines and determine dynamically
            asset.BIRPCompatible = AssetUtils.ShouldBeBIRPCompatible(asset.SafeName);
            asset.URPCompatible = AssetUtils.ShouldBeURPCompatible(asset.SafeName);
            asset.HDRPCompatible = AssetUtils.ShouldBeHDRPCompatible(asset.SafeName);
        }

        public static bool TryParseSyntyFilename(string filename, out string group, out string name, out string minVersion, out string version)
        {
            group = "";
            name = "";
            minVersion = "";
            version = "";

            Match match = SyntyNameRegex.Match(filename);
            if (!match.Success)
            {
                return false;
            }
            group = match.Groups["group"].Value;
            name = match.Groups["name"].Value;
            minVersion = match.Groups["minVersion"].Value;
            version = match.Groups["version"].Value;

            return true;
        }

        public static void ApplyHeader(AssetHeader header, Asset asset)
        {
            if (header == null) return;

            // only apply if foreign Id matches
            if (int.TryParse(header.id, out int id))
            {
                if (id > 0 && asset.ForeignId > 0 && id != asset.ForeignId) return;
                asset.ForeignId = id;
            }

            if (!string.IsNullOrWhiteSpace(header.version))
            {
                // version can be incorrect due to Unity bug (report pending)
                // there are two possible solutions using the upload id
                // 1. if there is an info file next to the package in the cache it will contain the upload id
                // if the upload id matches the current metadata use the live version instead
                bool skipVersion = false;
                if (asset.UploadId > 0)
                {
                    string infoFile = asset.GetLocation(true) + ".info.json";
                    if (File.Exists(infoFile))
                    {
                        CacheInfo cacheInfo = JsonConvert.DeserializeObject<CacheInfo>(File.ReadAllText(infoFile));
                        if (cacheInfo != null && int.TryParse(cacheInfo.upload_id, out int uploadId) && uploadId == asset.UploadId)
                        {
                            asset.Version = asset.LatestVersion;
                            skipVersion = true;
                        }
                    }
                    // 2. if the header contains the upload id and it matches the current metadata use the live version instead as well
                    else if (int.TryParse(header.upload_id, out int uploadId) && uploadId == asset.UploadId)
                    {
                        asset.Version = asset.LatestVersion;
                        skipVersion = true;
                    }
                }
                if (!skipVersion) asset.Version = header.version;
            }
            if (!string.IsNullOrWhiteSpace(header.title)) asset.DisplayName = header.title;
            if (!string.IsNullOrWhiteSpace(header.description)) asset.Description = header.description;
            if (header.publisher != null) asset.DisplayPublisher = header.publisher.label;
            if (header.category != null) asset.DisplayCategory = header.category.label;
        }

        public async Task IndexDetails(int assetId = 0)
        {
            List<Asset> assets;
            if (assetId == 0)
            {
                assets = DBAdapter.DB.Table<Asset>()
                    .Where(asset => !asset.Exclude && (asset.CurrentState == Asset.State.InProcess || asset.CurrentState == Asset.State.SubInProcess) && (asset.AssetSource == Asset.Source.AssetStorePackage || asset.AssetSource == Asset.Source.CustomPackage))
                    .ToList();

                // do shorter run initially for quick results
                if (!AI.Config.quickIndexingDone)
                {
                    // Limit to first 10 assets, prefer assets with known name
                    List<Asset> prefAssets = assets.Where(a => a.SafeName.ToLowerInvariant().Contains(QUICK_INDEX_PREFERENCE)).Take(QUICK_INDEX_COUNT).ToList();
                    List<Asset> otherAssets = assets.Where(a => !a.SafeName.ToLowerInvariant().Contains(QUICK_INDEX_PREFERENCE)).Take(QUICK_INDEX_COUNT - prefAssets.Count).ToList();
                    assets = prefAssets.Concat(otherAssets).ToList();
                }
            }
            else
            {
                assets = DBAdapter.DB.Table<Asset>()
                    .Where(asset => asset.Id == assetId && (asset.AssetSource == Asset.Source.AssetStorePackage || asset.AssetSource == Asset.Source.CustomPackage))
                    .ToList();
            }

            MainCount = assets.Count;
            for (int i = 0; i < assets.Count; i++)
            {
                if (CancellationRequested) break;
                if (!AI.Config.indexSubPackages && assets[i].ParentId > 0) continue;

                SetProgress(IOUtils.GetFileName(assets[i].GetLocation(true)) + " (" + StringUtils.FormatBytes(assets[i].PackageSize) + ")", i + 1);

                bool wasCachedAlready = await IndexPackage(assets[i], ProgressId);
                await Task.Yield(); // let editor breath
                if (CancellationRequested) break;

                // reread asset from DB in case of intermittent changes by online refresh 
                Asset asset = DBAdapter.DB.Find<Asset>(assets[i].Id);
                if (asset == null)
                {
                    Debug.LogWarning($"{assets[i]} disappeared while indexing.");
                    continue;
                }
                assets[i] = asset;

                AssetHeader header = ReadHeader(assets[i].GetLocation(true));
                ApplyHeader(header, assets[i]);
                ApplyOverrides(assets[i]);

                assets[i].CurrentState = Asset.State.Done;
                Persist(assets[i]);

                // recreate previews immediately
                if (AI.Config.recreatePreviewsAfterIndexing)
                {
                    PreviewPipeline pp = new PreviewPipeline();
                    AI.Actions.RegisterRunningAction(ActionHandler.ACTION_PREVIEWS_RECREATE, pp, "Recreating missing previews");
                    await pp.RecreatePreviews(new List<AssetInfo> {new AssetInfo(assets[i])}, true, AI.LoadAssets());
                    pp.FinishProgress();
                }
                if (!wasCachedAlready) RemoveWorkFolder(assets[i], AI.GetMaterializedAssetPath(assets[i]));

                AI.TriggerPackageRefresh();
            }
        }

        private async Task<bool> IndexPackage(Asset asset, int progressId)
        {
            if (string.IsNullOrEmpty(asset.Location)) return false;

            int subProgressId = MetaProgress.Start("Indexing package", null, progressId);
            string previewPath = AI.GetPreviewFolder();

            // extract
            string tempPath = AI.GetMaterializedAssetPath(asset);
            bool wasCachedAlready = Directory.Exists(tempPath);
            tempPath = await AI.ExtractAsset(asset);

            if (string.IsNullOrEmpty(tempPath))
            {
                Debug.LogError($"{asset} could not be indexed due to issues extracting it: {asset.GetLocation(true)}");
                MetaProgress.Remove(subProgressId);
                return wasCachedAlready;
            }

            // TODO: gather old index before to delete orphans afterwards but keep IDs of existing entries stable for tags etc.

            string assetPreviewFile = asset.GetLocation(true) + ".icon.png"; // alternatively allow placing png files next to the package
            if (!File.Exists(assetPreviewFile)) assetPreviewFile = Path.Combine(tempPath, ".icon.png");
            if (File.Exists(assetPreviewFile))
            {
                string targetDir = Path.Combine(previewPath, asset.Id.ToString());
                string targetFile = Path.Combine(targetDir, "a-" + asset.Id + Path.GetExtension(assetPreviewFile));
                Directory.CreateDirectory(targetDir);
                File.Copy(assetPreviewFile, targetFile, true);
            }

            // gather files
            string[] assets;
            try
            {
                assets = Directory.GetFiles(tempPath, "pathname", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                // in case this is a network drive or some files are locked
                Debug.LogError($"{asset} could not be indexed due to issues reading its contents: {e.Message}");
                MetaProgress.Remove(subProgressId);
                return wasCachedAlready;
            }

            List<AssetFile> subPackages = new List<AssetFile>();
            SubCount = assets.Length;
            for (int i = 0; i < assets.Length; i++)
            {
                string packagePath = assets[i];
                string fileName = IOUtils.ReadFirstLine(packagePath).Replace("\\", "/");
                if (IsIgnoredPath(fileName, false)) continue;

                string dir = Path.GetDirectoryName(packagePath).Replace("\\", "/");
                string assetFile = Path.Combine(dir, "asset"); // TODO: file not guaranteed to be there
                string previewFile = Path.Combine(dir, "preview.png");
                string guid = Path.GetFileName(dir);

                if (IOUtils.PathContainsInvalidChars(fileName))
                {
                    Debug.LogError($"Skipping entry in '{packagePath}' since path contains invalid characters: {fileName}");
                    continue;
                }

                CurrentSub = fileName;
                SubProgress = i + 1;
                MetaProgress.Report(subProgressId, i + 1, assets.Length, fileName);

                // skip folders
                if (!File.Exists(assetFile)) continue;

                if (i % 30 == 0) await Task.Yield(); // let editor breath

                // remaining info from file data (creation date is not original date anymore, ignore)
                FileInfo assetInfo = new FileInfo(assetFile);
                long size = assetInfo.Length;

                AssetFile af = new AssetFile();
                af.Guid = guid;
                af.AssetId = asset.Id;
                af.SetPath(fileName);
                af.SetSourcePath(assetFile.Substring(tempPath.Length + 1));
                af.FileName = Path.GetFileName(af.Path);
                af.Size = size;
                af.Type = IOUtils.GetExtensionWithoutDot(fileName).ToLowerInvariant();

                // if only new sub packages should be indexed, skip this one
                if (asset.CurrentState == Asset.State.SubInProcess && !af.IsUnityPackage() && !af.IsArchive()) continue;

                if (AI.Config.gatherExtendedMetadata)
                {
                    await ProcessMediaAttributes(assetFile, af, asset); // must be run on main thread
                }

                // persist once to get Id
                Persist(af);

                // update preview 
                if (AI.Config.extractPreviews && File.Exists(previewFile))
                {
                    string targetFile = af.GetPreviewFile(previewPath);
                    string targetDir = Path.GetDirectoryName(targetFile);
                    Directory.CreateDirectory(targetDir);

                    bool copyOriginal = true;
#if UNITY_2021_2_OR_NEWER
                    if (AI.Config.upscalePreviews && ImageUtils.SYSTEM_IMAGE_TYPES.Contains(af.Type))
                    {
                        // scale up preview already during import
                        if (ImageUtils.ResizeImage(assetFile, targetFile, AI.Config.upscaleSize, !AI.Config.upscaleLossless))
                        {
                            PreviewManager.StorePreviewResult(new PreviewRequest {DestinationFile = targetFile, Id = af.Id, Icon = Texture2D.grayTexture, SourceFile = assetFile});
                            af.PreviewState = AssetFile.PreviewOptions.Custom;
                            copyOriginal = false;
                        }
                    }
 #endif
                    if (copyOriginal)
                    {
                        try
                        {
                            File.Copy(previewFile, targetFile, true);
                            af.PreviewState = AssetFile.PreviewOptions.Provided;
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Could not copy preview '{previewFile}' to '{targetFile}': {e.Message}");
                        }
                    }
                    af.Hue = -1f;

                    try
                    {
#if UNITY_2021_2_OR_NEWER
                        // verify preview for complex previews going through Unity's preview pipeline
                        if (AI.Config.verifyPreviews
                            && (af.PreviewState == AssetFile.PreviewOptions.Provided || af.PreviewState == AssetFile.PreviewOptions.Custom)
                            && (AI.IsFileType(fileName, AI.AssetGroup.Models) || AI.IsFileType(fileName, AI.AssetGroup.Prefabs) || AI.IsFileType(fileName, AI.AssetGroup.Materials))
                           )
                        {
#if UNITY_EDITOR_WIN && NET_4_6
                            using (Bitmap image = new Bitmap(IOUtils.ToLongPath(previewFile)))
#else
                            using (Image<Rgba32> image = Image.Load<Rgba32>(IOUtils.ToLongPath(previewFile)))
#endif
                            {
                                if (PreviewManager.IsDefaultIcon(image)) af.PreviewState = AssetFile.PreviewOptions.Redo;
                            }
                        }
#endif
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Could not verify preview '{previewFile}': {e.Message}");
                    }

                    Persist(af);
                }
                if (af.IsUnityPackage() || af.IsArchive()) subPackages.Add(af);

                if (CancellationRequested) break;
                AI.MemoryObserver.Do(size);
                await AI.Cooldown.Do();
            }
            CurrentSub = null;
            MetaProgress.Remove(subProgressId);

            if (!CancellationRequested) await AI.ProcessSubPackages(asset, subPackages);

            return wasCachedAlready;
        }

        public async Task ProcessSubPackages(Asset asset, List<AssetFile> subPackages)
        {
            // index sub-packages while extracted
            if (AI.Config.indexSubPackages && subPackages.Count > 0)
            {
                CurrentMain = "Indexing sub-packages";
                MainCount = subPackages.Count;
                for (int i = 0; i < subPackages.Count; i++)
                {
                    if (CancellationRequested) break;

                    AssetFile subPackage = subPackages[i];

                    SetProgress(subPackage.FileName, i + 1);

                    string path = await AI.EnsureMaterializedAsset(asset, subPackage);
                    if (path == null)
                    {
                        Debug.LogError($"Could materialize sub-package '{subPackage.Path}' for '{asset.DisplayName}'");
                        continue;
                    }
                    Asset subAsset = HandlePackage(asset.AssetSource == Asset.Source.AssetStorePackage, path, i, true, asset, subPackage);
                    if (subAsset == null) continue;

                    // index immediately
                    await IndexPackage(subAsset, ProgressId);
                    subAsset.CurrentState = Asset.State.Done;
                    Persist(subAsset);
                }
            }
        }

        public static AssetHeader ReadHeader(string path, bool fileExistsForSure = false)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!fileExistsForSure && !File.Exists(path)) return null;

            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
#if UNITY_2021_2_OR_NEWER
                    Span<byte> headerBuffer = stackalloc byte[17];
                    if (stream.Read(headerBuffer) < 17) return null;

                    // check if really a JSON object
                    if (headerBuffer[16] != (byte)'{') return null;
                    int length = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(14, 2));
#else
                    byte[] headerBuffer = new byte[17];
                    if (stream.Read(headerBuffer, 0, 17) < 17) return null;

                    // check if really a JSON object
                    if (headerBuffer[16] != (byte)'{') return null;
                    int length = BitConverter.ToInt16(headerBuffer, 14);
#endif
                    byte[] jsonBuffer = new byte[length];
                    jsonBuffer[0] = headerBuffer[16];
                    int remaining = length - 1;
                    if (stream.Read(jsonBuffer, 1, remaining) != remaining) return null;

                    string headerData = Encoding.ASCII.GetString(jsonBuffer);
                    return JsonConvert.DeserializeObject<AssetHeader>(headerData);
                }
            }
            catch (IOException e)
            {
                Debug.LogError($"IO error while parsing package '{path}': {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogError($"Access error while parsing package '{path}': {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not parse package '{path}': {e.Message}");
            }
            return null;
        }
    }
}