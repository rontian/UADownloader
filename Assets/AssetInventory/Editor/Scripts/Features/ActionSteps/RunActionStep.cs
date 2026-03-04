using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public sealed class RunActionStep : ActionStep
    {
        public RunActionStep()
        {
            List<Tuple<string, ParameterValue>> options = new List<Tuple<string, ParameterValue>>();
            foreach (UpdateAction action in AI.Actions.Actions.Where(a => !a.hidden))
            {
                options.Add(new Tuple<string, ParameterValue>(action.name, new ParameterValue(action.key)));
            }

            Key = "RunAction";
            Name = "Run Action";
            Description = "Run another custom or predefined action.";
            Category = ActionCategory.Misc;
            Parameters.Add(new StepParameter
            {
                Name = "Action",
                Description = "Action to run.",
                Type = StepParameter.ParamType.String,
                ValueList = StepParameter.ValueType.Custom,
                Options = options
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            string key = parameters[0].stringValue;
            if (key.StartsWith(ActionHandler.ACTION_USER))
            {
                // user actions are more complex and need to be triggered differently
                int idx = int.Parse(key.Substring(ActionHandler.ACTION_USER.Length));
                CustomAction action = DBAdapter.DB.Find<CustomAction>(idx);
                await AI.Actions.RunUserAction(action);
            }
            else
            {
                // internal action
                await AI.Actions.RunAction(key);
            }
        }
    }
}