using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using static AssetInventory.AssetInfo;

namespace AssetInventory
{
    public class DependencyAnalysis
    {
        private static readonly Regex FileGuid = new Regex("guid: (?:([a-z0-9]*))", RegexOptions.Compiled);
        private static readonly Regex GraphGuid = new Regex("\\\\\"guid\\\\\": \\\\\\\"([^\"]*)\\\\\"", RegexOptions.Compiled);
        private static readonly Regex JsonGraphGuid = new Regex("\\\\\\\\\\\\\"guid\\\\\\\\\\\\\": \\\\\\\\\\\\\\\"([^\"]*)\\\\\\\\\\\\\"", RegexOptions.Compiled);
        private static readonly Regex IncludeFilesRegex = new Regex(@"#include(?:_with_pragmas)?\s*""(.+?)""", RegexOptions.Compiled);
        private static readonly Regex CustomEditorRegex = new Regex(@"CustomEditor\s*""(.+?)""", RegexOptions.Compiled); // Regex to match custom editor lines and capture names

        // Thread-safe unique temp folder for this analysis instance
        private readonly string _uniqueTempFolder;
        private readonly string _uniqueTempFolderPath;

        // Cached pipeline checks to avoid repeated calls during dependency analysis
        private readonly bool _isOnURP;
        private readonly bool _isOnHDRP;

        private static readonly string[] ScanDependencies =
        {
            "prefab", "mat", "controller", "overridecontroller", "anim", "asset", "physicmaterial", "physicsmaterial",
            "sbs", "sbsar", "cubemap", "shader", "cginc", "hlsl", "shadergraph", "shadersubgraph",
            "terrainlayer", "inputactions", "vfx", "vfxoperator", "unity", "preset"
        };

        private static readonly string[] ScanMetaDependencies =
        {
            "shader", "ttf", "otf", "js", "obj", "fbx", "uxml", "uss", "inputactions", "tss", "nn", "cs"
        };

        public DependencyAnalysis()
        {
            // Generate unique temp folder name with GUID suffix for thread safety
            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _uniqueTempFolder = $"{AI.TEMP_FOLDER}_{uniqueId}";
            _uniqueTempFolderPath = Path.Combine(Application.dataPath, _uniqueTempFolder);

            // Cache pipeline checks once per analysis session
            _isOnURP = AssetUtils.IsOnURP();
            _isOnHDRP = AssetUtils.IsOnHDRP();
        }

        public static bool NeedsScan(string type)
        {
            return ScanDependencies.Contains(type) || ScanMetaDependencies.Contains(type);
        }

