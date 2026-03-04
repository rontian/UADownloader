using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
#if UNITY_2021_2_OR_NEWER
using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR_WIN && NET_4_6
using System.Drawing;
#else
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
#endif
#else
using UnityEngine;
#endif

namespace AssetInventory
{
    public sealed class IncorrectPreviewsValidator : Validator
    {
        public IncorrectPreviewsValidator()
        {
            Type = ValidatorType.DB;
            Speed = ValidatorSpeed.Slow;
            Name = "Incorrect Previews";
            Description = "Scans all previews for either pink shaders or default preview icons instead of real previews.";
            FixCaption = "Schedule Recreation";
        }

        public override async Task Validate()
        {
            CurrentState = State.Scanning;

            string query = "select * from AssetFile where PreviewState = ? or PreviewState = ?";
            List<AssetInfo> files = DBAdapter.DB.Query<AssetInfo>(query, AssetFile.PreviewOptions.Provided, AssetFile.PreviewOptions.Custom).ToList();

            DBIssues = await GatherIssues(files);
            CurrentState = State.Completed;
        }

        public async Task Validate(List<AssetInfo> files)
        {
            CurrentState = State.Scanning;
            DBIssues = await GatherIssues(files);
            CurrentState = State.Completed;
        }

        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            string query = "update AssetFile set PreviewState = ? where Id = ?";
            foreach (AssetInfo info in DBIssues)
            {
                if (CancellationRequested) break;
                DBAdapter.DB.Execute(query, info.URPCompatible ? AssetFile.PreviewOptions.RedoMissing : AssetFile.PreviewOptions.Error, info.Id);
            }
            await Task.Yield();

            CurrentState = State.Idle;
        }

        private async Task<List<AssetInfo>> GatherIssues(List<AssetInfo> files)
        {
            List<AssetInfo> result = new List<AssetInfo>();

#if UNITY_2021_2_OR_NEWER
            string previewFolder = AI.GetPreviewFolder();
            Progress = 0;
            MaxProgress = files.Count;
            ProgressId = MetaProgress.Start("Gathering incorrect previews");

            // TODO: parallelize this loop but when done currently there are many main thread required exceptions
            foreach (AssetInfo file in files)
            {
                try
                {
                    Progress++;
                    MetaProgress.Report(ProgressId, Progress, MaxProgress, file.FileName);
                    if (CancellationRequested) break;
                    if (Progress % 50 == 0) await Task.Yield();

                    string previewFile = file.GetPreviewFile(previewFolder);
                    if (!PreviewManager.IsPreviewable(previewFile, true)) continue;
                    if (!File.Exists(previewFile)) continue;

#if UNITY_EDITOR_WIN && NET_4_6
                    using (Bitmap image = new Bitmap(IOUtils.ToLongPath(previewFile)))
#else
                    using (Image<Rgba32> image = Image.Load<Rgba32>(IOUtils.ToLongPath(previewFile)))
#endif
                    {
                        // scan for both issues in one go for performance
                        // use URP flag to differentiate between default cube and error shader issues
                        if (file.PreviewState == AssetFile.PreviewOptions.Provided)
                        {
                            if (PreviewManager.IsDefaultIcon(image))
                            {
                                file.URPCompatible = true;
                                result.Add(file);
                                continue;
                            }
                        }
                        if (PreviewManager.IsErrorShader(image))
                        {
                            file.URPCompatible = false;
                            result.Add(file);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Skipping validation for '{file.FileName}': {e.Message}");
                }
            }
            MetaProgress.Remove(ProgressId);
#else
            Debug.LogError("Incorrect previews validator is only supported in Unity 2021.2 or newer.");
            await Task.Yield();
#endif

            return result;
        }
    }
}