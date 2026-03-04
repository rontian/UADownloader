using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetInventory
{
    public static class UnityPreviewGenerator
    {
        public const string PREVIEW_FOLDER = "_AssetInventoryPreviewsTemp";

        private const int MIN_PREVIEW_CACHE_SIZE = 200;
        private const float PREVIEW_TIMEOUT = 25f;
        private const int BREAK_INTERVAL = 30;
        private static readonly List<PreviewRequest> _requests = new List<PreviewRequest>();
        private static readonly object _requestsLock = new object();

        public static void Init(int expectedFileCount)
        {
            AssetPreview.SetPreviewTextureCacheSize(Mathf.Max(MIN_PREVIEW_CACHE_SIZE, expectedFileCount + 100));
        }

        public static int ActiveRequestCount()
        {
            lock (_requestsLock)
            {
                return _requests.Count;
            }
        }

        public static string GetPreviewWorkFolder()
        {
            string targetDir = Path.Combine(Application.dataPath, PREVIEW_FOLDER);
            Directory.CreateDirectory(targetDir);

            return targetDir;
        }

        public static bool RegisterPreviewRequest(AssetInfo info, string sourceFile, string previewDestination, Action<PreviewRequest> onDone, bool useSourceDirectly = false)
        {
            PreviewRequest request = Localize(info, sourceFile, previewDestination, onDone, useSourceDirectly);
            if (request == null) return false;

            // trigger creation, fetch later as it takes a while
            request.Obj = AssetDatabase.LoadAssetAtPath<Object>(request.TempFileRel);
            if (request.Obj != null)
            {
                request.TimeStarted = Time.realtimeSinceStartup;
                AssetPreview.GetAssetPreview(request.Obj);
                lock (_requestsLock)
                {
                    _requests.Add(request);
                }
            }
            else
            {
                Debug.LogError($"Queuing preview request failed for: {sourceFile}");
                return false;
            }

            return true;
        }

        public static PreviewRequest Localize(AssetInfo info, string sourceFile, string previewDestination, Action<PreviewRequest> onDone = null, bool useSourceDirectly = false)
        {
            PreviewRequest request = new PreviewRequest
            {
                Id = info.Id, SourceFile = sourceFile, DestinationFile = previewDestination, OnDone = onDone
            };

            // ensure target folder exists for subsequent write operations
            string resultDir = Path.GetDirectoryName(request.DestinationFile);
            Directory.CreateDirectory(resultDir);

            if (useSourceDirectly)
            {
                request.TempFile = sourceFile;
            }
            else
            {
                string targetDir = GetPreviewWorkFolder();
                request.TempFile = Path.Combine(targetDir, info.Id + Path.GetExtension(sourceFile));
                try
                {
                    File.Copy(sourceFile, request.TempFile, true);
                    string sourceFileMeta = sourceFile + ".meta";
                    if (File.Exists(sourceFileMeta)) File.Copy(sourceFileMeta, request.TempFile + ".meta", true);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"File is inaccessible. Preview could not be generated for '{sourceFile}': {e.Message}");
                    return null;
                }
            }

            request.TempFileRel = IOUtils.MakeProjectRelative(request.TempFile);
            if (!File.Exists(request.TempFileRel)) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); // can happen in very rare cases, not yet clear why
            if (!File.Exists(request.TempFileRel))
            {
                Debug.LogWarning($"Preview could not be generated for: {sourceFile}");
                return null;
            }
            AssetDatabase.ImportAsset(request.TempFileRel, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            return request;
        }

        public static void EnsureProgress()
        {
            // Unity is so buggy when creating previews, you need to hammer the GetAssetPreview call
            lock (_requestsLock)
            {
                for (int i = _requests.Count - 1; i >= 0; i--)
                {
                    PreviewRequest req = _requests[i];
                    if (req.Icon != null) continue;

                    req.Icon = AssetPreview.GetAssetPreview(req.Obj);
                    if (req.Icon == null && AssetPreview.IsLoadingAssetPreview(req.Obj.GetInstanceID()))
                    {
                        AssetPreview.GetAssetPreview(req.Obj);
                    }
                }
            }
        }

        public static async Task ExportPreviews(int limit = 0)
        {
            while (_requests.Count > limit)
            {
                await Task.Yield();
                List<PreviewRequest> requestsToCleanup = new List<PreviewRequest>();

                lock (_requestsLock)
                {
                    for (int i = _requests.Count - 1; i >= 0; i--)
                    {
                        PreviewRequest req = _requests[i];
                        if (req.Icon == null)
                        {
                            req.Icon = AssetPreview.GetAssetPreview(req.Obj);
                            if (req.Icon == null && AssetPreview.IsLoadingAssetPreview(req.Obj.GetInstanceID()))
                            {
                                AssetPreview.GetAssetPreview(req.Obj);
                                if (Time.realtimeSinceStartup - req.TimeStarted < PREVIEW_TIMEOUT) continue;
                            }
                            if (req.Icon == null) req.Icon = AssetPreview.GetAssetPreview(req.Obj);
                            if (req.Icon == null && Time.realtimeSinceStartup - req.TimeStarted < AI.Config.minPreviewWait) continue;
                        }

                        // still will not return something for all assets
                        if (req.Icon != null && req.Icon.isReadable)
                        {
#if UNITY_2021_2_OR_NEWER
                            // only verify non-image types as images work by default and can lead to false positives
                            string fileType = IOUtils.GetExtensionWithoutDot(req.TempFile).ToLowerInvariant();
                            if (AI.Config.verifyPreviews && !AI.TypeGroups[AI.AssetGroup.Images].Contains(fileType))
                            {
                                if (PreviewManager.IsErrorShader(req.Icon.ToImage()))
                                {
                                    req.Icon = null;
                                    req.IncompatiblePipeline = true;
                                }
                            }
#endif
                            byte[] bytes = req.Icon?.EncodeToPNG();
                            if (bytes != null)
                            {
                                // using await async variant will result in req.Icon getting set to Null in some cases for yet unknown reasons
                                try
                                {
                                    File.WriteAllBytes(req.DestinationFile, bytes);
                                }
                                catch (IOException ioEx)
                                {
                                    Debug.LogError($"Failed to write preview for '{req.SourceFile}'. Disk may be full: {ioEx.Message}");
                                    req.Icon = null; // Mark as failed
                                }
                            }
                        }
                        req.OnDone?.Invoke(req);

                        // check if file is directly in temp folder (means had no dependencies) or in a subdirectory (means had dependencies so cannot be deleted if there are more requests still in the queue)
                        if (Path.GetFileName(Path.GetDirectoryName(req.TempFile)) == PREVIEW_FOLDER)
                        {
                            // delete asset again, this will also null the Obj field
                            if (!AssetDatabase.DeleteAsset(req.TempFileRel))
                            {
                                requestsToCleanup.Add(req);
                            }
                        }

                        _requests.RemoveAt(i);
                        if (i % BREAK_INTERVAL == 0) break; // let editor breath in case many files are already indexed
                    }
                }

                // Handle cleanup outside the lock
                foreach (PreviewRequest req in requestsToCleanup)
                {
                    await IOUtils.DeleteFileOrDirectory(req.TempFile);
                    await IOUtils.DeleteFileOrDirectory(req.TempFile + ".meta");
                }
            }
        }

        public static void CleanUp()
        {
            lock (_requestsLock)
            {
                _requests.Clear();
            }

            string targetDir = Path.Combine(Application.dataPath, PREVIEW_FOLDER);
            if (!Directory.Exists(targetDir)) return;

            try
            {
                if (!AssetDatabase.DeleteAsset($"Assets/{PREVIEW_FOLDER}"))
                {
                    Directory.Delete(targetDir, true);
                    FileUtil.DeleteFileOrDirectory(targetDir + ".meta");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not remove temporary preview folder '{targetDir}'. Please do so manually: {e.Message}");
            }

            AssetDatabase.Refresh();
        }
    }

    public sealed class PreviewRequest
    {
        public int Id;
        public string SourceFile;
        public string TempFile;
        public string TempFileRel;
        public string DestinationFile;
        public Object Obj;
        public Action<PreviewRequest> OnDone;

        // runtime properties
        public float TimeStarted;
        public Texture2D Icon;
        public bool IncompatiblePipeline;
    }
}