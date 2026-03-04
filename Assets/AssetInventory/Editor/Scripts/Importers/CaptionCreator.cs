using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
using Microsoft.Extensions.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Net.Http;
#endif
using Newtonsoft.Json;
using UnityEngine;

namespace AssetInventory
{
    public sealed class CaptionCreator : AssetImporter
    {
        public async Task Run()
        {
            List<string> types = new List<string>();
            if (AI.Config.aiForPrefabs) types.AddRange(AI.TypeGroups[AI.AssetGroup.Prefabs]);
            if (AI.Config.aiForImages) types.AddRange(AI.TypeGroups[AI.AssetGroup.Images]);
            if (AI.Config.aiForModels) types.AddRange(AI.TypeGroups[AI.AssetGroup.Models]);

            string typeStr = string.Join("\",\"", types);
            string query = "select *, AssetFile.Id as Id from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId where Asset.Exclude = false and Asset.UseAI = true and AssetFile.Type in (\"" + typeStr + "\") and AssetFile.AICaption is null and (AssetFile.PreviewState = ? or AssetFile.PreviewState = ? or AssetFile.PreviewState = ?) order by Asset.Id desc";
            List<AssetInfo> files = DBAdapter.DB.Query<AssetInfo>(query, AssetFile.PreviewOptions.Custom, AssetFile.PreviewOptions.Provided, AssetFile.PreviewOptions.UseOriginal).ToList();

            await Run(files);
        }

        public async Task Run(List<AssetInfo> files)
        {
            if (files.Count == 0) return;

            if (AI.Config.aiBackend == 1)
            {
#if !UNITY_2021_2_OR_NEWER || !ASSET_INVENTORY_OLLAMA
                Debug.LogError("Ollama backend is not enabled. Go to Settings/Artificial Intelligence and enable it.");
                return;
#endif
            }

            string previewFolder = AI.GetPreviewFolder();

            int chunkSize = AI.Config.aiBackend == 0 ? AI.Config.blipChunkSize : 1;
            bool toolChainWorking = true;

            MainCount = files.Count;
            for (int i = 0; i < files.Count; i += chunkSize)
            {
                if (CancellationRequested) break;
                await Task.Delay(Mathf.RoundToInt(AI.Config.aiPause * 1000f)); // to prevent system crashes or overheating

                List<AssetInfo> fileChunk = files.Skip(i).Take(chunkSize).ToList();
                List<string> previewFiles = new List<string>();

                foreach (AssetInfo file in fileChunk)
                {
                    SetProgress(file.FileName, i + 1);

                    string previewFile = ValidatePreviewFile(file, previewFolder);
                    if (!string.IsNullOrEmpty(previewFile))
                    {
                        previewFiles.Add(previewFile);
                    }
                }
                if (previewFiles.Count == 0) continue;

                await Task.Run(async () =>
                {
                    List<CaptionResult> captions = await CaptionImage(previewFiles);
                    if (captions != null && captions.Count > 0)
                    {
                        for (int j = 0; j < captions.Count; j++)
                        {
                            if (captions[j].caption != null)
                            {
                                fileChunk[j].AICaption = captions[j].caption.Truncate(AI.Config.aiMaxCaptionLength);
                                DBAdapter.DB.Execute("update AssetFile set AICaption=? where Id=?", fileChunk[j].AICaption, fileChunk[j].Id);

                                if (AI.Config.logAICaptions)
                                {
                                    Debug.Log($"Caption: {captions[j].caption} ({fileChunk[j].FileName})");
                                }
                            }
                            else if (i == 0)
                            {
                                if (!AI.Config.aiContinueOnEmpty) toolChainWorking = false;
                            }
                        }
                    }
                    else if (i == 0)
                    {
                        if (AI.Config.aiBackend == 0 && !AI.Config.aiContinueOnEmpty) toolChainWorking = false;
                    }
                });
                if (!toolChainWorking) break;
            }
        }

