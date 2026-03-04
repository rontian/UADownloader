using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetInventory
{
    [Serializable]
    public sealed class MoveFolderStep : ActionStep
    {
        public MoveFolderStep()
        {
            Key = "MoveFolder";
            Name = "Move Folder";
            Description = "Move the folder under the specified path to the target location.";
            Category = ActionCategory.FilesAndFolders;
            Parameters.Add(new StepParameter
            {
                Name = "Source",
                Description = "Path to a folder relative to the current project root.",
                DefaultValue = new ParameterValue("Assets/Temp")
            });
            Parameters.Add(new StepParameter
            {
                Name = "Target",
                Description = "Path to a folder relative to the current project root.",
                DefaultValue = new ParameterValue("Assets/Temp2")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            AssetDatabase.MoveAsset(parameters[0].stringValue, parameters[1].stringValue);
            await Task.Yield();
        }
    }
}
