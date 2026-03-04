using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public sealed class AddCompilerArgumentStep : ActionStep
    {
        public AddCompilerArgumentStep()
        {
            Key = "AddCompilerArgument";
            Name = "Add Compiler Arg";
            Description = "Add a compiler argument for the build.";
            Category = ActionCategory.Settings;
            Parameters.Add(new StepParameter
            {
                Name = "Argument"
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
#if UNITY_2021_3_OR_NEWER
            if (!AssetUtils.HasCompilerArgument(parameters[0].stringValue)) AssetUtils.AddCompilerArgument(parameters[0].stringValue);
#endif
            await Task.Yield();
        }
    }
}
