using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public sealed class CopyFileStep : ActionStep
    {
        public CopyFileStep()
        {
            Key = "CopyFile";
            Name = "Copy File";
            Description = "Copy the file under the specified path to the target location.";
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
            File.Copy(parameters[0].stringValue, parameters[1].stringValue, true);
            await Task.Yield();
        }
    }
}
