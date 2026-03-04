using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetInventory
{
    [Serializable]
    public sealed class MoveFileStep : ActionStep
    {
        public MoveFileStep()
        {
            Key = "MoveFile";
            Name = "Move File";
            Description = "Move the file under the specified path to the target location.";
            Category = ActionCategory.FilesAndFolders;
            Parameters.Add(new StepParameter
            {
                Name = "Source",
                Description = "Path to a file relative to the current project root.",
                DefaultValue = new ParameterValue("Assets/Readme.md")
            });
            Parameters.Add(new StepParameter
            {
                Name = "Target",
                Description = "Path to a file relative to the current project root.",
                DefaultValue = new ParameterValue("Assets/Readme.md")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            AssetDatabase.MoveAsset(parameters[0].stringValue, parameters[1].stringValue);
            await Task.Yield();
        }
    }
}
