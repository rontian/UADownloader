using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetInventory
{
    [Serializable]
    public sealed class DeleteFolderStep : ActionStep
    {
        public DeleteFolderStep()
        {
            Key = "DeleteFolder";
            Name = "Delete Folder";
            Description = "Delete the folder under the specified path.";
            Category = ActionCategory.FilesAndFolders;
            Parameters.Add(new StepParameter
            {
                Name = "Path",
                Description = "Path of a folder relative to the current project root.",
                DefaultValue = new ParameterValue("Assets/Temp"),
                ValueList = StepParameter.ValueType.Folder
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            string path = parameters[0].stringValue;

            // Check if path is within Unity's asset database (Assets/, Packages/, etc.)
            bool isAssetPath = path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);

            if (isAssetPath)
            {
                // Use AssetDatabase for paths within the project to ensure proper refresh
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.Refresh();
            }
            else
            {
                // Use standard file system operations for paths outside the project
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }

            await Task.Yield();
        }
    }
}