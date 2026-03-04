using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public sealed class AddDefineSymbolStep : ActionStep
    {
        public AddDefineSymbolStep()
        {
            Key = "AddDefineSymbol";
            Name = "Add Define Symbol";
            Description = "Add a compiler define symbol.";
            Category = ActionCategory.Settings;
            Parameters.Add(new StepParameter
            {
                Name = "Symbol"
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            if (!AssetUtils.HasDefine(parameters[0].stringValue)) AssetUtils.AddDefine(parameters[0].stringValue);

            await Task.Yield();
        }
    }
}