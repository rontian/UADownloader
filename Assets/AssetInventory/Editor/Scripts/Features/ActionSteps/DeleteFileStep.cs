using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetInventory
{
    [Serializable]
    public sealed class DeleteFileStep : ActionStep
    {
        public DeleteFileStep()
        {
            Key = "DeleteFile";
            Name = "Delete File";
            Description = "Delete the file under the specified path.";
            Category = ActionCategory.FilesAndFolders;
            Parameters.Add(new StepParameter
            {
                Name = "Path",
                Description = "Path to a file relative to the current project root.",
                DefaultValue = new ParameterValue("Assets/Readme.md")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            AssetDatabase.DeleteAsset(parameters[0].stringValue);
            await Task.Yield();
        }
    }
}
