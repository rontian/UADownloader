using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public sealed class RunProcessStep : ActionStep
    {
        public RunProcessStep()
        {
            Key = "RunProcess";
            Name = "Run Command Line";
            Description = "Run a command line process.";
            Category = ActionCategory.Misc;
            Parameters.Add(new StepParameter
            {
                Name = "Command",
                Description = "Command to call."
            });
            Parameters.Add(new StepParameter
            {
                Name = "Params",
                Description = "Parameters to add to the command.",
                Optional = true
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            string result = IOUtils.ExecuteCommand(parameters[0].stringValue, parameters[1].stringValue);
            if (result == null)
            {
                throw new Exception($"Failed to execute command: {parameters[0].stringValue}");
            }
            await Task.Yield();
        }
    }
}