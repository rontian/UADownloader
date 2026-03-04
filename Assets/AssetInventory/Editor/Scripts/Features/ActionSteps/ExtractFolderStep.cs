using System;
using System.Collections.Generic;
#if UNITY_2021_2_OR_NEWER
using System.IO;
using System.IO.Compression;
#endif
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    [Serializable]
    public sealed class ExtractFolderStep : ActionStep
    {
        public ExtractFolderStep()
        {
            Key = "ExtractFolder";
            Name = "Extract Folder";
            Description = "Extract a zip archive to a target folder.";
            Category = ActionCategory.FilesAndFolders;
            Parameters.Add(new StepParameter
            {
                Name = "Archive",
                Description = "Path to the zip file to extract (relative to the current project root).",
                DefaultValue = new ParameterValue("Assets/MyArchive.zip")
            });
            Parameters.Add(new StepParameter
            {
                Name = "Target",
                Description = "Path where the archive contents will be extracted (relative to the current project root).",
                DefaultValue = new ParameterValue("Assets/ExtractedFolder"),
                ValueList = StepParameter.ValueType.Folder
            });
            Parameters.Add(new StepParameter
            {
                Name = "Overwrite",
                Description = "Whether to overwrite existing files in the target folder.",
                Type = StepParameter.ParamType.Bool,
                DefaultValue = new ParameterValue(true)
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
#if UNITY_2021_2_OR_NEWER
            string archivePath = parameters[0].stringValue;
            string targetFolder = parameters[1].stringValue;
            bool overwrite = parameters[2].boolValue;

            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"Archive not found: {archivePath}");
            }

            // Create target directory if it doesn't exist
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            // Extract the archive
            try
            {
                ZipFile.ExtractToDirectory(archivePath, targetFolder, overwrite);
            }
            catch (Exception ex) when (!overwrite)
            {
                Debug.Log($"Archive not extracted to '{targetFolder}' because overwrite is disabled and files already exist: {ex.Message}");
            }
#else
            Debug.LogError("The folder extraction step requires Unity 2021.2 or newer.");
#endif
            await Task.Yield();
        }
    }
}