        /// <summary>
        /// Cleans up all temporary folders used for dependency analysis.
        /// This should be called during application initialization to remove orphaned folders from previous sessions.
        /// </summary>
        public static void CleanUp()
        {
            try
            {
                string assetsPath = Application.dataPath;
                if (!Directory.Exists(assetsPath)) return;

                // Find all temp folders matching the pattern _AssetInventoryTemp*
                string[] allDirs = Directory.GetDirectories(assetsPath, $"{AI.TEMP_FOLDER}*", SearchOption.TopDirectoryOnly);
                foreach (string dir in allDirs)
                {
                    try
                    {
                        string dirName = Path.GetFileName(dir);
                        // Only delete folders that match our pattern (base name or base name with underscore and ID)
                        if (dirName == AI.TEMP_FOLDER || dirName.StartsWith(AI.TEMP_FOLDER + "_"))
                        {
                            Directory.Delete(dir, true);
                            string metaFile = dir + ".meta";
                            if (File.Exists(metaFile)) File.Delete(metaFile);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Could not remove temporary dependency analysis folder '{dir}': {e.Message}");
                    }
                }
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not clean up temporary dependency analysis folders: {e.Message}");
            }
        }

        public async Task Analyze(AssetInfo info, CancellationToken ct)
        {
            info.DependencyState = DependencyStateOptions.Calculating;
            info.Dependencies = new List<AssetFile>();
            info.CrossPackageDependencies = new List<Asset>();

            string targetPath = await AI.EnsureMaterializedAsset(info, false, ct);
            if (targetPath == null)
            {
                info.DependencyState = DependencyStateOptions.Failed;
                return;
            }

            PreparePipelineDependencies(info);

            // work on a copy in case SRP will be used to not mess with the original data
            AssetInfo workInfo = new AssetInfo(info);
            if (info.SRPMainReplacement != null)
            {
                workInfo.CopyFrom(info.SRPSupportPackage, info.SRPMainReplacement);
                targetPath = await AI.EnsureMaterializedAsset(workInfo, false, ct);
                if (targetPath == null)
                {
                    info.DependencyState = DependencyStateOptions.Failed;
                    return;
                }
            }

            // calculate
            ConcurrentDictionary<string, bool> processedGuids = new ConcurrentDictionary<string, bool>();
            List<AssetFile> deps = await DoCalculateDependencies(workInfo, targetPath, processedGuids, null, ct);

            // free up memory
            if (!workInfo.SRPUsed)
            {
                info.SRPOriginalBackup = null;
                info.SRPSupportPackage = null;
                info.SRPMainReplacement = null;
            }

            // ensure unique dependencies
            info.CrossPackageDependencies = workInfo.CrossPackageDependencies.GroupBy(d => d.Id).Select(g => g.First()).ToList();

            if (deps == null)
            {
                info.DependencyState = DependencyStateOptions.Failed;
                return;
            }
            info.DependencyState = workInfo.DependencyState;

            info.Dependencies = deps
                .OrderBy(f => f.AssetId)
                .ThenBy(f => f.Path)
                .ThenBy(f => f.Type)
                .ToList();
            info.DependencySize = info.Dependencies.Sum(af => af.Size);
            info.MediaDependencies = info.Dependencies.Where(af => af.Type != "cs" && af.Type != "dll").ToList();
            info.ScriptDependencies = info.Dependencies.Where(af => af.Type == "cs" || af.Type == "dll").ToList();

            // clean-up again on-demand - use unique temp folder for this instance
            if (Directory.Exists(_uniqueTempFolderPath))
            {
                await IOUtils.DeleteFileOrDirectory(_uniqueTempFolderPath);
                await IOUtils.DeleteFileOrDirectory(_uniqueTempFolderPath + ".meta");
                AssetDatabase.Refresh();
            }
            if (info.DependencyState == DependencyStateOptions.Calculating) info.DependencyState = DependencyStateOptions.Done; // otherwise error along the way
        }

        private async Task<List<AssetFile>> DoCalculateDependencies(AssetInfo info, string path, ConcurrentDictionary<string, bool> processedGuids, List<AssetFile> result = null, CancellationToken ct = default(CancellationToken))
        {
            if (result == null) result = new List<AssetFile>();

            if (ct.IsCancellationRequested)
            {
                info.DependencyState = DependencyStateOptions.Partial;
                return result;
            }

            path = IOUtils.ToLongPath(path);

            // Asset Manager dependencies are all files in the asset's folder
            if (info.AssetSource == Asset.Source.AssetManager)
            {
                if (File.Exists(path)) return result; // single-file asset 

                List<string> allFiles = await Task.Run(() => Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList());
                foreach (string file in allFiles)
                {
                    AssetFile af = new AssetFile();
                    af.Path = file.Substring(path.Length + 1);
                    af.FileName = Path.GetFileName(file);
                    af.Type = IOUtils.GetExtensionWithoutDot(file).ToLowerInvariant();
                    af.Size = new FileInfo(file).Length;

                    processedGuids.TryAdd(af.Guid, true);
                    result.Add(af);
                    // TODO: await ScanDependencyResult(info, result, af);
                }
                return result;
            }

            // only scan file types that contain guid references
            string extension = IOUtils.GetExtensionWithoutDot(path).ToLowerInvariant();

            // meta files can also contain dependencies
            if (ScanMetaDependencies.Contains(extension))
            {
                string metaPath = path + ".meta";
                if (File.Exists(metaPath)) await DoCalculateDependencies(info, metaPath, processedGuids, result, ct);

                if (AI.Config.scanFBXDependencies && extension == "fbx")
                {
                    // also scan for texture references to image files inside the package (embedded materials)
                    string typeStr = string.Join("\",\"", AI.TypeGroups[AI.AssetGroup.Images]);
                    string query = "select * from AssetFile where AssetId = ? and Type in (\"" + typeStr + "\")";
                    List<AssetFile> files = DBAdapter.DB.Query<AssetFile>(query, info.AssetId).ToList();
                    if (files.Count > 0)
                    {
                        List<string> embedded = await IOUtils.FindMatchesInBinaryFile(path, files.Select(f => f.FileName).Distinct().ToList());
                        foreach (string embed in embedded)
                        {
                            AssetFile af = files.FirstOrDefault(f => f.FileName == embed);
                            await AddToResultAndCheckForSRPSupportReplacement(info, result, af, processedGuids, ct);
                        }
                    }
                }
            }

            if (extension != "meta" && !ScanDependencies.Contains(extension)) return result;

            if (string.IsNullOrEmpty(info.Guid))
            {
                info.DependencyState = DependencyStateOptions.Failed;
                return result;
            }

            string content;
            try
            {
#if UNITY_2021_2_OR_NEWER
                content = await File.ReadAllTextAsync(path);
#else
                content = File.ReadAllText(path);
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not read file '{path}': {e.Message}");
                return null;
            }

            MatchCollection matches = null;
            if (extension == "shader" || extension == "cginc" || extension == "hlsl")
            {
                string metaPath = path + ".meta";
                if (File.Exists(metaPath))
                {
                    string curGuid = AssetUtils.ExtractGuidFromFile(metaPath);
                    if (curGuid != null)
                    {
                        AssetFile curAf = DBAdapter.DB.Find<AssetFile>(a => a.Guid == curGuid && a.AssetId == info.AssetId);
                        if (curAf != null)
                        {
                            // include files
                            HashSet<string> includedFiles = FindIncludeFiles(content);
                            foreach (string include in includedFiles)
                            {
                                string includePath = include.StartsWith("Assets") ? include : Path.Combine(Path.GetDirectoryName(curAf.Path), include);
                                includePath = includePath.Replace("\\", "/");
                                includePath = IOUtils.NormalizeRelative(includePath);

                                AssetFile af = DBAdapter.DB.Find<AssetFile>(a => a.AssetId == info.AssetId && a.Path == includePath);
                                if (af == null && info.SRPSupportPackage != null && info.SRPOriginalBackup != null && info.AssetId != info.SRPOriginalBackup.AssetId)
                                {
                                    af = DBAdapter.DB.Find<AssetFile>(a => a.AssetId == info.SRPOriginalBackup.AssetId && a.Path == includePath);
                                }
                                await AddToResultAndCheckForSRPSupportReplacement(info, result, af, processedGuids, ct);
                            }
                        }
                    }
                }

                if (extension == "shader")
                {
                    // custom editors
                    List<string> editorFiles = FindCustomEditors(content);
                    foreach (string include in editorFiles)
                    {
                        // remove potential namespace
                        string[] arr = include.Split('.');
                        string includePath = arr.Last() + ".cs"; // file could also be named differently than class name, would require code analysis
                        AssetFile af = DBAdapter.DB.Find<AssetFile>(a => a.AssetId == info.AssetId && a.FileName == includePath);
                        if (af == null && info.SRPSupportPackage != null && info.SRPOriginalBackup != null && info.AssetId != info.SRPOriginalBackup.AssetId)
                        {
                            af = DBAdapter.DB.Find<AssetFile>(a => a.AssetId == info.SRPOriginalBackup.AssetId && a.FileName == includePath);
                        }
                        await AddToResultAndCheckForSRPSupportReplacement(info, result, af, processedGuids, ct);
                    }
                }
            }
            else if (extension == "shadergraph" || extension == "shadersubgraph")
            {
                // check for referenced sub-graphs
                matches = GraphGuid.Matches(content);
                if (matches.Count == 0) matches = JsonGraphGuid.Matches(content);
            }
            else if (extension != "meta" && !content.StartsWith("%YAML"))
            {
                // reserialize prefabs on-the-fly by copying them over which will cause Unity to change the encoding upon refresh
                // this will not work but throw missing script errors instead if there are any attached
                Directory.CreateDirectory(_uniqueTempFolderPath);

                string targetFile = Path.Combine("Assets", _uniqueTempFolder, Path.GetFileName(path));

                // file can be locked, retry automatically
                if (!await IOUtils.TryCopyFile(path, targetFile, true))
                {
                    info.DependencyState = DependencyStateOptions.Failed;
                    return result;
                }
                AssetDatabase.Refresh();

#if UNITY_2021_2_OR_NEWER
                content = await File.ReadAllTextAsync(targetFile);
#else
                content = File.ReadAllText(targetFile);
#endif

                // if it still does not work, might be because of missing scripts inside prefabs
                if (!content.StartsWith("%YAML"))
                {
                    if (targetFile.ToLowerInvariant().EndsWith(".prefab"))
                    {
                        try
                        {
                            GameObject go = PrefabUtility.LoadPrefabContents(targetFile);
                            int removed = go.transform.RemoveMissingScripts();
                            if (removed > 0)
                            {
                                PrefabUtility.SaveAsPrefabAsset(go, targetFile);
                                PrefabUtility.UnloadPrefabContents(go);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Invalid prefab '{info}' encountered: {e.Message}");
                            info.DependencyState = DependencyStateOptions.Failed;
                            return result;
                        }

#if UNITY_2021_2_OR_NEWER
                        content = await File.ReadAllTextAsync(targetFile);
#else
                        content = File.ReadAllText(targetFile);
#endif

                        // final check (.asset are often binary files so don't fail for these)
                        if (!content.StartsWith("%YAML"))
                        {
                            if (extension != "asset") info.DependencyState = DependencyStateOptions.NotPossible;
                            return result;
                        }
                    }
                    else
                    {
                        if (extension != "asset") info.DependencyState = DependencyStateOptions.NotPossible;
                        return result;
                    }
                }
            }

            if (matches == null) matches = FileGuid.Matches(content);
            List<string> guids = matches.Cast<Match>()
                .Select(m => m.Groups[1].Value).Distinct()
                .Where(g => g != info.Guid && !processedGuids.ContainsKey(g)) // ignore self & break recursion
                .ToList();
            if (guids.Count == 0) return result;
            List<AssetFile> afCache = DBAdapter.DB.Table<AssetFile>().Where(a => a.AssetId == info.AssetId && guids.Contains(a.Guid)).ToList();

            foreach (string guid in guids)
            {
                // search strategy:
                // if there is an SRP package available, check if the dependency is in there and use that one
                // if not, check if the dependency is in the original package and use that one
                // if not, check if the dependency is in any other package and use that one
                AssetFile af = null;
                if (info.SRPSupportFiles != null) af = info.SRPSupportFiles.FirstOrDefault(f => f.Guid == guid);
                if (af == null) af = afCache.FirstOrDefault(a => a.AssetId == info.AssetId && a.Guid == guid);
                if (af == null && info.SRPSupportPackage != null && info.AssetId != info.SRPOriginalBackup.AssetId)
                {
                    af = DBAdapter.DB.Find<AssetFile>(a => a.AssetId == info.SRPOriginalBackup.AssetId && a.Guid == guid);
                }

                // potentially do cross-package search
                AssetInfo workInfo = info;
                if (af == null && AI.Config.allowCrossPackageDependencies)
                {
                    af = DBAdapter.DB.Find<AssetFile>(a => a.Guid == guid);
                    if (af == null)
                    {
                        processedGuids.TryAdd(guid, true); // we tried it all, give up
                        continue;
                    }

                    // break-out to other package
                    Asset crossAsset = null;
                    List<Asset> candidates = DBAdapter.DB.Table<Asset>().Where(a => a.Id == af.AssetId).ToList();
                    if (crossAsset == null && _isOnURP) crossAsset = candidates.FirstOrDefault(a => a.SafeName.ToLowerInvariant().Contains("urp"));
                    if (crossAsset == null && _isOnHDRP) crossAsset = candidates.FirstOrDefault(a => a.SafeName.ToLowerInvariant().Contains("hdrp"));
                    if (crossAsset == null) crossAsset = candidates.FirstOrDefault();
                    if (crossAsset != null)
                    {
                        workInfo = new AssetInfo(info).CopyFrom(crossAsset);

                        workInfo.SRPSupportPackage = null;
                        workInfo.SRPOriginalBackup = null;
                        workInfo.SRPMainReplacement = null;
                        workInfo.SRPSupportFiles = null;

                        if (!workInfo.CrossPackageDependencies.Any(p => p.Id == crossAsset.Id)) workInfo.CrossPackageDependencies.Add(crossAsset);
                        if (!info.CrossPackageDependencies.Any(p => p.Id == crossAsset.Id)) info.CrossPackageDependencies.Add(crossAsset);
                    }
                }

                // ignore missing guids as they are not in the package, so we can't do anything about them
                await AddToResultAndCheckForSRPSupportReplacement(workInfo, result, af, processedGuids, ct);
            }

            return result.Distinct().ToList();
        }

        private void PreparePipelineDependencies(AssetInfo info)
        {
            info.SRPOriginalBackup = null;
            info.SRPSupportPackage = null;
            info.SRPMainReplacement = null;
            info.SRPUsed = false;

            string targetSRPVersion;
            if (_isOnURP)
            {
                targetSRPVersion = AssetUtils.GetURPVersion();
            }
            else if (_isOnHDRP)
            {
                targetSRPVersion = AssetUtils.GetHDRPVersion();
            }
            else
            {
                return;
            }

            if (info.SRPSupportPackage == null)
            {
                // Query sub-packages by render pipeline compatibility flags
                string compatibilityField = _isOnURP ? "URPCompatible" : "HDRPCompatible";

                // Check first with version number supplied in case there are versioned packages
                List<Asset> srpCandidates = DBAdapter.DB.Query<Asset>($"select * from Asset where ParentId=? and Exclude=0 and {compatibilityField}=1 "
                    + (targetSRPVersion != null ? $" and (SafeName like '%{targetSRPVersion}%' or DisplayName like '%{targetSRPVersion}%')" : "")
                    + " order by SafeName", info.AssetId);

                // if nothing was found, try again without version
                if (srpCandidates.Count == 0)
                {
                    srpCandidates = DBAdapter.DB.Query<Asset>($"select * from Asset where ParentId=? and Exclude=0 and {compatibilityField}=1 order by SafeName", info.AssetId);
                }
                if (srpCandidates.Count == 0) return;

                if (srpCandidates.Count > 1) Debug.LogWarning($"Multiple potential SRP candidate packages found for package '{info}'. Using last: {string.Join(", ", srpCandidates.Select(a => a.SafeName))}");

                // some packages have sub-set packages, use a heuristic to find the "all" package
                info.SRPSupportPackage = srpCandidates.FirstOrDefault(c => c.SafeName.ToLowerInvariant().Contains("all"));

                if (info.SRPSupportPackage == null) info.SRPSupportPackage = srpCandidates.Last();
                info.SRPOriginalBackup = new AssetInfo(info);
                info.CrossPackageDependencies.Add(info.SRPSupportPackage);
            }

            // Use parameterized query for SRP support files
            info.SRPSupportFiles = DBAdapter.DB.Query<AssetFile>("SELECT * FROM AssetFile WHERE AssetId=?", info.SRPSupportPackage.Id);

            // check main file as well, some packages have dedicated prefabs etc.
            AssetFile srpFile = info.SRPSupportFiles.FirstOrDefault(f => f.Guid == info.Guid);
            if (srpFile != null)
            {
                info.SRPMainReplacement = srpFile;
                info.SRPUsed = true;
            }
        }

        private async Task AddToResultAndCheckForSRPSupportReplacement(AssetInfo info, List<AssetFile> result, AssetFile af, ConcurrentDictionary<string, bool> processedGuids, CancellationToken ct)
        {
            if (af == null || af.Guid == null || processedGuids.ContainsKey(af.Guid)) return;

            // check if there is an URP file with matching GUID and replace the original dependency with that one
            if (info.SRPSupportFiles != null && af.AssetId != info.SRPSupportPackage.Id)
            {
                AssetFile srpFile = info.SRPSupportFiles.FirstOrDefault(f => f.Guid == af.Guid);
                if (srpFile != null)
                {
                    af = srpFile;
                    info.SRPUsed = true;
                }
            }

            processedGuids.TryAdd(af.Guid, true);
            result.Add(af);

            await ScanDependencyResult(info, result, af, processedGuids, ct);
        }

        private async Task ScanDependencyResult(AssetInfo info, List<AssetFile> result, AssetFile af, ConcurrentDictionary<string, bool> processedGuids, CancellationToken ct)
        {
            AssetInfo workInfo = info;

            // switch to matching package if SRP is used and af does not refer to correct info
            if (info.SRPSupportPackage != null && af.AssetId != info.AssetId)
            {
                if (af.AssetId == info.SRPSupportPackage.Id)
                {
                    workInfo = new AssetInfo(workInfo).CopyFrom(info.SRPSupportPackage);
                }
                else
                {
                    workInfo = new AssetInfo(workInfo).CopyFrom(info.SRPOriginalBackup, true);
                }
            }

            string targetPath = await AI.EnsureMaterializedAsset(workInfo.ToAsset(), af, false, ct);
            if (targetPath == null)
            {
                Debug.LogWarning($"Could not materialize dependency: {af.Path}");
                processedGuids.TryAdd(af.Guid, true);
                return;
            }

            await DoCalculateDependencies(workInfo, targetPath, processedGuids, result, ct);

            // carry over results set during calculation
            if (workInfo.SRPUsed) info.SRPUsed = true;
            info.DependencyState = workInfo.DependencyState;
            foreach (Asset d in workInfo.CrossPackageDependencies)
            {
                if (!info.CrossPackageDependencies.Any(p => p.Id == d.Id)) info.CrossPackageDependencies.Add(d);
            }
        }

        private HashSet<string> FindIncludeFiles(string shaderCode, bool returnPackageReferences = false)
        {
            HashSet<string> result = new HashSet<string>();

            MatchCollection matches = IncludeFilesRegex.Matches(shaderCode);
            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    string value = match.Groups[1].Value;
                    if (!returnPackageReferences && value.StartsWith("Packages/")) continue;
                    if (value.StartsWith("./")) value = value.Substring(2); // remove leading './'
                    result.Add(value);
                }
            }

            return result;
        }

