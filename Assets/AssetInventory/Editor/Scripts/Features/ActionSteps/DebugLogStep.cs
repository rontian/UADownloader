using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    [Serializable]
    public sealed class DebugLogStep : ActionStep
    {
        public DebugLogStep()
        {
            Key = "DebugLog";
            Name = "Debug Log";
            Description = "Print text to the debug log.";
            Parameters.Add(new StepParameter
            {
                Name = "Text",
                Type = StepParameter.ParamType.MultilineString
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            Debug.Log(parameters[0].stringValue);

            await Task.Yield();
        }
    }
}