using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SQLite;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class MediaImporter : AssetImporter
    {
        private const double BREAK_INTERVAL = 0.5;

        public async Task Index(FolderSpec spec, Asset attachedAsset = null, bool storeRelativePath = false, bool actAsSubImporter = false, bool skipSubPackages = false)
        {
            if (string.IsNullOrEmpty(spec.location)) return;

            string fullLocation = spec.GetLocation(true).Replace("\\", "/");
            if (!Directory.Exists(fullLocation)) return;

            List<string> searchPatterns = new List<string>();
            List<AI.AssetGroup> types = new List<AI.AssetGroup>();
            switch (spec.scanFor)
            {
                case 0:
                    types.AddRange(new[] {AI.AssetGroup.Audio, AI.AssetGroup.Images, AI.AssetGroup.Models});
                    break;

                case 1:
                    searchPatterns.Add("*.*");
                    break;

                case 3:
                    types.Add(AI.AssetGroup.Audio);
                    break;

                case 4:
                    types.Add(AI.AssetGroup.Images);
                    break;

                case 5:
                    types.Add(AI.AssetGroup.Models);
                    break;

                case 7:
                    if (!string.IsNullOrWhiteSpace(spec.pattern)) searchPatterns.AddRange(spec.pattern.Split(';'));
                    break;
            }

            // load existing for orphan checking and caching 
            string previewFolder = AI.GetPreviewFolder();
            List<string> fileTypes = new List<string>();
            types.ForEach(t => fileTypes.AddRange(AI.TypeGroups[t]));

            TableQuery<AssetFile> existingQuery = DBAdapter.DB.Table<AssetFile>();
            if (fileTypes.Count > 0) existingQuery = existingQuery.Where(af => fileTypes.Contains(af.Type));
            existingQuery = existingQuery.Where(af => af.SourcePath.StartsWith(spec.location));
            List<AssetFile> existing = existingQuery.ToList();

            // clean up existing
            if (spec.removeOrphans)
            {
                DBAdapter.DB.RunInTransaction(() =>
                {
                    foreach (AssetFile file in existing)
                    {
                        if (!File.Exists(file.GetSourcePath(true)))
                        {
                            // TODO: rethink if relative
                            Debug.Log($"Removing orphaned entry from index: {file.SourcePath}");
                            DBAdapter.DB.Delete<AssetFile>(file.Id);

                            if (File.Exists(file.GetPreviewFile(previewFolder))) File.Delete(file.GetPreviewFile(previewFolder));
                        }
                    }
                });
            }

            bool treatAsUnityProject = spec.detectUnityProjects && AssetUtils.IsUnityProject(fullLocation);

            // scan for new files
            string[] excludedExtensions = StringUtils.Split(spec.excludedExtensions, new[] {';', ','});
            string[] excludedPreviewExtensions = StringUtils.Split(AI.Config.excludedPreviewExtensions, new[] {';', ','});

            types.ForEach(t => searchPatterns.AddRange(AI.TypeGroups[t].Select(ext => $"*.{ext}")));
            string[] files = IOUtils.GetFiles(treatAsUnityProject ? Path.Combine(fullLocation, "Assets") : fullLocation, searchPatterns, SearchOption.AllDirectories)
                .Where(file =>
                {
                    string type = IOUtils.GetExtensionWithoutDot(file).ToLowerInvariant();
                    return type != "meta" && !excludedExtensions.Contains(type);
                })
                .ToArray();
            int fileCount = files.Length;
            if (spec.createPreviews) UnityPreviewGenerator.Init(fileCount);

            if (attachedAsset == null)
            {
                if (spec.attachToPackage)
                {
                    attachedAsset = DBAdapter.DB.Find<Asset>(a => a.SafeName == spec.location);
                    if (attachedAsset == null)
                    {
                        attachedAsset = new Asset();
                        attachedAsset.SafeName = fullLocation;
                        attachedAsset.SetLocation(fullLocation);
                        attachedAsset.DisplayName = Path.GetFileNameWithoutExtension(fullLocation);
                        attachedAsset.AssetSource = Asset.Source.Directory;
                        Persist(attachedAsset);
                    }
                }
                else
                {
                    // use generic catch-all package
                    attachedAsset = DBAdapter.DB.Find<Asset>(a => a.SafeName == Asset.NONE);
                    if (attachedAsset == null)
                    {
                        attachedAsset = Asset.GetNoAsset();
                        Persist(attachedAsset);
                    }
                }
            }

            // cache
            int specLength = fullLocation.Length + 1;
            Dictionary<string, List<AssetFile>> guidDict = ToGuidDict(existing);
            Dictionary<(string, int), AssetFile> pathIdDict = ToPathIdDict(existing);

            // do actual indexing
            double nextBreak = 0;
            List<AssetFile> subPackages = new List<AssetFile>();

            // performance considerations:
            // - parallelization does actually reduce speed as most time is spent in IO and DB operations
            // - transactions could help but would require to keep all new files in memory until the end
            MainCount = fileCount;
            long totalSize = 0;
            for (int i = 0; i < files.Length; i++)
            {
                if (CancellationRequested) break;
                if (EditorApplication.timeSinceStartup > nextBreak)
                {
                    nextBreak = EditorApplication.timeSinceStartup + BREAK_INTERVAL;
                    await Task.Yield(); // let editor breath in case many files are already indexed
                    await AI.Cooldown.Do();
                }

                string file = files[i];
                string relPath = file.Substring(specLength);
                if (IsIgnoredPath(relPath, true)) continue;

                SetProgress(file, i + 1);

                AssetFile af = new AssetFile();
                af.AssetId = attachedAsset.Id;

                // ensure no absolute paths are stored, e.g. in archives, would point to extracted otherwise
                // locations outside extracted, e.g. directories, reg packages... should be stored as is
                af.SetSourcePath(storeRelativePath ? relPath : AI.MakeRelative(file));
                af.SetPath(actAsSubImporter ? relPath : af.SourcePath);

                string metaFile = $"{file}.meta";
                if (File.Exists(metaFile)) af.Guid = AssetUtils.ExtractGuidFromFile(metaFile);

                AssetFile existingAf = Fetch(af, guidDict, pathIdDict);
                if (existingAf != null && !spec.checkSize)
                {
                    if (attachedAsset.CurrentState != Asset.State.SubInProcess || (!existingAf.IsUnityPackage() && !existingAf.IsArchive()))
                    {
                        // skip if already indexed and size check is disabled as it will slow down the process especially on dropbox folders significantly
                        totalSize += existingAf.Size;
                        continue;
                    }
                }

                // check if file is still there, there are cases (e.g. ".bundle") which can disappear
                if (!File.Exists(file))
                {
                    Debug.LogWarning($"File '{file}' disappeared, skipping");
                    continue;
                }

                string type = IOUtils.GetExtensionWithoutDot(file).ToLowerInvariant();
                try
                {
                    FileInfo fileInfo = new FileInfo(file);
                    fileInfo.Refresh(); // otherwise can cause sporadic FileNotFound exceptions
                    long size = fileInfo.Length;
                    totalSize += size;

                    // reindex if file size changed
                    if (existingAf != null)
                    {
                        if (attachedAsset.CurrentState != Asset.State.SubInProcess || (!existingAf.IsUnityPackage() && !existingAf.IsArchive()))
                        {
                            if (existingAf.Size == size) continue;
                        }

                        // make sure new changes carry over
                        existingAf.SetSourcePath(af.SourcePath);
                        existingAf.SetPath(af.Path);
                        if (!string.IsNullOrWhiteSpace(af.Guid)) existingAf.Guid = af.Guid;

                        af = existingAf;
                    }

                    CurrentMain = file + " (" + EditorUtility.FormatBytes(size) + ")";
                    if (i % 50 == 0) await Task.Yield(); // let editor breath
                    AI.MemoryObserver.Do(size);

                    af.FileName = Path.GetFileName(af.SourcePath);
                    af.Size = size;
                    af.Type = type;
                    if (AI.Config.gatherExtendedMetadata)
                    {
                        await ProcessMediaAttributes(file, af, attachedAsset); // must be run on main thread
                    }
                    Persist(af);

                    if (af.IsUnityPackage() || af.IsArchive()) subPackages.Add(af);
                }
                catch (Exception e)
                {
                    Debug.LogError($"File '{file}' could not be indexed: {e.Message}");
                }

                if (spec.createPreviews && PreviewManager.IsPreviewable(af.FileName, false))
                {
                    if (!AI.Config.excludePreviewExtensions || !excludedPreviewExtensions.Contains(type))
                    {
                        AssetInfo info = new AssetInfo().CopyFrom(attachedAsset, af);
                        await PreviewManager.Create(info, file);
                    }
                }
            }
            if (spec.createPreviews)
            {
                CurrentMain = "Finalizing preview images";
                await UnityPreviewGenerator.ExportPreviews();
                UnityPreviewGenerator.CleanUp();
            }

            if (attachedAsset.SafeName != Asset.NONE)
            {
                // update date
                attachedAsset = Fetch(attachedAsset);
                attachedAsset.CurrentState = Asset.State.Done;

                if (attachedAsset.AssetSource != Asset.Source.Archive)
                {
                    attachedAsset.LastRelease = DateTime.Now;
                    attachedAsset.PackageSize = totalSize;
                }

                // update location of attached asset to reflect current spec
                // but not for children as that would put extracted path into location
                if (!actAsSubImporter) attachedAsset.SetLocation(fullLocation);

                Persist(attachedAsset);

                if (!skipSubPackages) await AI.ProcessSubPackages(attachedAsset, subPackages);
            }
        }
    }
}