        private List<string> FindCustomEditors(string shaderCode)
        {
            List<string> result = new List<string>();

            MatchCollection matches = CustomEditorRegex.Matches(shaderCode);
            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    result.Add(match.Groups[1].Value);
                }
            }

            return result;
        }

        // for debugging purposes
        private static void ScanMetaFiles()
        {
            string[] packages = Directory.GetFiles(AI.GetMaterializeFolder(), "*.meta", SearchOption.AllDirectories);
            for (int i = 0; i < packages.Length; i++)
            {
                string content = File.ReadAllText(packages[i]);
                MatchCollection matches = FileGuid.Matches(content);
                if (matches.Count <= 1) continue;
                string pathFile = Path.Combine(Path.GetDirectoryName(packages[i]), "pathname");
                if (!File.Exists(pathFile)) continue;

                string pathName = File.ReadAllText(pathFile);
                if (pathName.ToLowerInvariant().Contains("fbx")
                    || pathName.ToLowerInvariant().Contains("shadergraph")
                    || pathName.ToLowerInvariant().Contains("ttf")
                    || pathName.ToLowerInvariant().Contains("otf")
                    || pathName.ToLowerInvariant().Contains("cs")
                    || pathName.ToLowerInvariant().Contains("png")
                    || pathName.ToLowerInvariant().Contains("obj")
                    || pathName.ToLowerInvariant().Contains("uxml")
                    || pathName.ToLowerInvariant().Contains("js")
                    || pathName.ToLowerInvariant().Contains("uss")
                    || pathName.ToLowerInvariant().Contains("nn")
                    || pathName.ToLowerInvariant().Contains("tss")
                    || pathName.ToLowerInvariant().Contains("inputactions")
                    || pathName.ToLowerInvariant().Contains("shader")) continue;

                Debug.Log($"Found meta file with multiple guids: {packages[i]}");
                break;
            }
        }
    }
}