using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>
    /// Action step to define a text variable that can be used in subsequent steps.
    /// Variables are referenced using the $varname syntax in other step parameters.
    /// </summary>
    [Serializable]
    public sealed class SetTextVariableStep : ActionStep
    {
        public SetTextVariableStep()
        {
            Key = "SetTextVariable";
            Name = "Set Text Variable";
            Description = "Define a text variable that can be used in subsequent steps using $varname syntax.";
            Category = ActionCategory.Misc;

            Parameters.Add(new StepParameter
            {
                Name = "Variable",
                Description = "Name of the variable (without $ prefix). Must start with letter or underscore, can contain letters, numbers, underscores. Dots are reserved for internal variables.",
                Type = StepParameter.ParamType.String,
                DefaultValue = new ParameterValue("myvar")
            });

            Parameters.Add(new StepParameter
            {
                Name = "Value",
                Description = "The text value to assign to the variable. Can contain references to other variables defined in previous steps.",
                Type = StepParameter.ParamType.MultilineString,
                DefaultValue = new ParameterValue("")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            string variableName = parameters[0].stringValue?.Trim();
            string variableValue = parameters[1].stringValue ?? "";

            // Validate variable name
            if (string.IsNullOrWhiteSpace(variableName))
            {
                Debug.LogError("Set Text Variable: Variable name cannot be empty.");
                await Task.Yield();
                return;
            }

            // Check for dots (reserved for internal variables)
            if (variableName.Contains("."))
            {
                Debug.LogError($"Set Text Variable: Variable name '{variableName}' cannot contain dots. Dots are reserved for internal variables (e.g., $Application.unityVersion).");
                await Task.Yield();
                return;
            }

            if (!VariableResolver.IsValidVariableName(variableName))
            {
                Debug.LogError($"Set Text Variable: Invalid variable name '{variableName}'. Variable names must start with a letter or underscore and can only contain letters, numbers, and underscores.");
                await Task.Yield();
                return;
            }

            // Note: The actual storage of the variable is handled by UserActionRunner
            // This step just validates the input. The runner will extract and store the variable.

            if (AI.Config.LogCustomActions)
            {
                Debug.Log($"Set Text Variable: ${variableName} = \"{variableValue}\"");
            }

            await Task.Yield();
        }

        /// <summary>
        /// Extracts the variable name and value from the parameters.
        /// Called by UserActionRunner to store the variable.
        /// </summary>
        public static bool TryExtractVariable(List<ParameterValue> parameters, out string name, out string value)
        {
            name = null;
            value = null;

            if (parameters == null || parameters.Count < 2) return false;

            name = parameters[0].stringValue?.Trim();
            value = parameters[1].stringValue ?? "";

            if (string.IsNullOrWhiteSpace(name)) return false;

            // Reject variable names with dots (reserved for internal variables)
            if (name.Contains(".")) return false;

            if (!VariableResolver.IsValidVariableName(name)) return false;

            return true;
        }
    }
}