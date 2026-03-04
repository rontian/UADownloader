using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetInventory
{
    [Serializable]
    public sealed class MessageDialogStep : ActionStep
    {
        public MessageDialogStep()
        {
            Key = "MessageDialog";
            Name = "Message Dialog";
            Description = "Show text in a popup dialog.";
            Parameters.Add(new StepParameter
            {
                Name = "Text",
                Type = StepParameter.ParamType.MultilineString
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            EditorUtility.DisplayDialog("Message", parameters[0].stringValue, "OK");

            await Task.Yield();
        }
    }
}
