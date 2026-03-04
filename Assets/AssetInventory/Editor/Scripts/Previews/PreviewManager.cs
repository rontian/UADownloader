using System;
using System.IO;
using System.Threading.Tasks;
#if UNITY_2021_2_OR_NEWER
using UnityEditor;
#if UNITY_EDITOR_WIN && NET_4_6
using System.Drawing;
#else
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
#endif
using Color = UnityEngine.Color;
#endif
using UnityEngine;

namespace AssetInventory
{
    public sealed class PreviewManager
    {
        private const int MAX_REQUESTS = 50;
        private const int OPEN_REQUESTS = 5;

#if UNITY_2021_2_OR_NEWER
        private static ulong _textureIconHash;
        private static ulong _audioIconHash;

#if UNITY_EDITOR_WIN && NET_4_6
        public static bool IsErrorShader(Bitmap image) => ImageUtils.IsErrorPreview(image);
        public static bool IsDefaultIcon(Bitmap image)
        {
            if (_textureIconHash == 0)
            {
                Bitmap textureIcon = ((Texture2D)EditorGUIUtility.IconContent("d_texture icon").image).MakeReadable().ToImage();
                _textureIconHash = ImageUtils.ComputePerceptualHash(textureIcon);
            }
            if (_audioIconHash == 0)
            {
                Bitmap audioIcon = ((Texture2D)EditorGUIUtility.IconContent("audioclip icon").image).MakeReadable().ToImage();
                _audioIconHash = ImageUtils.ComputePerceptualHash(audioIcon);
            }
#else
        public static bool IsErrorShader(Image<Rgba32> image) => ImageUtils.IsErrorPreview(image);
        public static bool IsDefaultIcon(Image<Rgba32> image)
        {
            if (_textureIconHash == 0)
            {
                Image<Rgba32> textureIcon = ((Texture2D)EditorGUIUtility.IconContent("d_texture icon").image).MakeReadable().ToImage();
                _textureIconHash = ImageUtils.ComputePerceptualHash(textureIcon);
            }
            if (_audioIconHash == 0)
            {
                Image<Rgba32> audioIcon = ((Texture2D)EditorGUIUtility.IconContent("audioclip icon").image).MakeReadable().ToImage();
                _audioIconHash = ImageUtils.ComputePerceptualHash(audioIcon);
            }
#endif
            return ImageUtils.HasDominantColor(image, new Color(128, 216, 255))
                || ImageUtils.AreSimilar(image, _textureIconHash)
                || ImageUtils.AreSimilar(image, _audioIconHash);
        }
#endif

        public static async Task<bool> Create(AssetInfo info, string sourcePath = null, Action onCreated = null, Action<PreviewRequest> onDone = null)
        {
            // check if previewable at all
            if (!IsPreviewable(info.FileName, true, info)) return false;

            if (sourcePath == null)
            {
                sourcePath = await AI.EnsureMaterializedAsset(info);
                if (sourcePath == null)
                {
                    if (info.PreviewState != AssetFile.PreviewOptions.Provided)
                    {
                        DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", AssetFile.PreviewOptions.Error, info.Id);
                    }
                    return false;
                }
            }

            // short-cut for directly accessible small media images to avoid copying these around
            if (info.AssetSource == Asset.Source.Directory &&
                AI.Config.directMediaPreviews &&
                info.Width > 0 && info.Height > 0 &&
                ((AI.Config.upscalePreviews && info.Width <= AI.Config.upscaleSize && info.Height <= AI.Config.upscaleSize) || (!AI.Config.upscalePreviews && info.Width <= 128 && info.Height <= 128)) &&
                (info.Type == "png" || info.Type == "jpg" || info.Type == "jpeg"))
            {
                PreviewRequest req = new PreviewRequest {DestinationFile = sourcePath, Id = info.Id, Icon = Texture2D.grayTexture, SourceFile = sourcePath};
                StorePreviewResult(req);
                onCreated?.Invoke();
                onDone?.Invoke(req);

                return true;
            }

            string previewFile = info.GetPreviewFile(AI.GetPreviewFolder());
            string animPreviewFile = info.GetPreviewFile(AI.GetPreviewFolder(), true);

            Texture2D texture = null;
            Texture2D animTexture = null;
            bool directPreview = false;

#if UNITY_2021_2_OR_NEWER
            if (ImageUtils.SYSTEM_IMAGE_TYPES.Contains(info.Type))
            {
                // take shortcut for images and skip Unity importer
                if (ImageUtils.ResizeImage(sourcePath, previewFile, AI.Config.upscaleSize, !AI.Config.upscaleLossless))
                {
                    PreviewRequest req = new PreviewRequest {DestinationFile = previewFile, Id = info.Id, Icon = Texture2D.grayTexture, SourceFile = sourcePath};
                    StorePreviewResult(req);
                    onCreated?.Invoke();
                    onDone?.Invoke(req);
                }
                else
                {
                    // try to use original preview
                    string originalPreviewFile = DerivePreviewFile(sourcePath);
                    if (File.Exists(originalPreviewFile))
                    {
                        File.Copy(originalPreviewFile, previewFile, true);
                        PreviewRequest req = new PreviewRequest {DestinationFile = previewFile, Id = info.Id, Icon = Texture2D.grayTexture, SourceFile = originalPreviewFile};
                        StorePreviewResult(req);
                        info.PreviewState = AssetFile.PreviewOptions.Provided;
                        DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", AssetFile.PreviewOptions.Provided, info.Id);
                        onCreated?.Invoke();
                        onDone?.Invoke(req);
                    }
                    else if (info.PreviewState != AssetFile.PreviewOptions.Provided)
                    {
                        info.PreviewState = AssetFile.PreviewOptions.Error;
                        DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", AssetFile.PreviewOptions.Error, info.Id);
                        onDone?.Invoke(null);
                    }
                }
            }
            else
#endif
            if (AI.IsFileType(info.FileName, AI.AssetGroup.Fonts))
            {
                PreviewRequest req = UnityPreviewGenerator.Localize(info, sourcePath, previewFile);
                texture = FontPreviewGenerator.Create(req.TempFileRel, AI.Config.upscaleSize);
                directPreview = true;
            }
#if UNITY_EDITOR_WIN
            else if (AI.IsFileType(info.FileName, AI.AssetGroup.Videos))
            {
                PreviewRequest req = UnityPreviewGenerator.Localize(info, sourcePath, previewFile);

                // first static
                texture = await VideoPreviewGenerator.Create(req.TempFileRel, AI.Config.upscaleSize, 1, clip =>
                {
                    info.Width = (int)clip.width;
                    info.Height = (int)clip.height;
                    info.Length = (float)clip.length;
                    DBAdapter.DB.Execute("update AssetFile set Width=?, Height=?, Length=? where Id=?", info.Width, info.Height, info.Length, info.Id);
                });

                // give time for video player cleanup, might result in black textures otherwise when done in quick succession
                await Task.Yield();

                if (texture != null)
                {
                    // now animated
                    animTexture = await VideoPreviewGenerator.Create(req.TempFileRel, AI.Config.upscaleSize, AI.Config.animationGrid * AI.Config.animationGrid, _ => { });
                    await Task.Yield();
                }

                directPreview = true;
            }
#endif
            else
            {
                // potential short-cut: check if already imported
                if (info.InProject)
                {
                    sourcePath = info.ProjectPath;
                }
                else
                {
                    // import through Unity
                    if (DependencyAnalysis.NeedsScan(info.Type))
                    {
                        if (info.DependencyState == AssetInfo.DependencyStateOptions.Unknown || info.DependencyState == AssetInfo.DependencyStateOptions.Partial)
                        {
                            await AI.CalculateDependencies(info);
                        }
                        if (info.Dependencies.Count > 0 || info.SRPMainReplacement != null)
                        {
                            // ensure to remove ~ from folders (sample flag) as otherwise Unity will not generate previews 
                            sourcePath = await AI.CopyTo(info, UnityPreviewGenerator.GetPreviewWorkFolder(), true, false, false, false, false, true);
                        }
                        if (sourcePath == null) // can happen when file system issues occur
                        {
                            if (info.PreviewState != AssetFile.PreviewOptions.Provided)
                            {
                                info.PreviewState = AssetFile.PreviewOptions.Error;
                                DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", AssetFile.PreviewOptions.Error, info.Id);
                                onDone?.Invoke(null);
                            }
                            return false;
                        }
                    }
                }

                if (!UnityPreviewGenerator.RegisterPreviewRequest(info, sourcePath, previewFile, req =>
                    {
                        AssetFile af = StorePreviewResult(req);
                        if (req.Icon != null)
                        {
                            onCreated?.Invoke();
                        }
                        else if (req.IncompatiblePipeline)
                        {
                            Debug.LogWarning($"Unity did return a pink preview image for '{info.FileName}' due to the currently incompatible render pipeline. Reverting to previous version.");
                        }
                        else if (af.PreviewState != AssetFile.PreviewOptions.Error) // otherwise error already logged
                        {
                            if (AI.Config.LogPreviewCreation) Debug.LogWarning($"Unity did not return any preview image for '{info.FileName}'.");
                        }
                        onDone?.Invoke(req);
                    }, info.InProject || info.Dependencies?.Count > 0))
                {
                    if (info.PreviewState != AssetFile.PreviewOptions.Provided)
                    {
                        info.PreviewState = AssetFile.PreviewOptions.Error;
                        DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", AssetFile.PreviewOptions.Error, info.Id);
                    }
                    return false;
                }

                await EnsureProgress();
            }
            if (directPreview)
            {
                if (texture != null)
                {
                    try
                    {
#if UNITY_2021_2_OR_NEWER
                        await File.WriteAllBytesAsync(previewFile, texture.EncodeToPNG());
#else
                        File.WriteAllBytes(previewFile, texture.EncodeToPNG());
#endif
                    }
                    catch (IOException ioEx)
                    {
                        Debug.LogError($"Failed to write preview '{previewFile}'. Disk may be full: {ioEx.Message}");
                        UnityEngine.Object.DestroyImmediate(texture);
                        if (animTexture != null) UnityEngine.Object.DestroyImmediate(animTexture);
                        return false;
                    }

                    PreviewRequest req = new PreviewRequest {DestinationFile = previewFile, Id = info.Id, Icon = Texture2D.grayTexture, SourceFile = sourcePath};
                    StorePreviewResult(req);
                    onCreated?.Invoke();
                    onDone?.Invoke(req);

                    if (animTexture != null)
                    {
                        try
                        {
#if UNITY_2021_2_OR_NEWER
                            await File.WriteAllBytesAsync(animPreviewFile, animTexture.EncodeToPNG());
#else
                            File.WriteAllBytes(animPreviewFile, animTexture.EncodeToPNG());
#endif
                        }
                        catch (IOException ioEx)
                        {
                            Debug.LogError($"Failed to write animated preview '{animPreviewFile}'. Disk may be full: {ioEx.Message}");
                        }
                        UnityEngine.Object.DestroyImmediate(animTexture);
                    }

                    UnityEngine.Object.DestroyImmediate(texture);
                    return true;
                }
                if (info.PreviewState != AssetFile.PreviewOptions.Provided)
                {
                    info.PreviewState = AssetFile.PreviewOptions.Error;
                    DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", AssetFile.PreviewOptions.Error, info.Id);
                    onDone?.Invoke(null);
                    return false;
                }
            }
            return true;
        }

        public static bool IsPreviewable(string file, bool includeComplex, AssetInfo autoMarkNA = null)
        {
            bool previewable = false;
            if (!file.Contains("__MACOSX"))
            {
                if (includeComplex)
                {
                    previewable = AI.IsFileType(file, AI.AssetGroup.Audio)
                        || AI.IsFileType(file, AI.AssetGroup.Images)
#if UNITY_EDITOR_WIN
                        || AI.IsFileType(file, AI.AssetGroup.Videos)
#endif
                        || AI.IsFileType(file, AI.AssetGroup.Models)
                        || AI.IsFileType(file, AI.AssetGroup.Fonts)
                        || AI.IsFileType(file, AI.AssetGroup.Prefabs)
                        || AI.IsFileType(file, AI.AssetGroup.Materials);
                }
                else
                {
                    previewable = AI.IsFileType(file, AI.AssetGroup.Audio)
                        || AI.IsFileType(file, AI.AssetGroup.Images)
#if UNITY_EDITOR_WIN
                        || AI.IsFileType(file, AI.AssetGroup.Videos)
#endif
                        || AI.IsFileType(file, AI.AssetGroup.Fonts);
                }
            }
            if (!previewable && autoMarkNA != null)
            {
                if (autoMarkNA.PreviewState != AssetFile.PreviewOptions.Provided)
                {
                    autoMarkNA.PreviewState = AssetFile.PreviewOptions.NotApplicable;
                    DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", autoMarkNA.PreviewState, autoMarkNA.Id);
                }
            }

            return previewable;
        }

        private static async Task EnsureProgress()
        {
            UnityPreviewGenerator.EnsureProgress();
            if (UnityPreviewGenerator.ActiveRequestCount() > MAX_REQUESTS) await UnityPreviewGenerator.ExportPreviews(OPEN_REQUESTS);
        }

        public static string DerivePreviewFile(string sourcePath)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(sourcePath)), "preview.png");
        }

        public static AssetFile StorePreviewResult(PreviewRequest req)
        {
            AssetFile af = DBAdapter.DB.Find<AssetFile>(req.Id);
            if (af == null) return null;

            if (!File.Exists(req.DestinationFile))
            {
                if (af.PreviewState != AssetFile.PreviewOptions.Provided)
                {
                    af.PreviewState = AssetFile.PreviewOptions.Error;
                    DBAdapter.DB.Update(af);
                }
                return af;
            }

            if (req.Obj != null)
            {
                if (req.Obj is Texture2D tex)
                {
                    af.Width = tex.width;
                    af.Height = tex.height;
                }
                if (req.Obj is AudioClip clip)
                {
                    af.Length = clip.length;
                }
            }

            if (req.SourceFile == req.DestinationFile)
            {
                af.PreviewState = AssetFile.PreviewOptions.UseOriginal;
            }
            else
            {
                // do not remove originally supplied previews even in case of error
                af.PreviewState = req.Icon != null ?
                    AssetFile.PreviewOptions.Custom :
                    (af.PreviewState != AssetFile.PreviewOptions.Provided ? AssetFile.PreviewOptions.Error : AssetFile.PreviewOptions.Provided);
            }

            // reset data to be recreated
            if (af.PreviewState != AssetFile.PreviewOptions.Error)
            {
                af.Hue = -1f;
                af.AICaption = null;
            }
            else
            {
                if (AI.Config.LogPreviewCreation) Debug.LogWarning($"No preview returned for '{req.SourceFile}'");
            }

            DBAdapter.DB.Update(af);

            return af;
        }
    }
}