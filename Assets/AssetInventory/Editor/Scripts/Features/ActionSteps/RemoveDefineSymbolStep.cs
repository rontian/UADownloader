using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public sealed class RemoveDefineSymbolStep : ActionStep
    {
        public RemoveDefineSymbolStep()
        {
            Key = "RemoveDefineSymbol";
            Name = "Remove Define Symbol";
            Description = "Remove a compiler define symbol.";
            Category = ActionCategory.Settings;
            Parameters.Add(new StepParameter
            {
                Name = "Symbol"
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            if (AssetUtils.HasDefine(parameters[0].stringValue)) AssetUtils.RemoveDefine(parameters[0].stringValue);

            await Task.Yield();
        }
    }
}