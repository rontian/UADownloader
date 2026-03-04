using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public sealed class CreateFolderStep : ActionStep
    {
        public CreateFolderStep()
        {
            Key = "CreateFolder";
            Name = "Create Folder";
            Description = "Create a folder under the specified path.";
            Category = ActionCategory.FilesAndFolders;
            Parameters.Add(new StepParameter
            {
                Name = "Path",
                Description = "Path of a folder relative to the current project root.",
                DefaultValue = new ParameterValue("Assets"),
                ValueList = StepParameter.ValueType.Folder
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            Directory.CreateDirectory(parameters[0].stringValue);
            await Task.Yield();
        }
    }
}
