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
    public sealed class CompressFolderStep : ActionStep
    {
        public CompressFolderStep()
        {
            Key = "CompressFolder";
            Name = "Compress Folder";
            Description = "Compress a folder and all its contents to a zip archive.";
            Category = ActionCategory.FilesAndFolders;
            Parameters.Add(new StepParameter
            {
                Name = "Path",
                Description = "Path to the folder to compress (relative to the current project root).",
                DefaultValue = new ParameterValue("Assets/MyFolder"),
                ValueList = StepParameter.ValueType.Folder
            });
            Parameters.Add(new StepParameter
            {
                Name = "Archive",
                Description = "Path where the zip file will be created (relative to the current project root).",
                DefaultValue = new ParameterValue("Assets/MyArchive.zip")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
#if UNITY_2021_2_OR_NEWER
            string folderPath = parameters[0].stringValue;
            string archivePath = parameters[1].stringValue;

            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
            }

            // Delete existing archive if it exists
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            // Ensure the directory for the archive exists
            string archiveDirectory = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(archiveDirectory) && !Directory.Exists(archiveDirectory))
            {
                Directory.CreateDirectory(archiveDirectory);
            }

            // Create the zip archive
            ZipFile.CreateFromDirectory(folderPath, archivePath);
#else
            Debug.LogError("The folder compression step requires Unity 2021.2 or newer.");
#endif
            await Task.Yield();
        }
    }
}
