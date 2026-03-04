using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SQLite;
using UnityEngine;

namespace AssetInventory
{
    public sealed class ColorImporter : AssetImporter
    {
        public async Task Run()
        {
            string previewFolder = AI.GetPreviewFolder();

            TableQuery<AssetFile> query = DBAdapter.DB.Table<AssetFile>()
                .Where(a => 
                    (a.PreviewState == AssetFile.PreviewOptions.Custom || a.PreviewState == AssetFile.PreviewOptions.Provided || a.PreviewState == AssetFile.PreviewOptions.UseOriginal) 
                    && a.Hue < 0);

            // skip audio files per default
            if (!AI.Config.extractAudioColors)
            {
                foreach (string t in AI.TypeGroups[AI.AssetGroup.Audio])
                {
                    query = query.Where(a => a.Type != t);
                }
            }

            List<AssetFile> files = query.ToList();

            int maxDegreeOfParallelism = Environment.ProcessorCount;
            SemaphoreSlim semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
            List<Task> tasks = new List<Task>();

            MainCount = files.Count;
            for (int i = 0; i < files.Count; i++)
            {
                if (CancellationRequested) break;
                await semaphore.WaitAsync();
                await AI.Cooldown.Do();

                AssetFile file = files[i];
                SetProgress(file.FileName, i + 1);

                async Task ProcessFile(AssetFile curFile)
                {
                    try
                    {
                        string previewFile = ValidatePreviewFile(curFile, previewFolder);
                        if (string.IsNullOrEmpty(previewFile)) return;

                        Texture2D texture = await AssetUtils.LoadLocalTexture(previewFile, false);
                        if (texture != null)
                        {
                            curFile.Hue = ImageUtils.GetHue(texture);
                            UnityEngine.Object.DestroyImmediate(texture);
                            Persist(curFile);
                        }
                        else
                        {
                            curFile.PreviewState = AssetFile.PreviewOptions.None;
                            DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", curFile.PreviewState, curFile.Id);                                                        
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }

                tasks.Add(ProcessFile(file));
            }
            await Task.WhenAll(tasks);
        }
    }
}