        public static async Task<List<CaptionResult>> CaptionImage(List<string> filenames, string modelName = null)
        {
            List<CaptionResult> resultList = null;

            switch (AI.Config.aiBackend)
            {
                case 0:
                    string blipType = AI.Config.blipType == 1 ? "--large" : "";
                    string gpuUsage = AI.Config.blipUseGPU ? "--gpu" : "";
                    string nameList = "\"" + string.Join("\" \"", filenames.Select(IOUtils.ToShortPath)) + "\"";
                    string command = AI.Config.blipPath != null ? Path.Combine(AI.Config.blipPath, "blip-caption") : "blip-caption";
                    string result = IOUtils.ExecuteCommand(command, $"{blipType} {gpuUsage} --json {nameList}");

                    if (string.IsNullOrWhiteSpace(result)) return null;

                    try
                    {
                        resultList = JsonConvert.DeserializeObject<List<CaptionResult>>(result);

                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Could not parse Blip result '{result}': {e.Message}");
                    }
                    break;

                case 1:
#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
                    if (modelName == null) modelName = AI.Config.ollamaModel;
                    IChatClient client = new OllamaChatClient(new Uri(Intelligence.OLLAMA_SERVICE_URL), modelName);
                    // FIXME: still a performance issue with this, as it uses different defaults than MS resulting in model being offloaded all the time, around 10x slower because of this
                    // https://github.com/awaescher/OllamaSharp/issues/249
                    // OllamaApiClient client = new OllamaApiClient(new Uri(Intelligence.OLLAMA_SERVICE_URL), modelName);

                    resultList = new List<CaptionResult>();
                    foreach (string file in filenames)
                    {
                        try
                        {
                            using Image<Rgba32> img = await Image.LoadAsync<Rgba32>(file);

                            // resize to minimum size if necessary
                            int w = img.Width;
                            int h = img.Height;

                            if (h < 2) continue; // scales too high if corrected

                            double scale = Math.Max((float)AI.Config.aiMinSize / w, (float)AI.Config.aiMinSize / h);
                            if (scale > 1.0)
                            {
                                int newW = (int)Math.Ceiling(w * scale);
                                int newH = (int)Math.Ceiling(h * scale);
                                img.Mutate(x => x.Resize(newW, newH));
                            }
                            using MemoryStream ms = new MemoryStream();
                            string ext = Path.GetExtension(file).ToLowerInvariant();
                            IImageEncoder encoder = ext == ".png" ? new PngEncoder() : new JpegEncoder();
                            await img.SaveAsync(ms, encoder); // save to memory stream
                            byte[] imgBytes = ms.ToArray();
                            string mime = ext == ".png" ? "image/png" : "image/jpeg";
                            string prompt = string.IsNullOrWhiteSpace(AI.Config.ollamaPrompt)
                                ? Intelligence.ModelPrompt
                                : AI.Config.ollamaPrompt;

                            // create msg and attachment
                            ChatMessage msg = new ChatMessage(ChatRole.User, prompt);
                            msg.Contents.Add(new DataContent(imgBytes, mime));

                            ChatResponse response = await client.GetResponseAsync(msg);
                            resultList.Add(new CaptionResult
                            {
                                path = file,
                                caption = response.Text
                            });
                        }
                        catch (HttpRequestException httpE)
                        {
                            Debug.LogError($"Could not connect to Ollama for '{file}': {httpE.Message}");
                        }
                        catch (InvalidOperationException opE)
                        {
                            Debug.LogError($"Ollama model error for '{file}', image might be too small: {opE.Message}");
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Could not get Ollama result for '{file}': {e.Message}");
                        }
                    }
#else
                    await Task.Yield();
#endif
                    break;
            }
            resultList?.ForEach(r => r.caption =
                StringUtils.StripTags(r.caption, true)
                    .Trim()
                    .TrimStart('"')
                    .TrimEnd('"'));

            return resultList;
        }
    }

    public class CaptionResult
    {
        public string path;
        public string caption;
    }
}