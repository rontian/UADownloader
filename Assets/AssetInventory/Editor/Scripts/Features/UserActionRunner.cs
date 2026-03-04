using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class UserActionRunner : ActionProgress
    {
        // Dictionary to store variables defined during action execution
        private Dictionary<string, string> _variables = new Dictionary<string, string>();
        private bool _hadFailures = false;

        public async Task Run(CustomAction ca)
        {
            // Get steps for this action
            List<CustomActionStep> steps = DBAdapter.DB.Query<CustomActionStep>("SELECT * FROM CustomActionStep WHERE ActionID = ? order by OrderIdx", ca.Id);

            // Initialize variables dictionary
            _variables = new Dictionary<string, string>();
            _hadFailures = false;

            // Check if we're resuming after a recompilation
            int lastExecutedStepIndex = EditorPrefs.GetInt(ActionHandler.AI_CURRENT_STEP + ca.Id, -1);
            bool isResuming = EditorPrefs.GetBool(ActionHandler.AI_ACTION_ACTIVE + ca.Id, false);

            if (isResuming)
            {
                if (AI.Config.LogCustomActions) Debug.Log($"Resuming custom action '{ca.Name}' after recompilation from step {lastExecutedStepIndex + 1}");

                // Restore variables from previous execution
                RestoreVariables(ca.Id, lastExecutedStepIndex, steps);
            }
            else
            {
                // mark that we're starting execution of this action
                EditorPrefs.SetBool(ActionHandler.AI_ACTION_ACTIVE + ca.Id, true);
                EditorPrefs.SetInt(ActionHandler.AI_CURRENT_STEP + ca.Id, -1);
                if (AI.Config.LogCustomActions) Debug.Log($"Starting fresh execution of custom action '{ca.Name}'");
            }

            MainCount = steps.Count;
            for (int i = 0; i < steps.Count; i++)
            {
                CustomActionStep step = steps[i];
                step.ResolveValues();
                if (step.StepDef == null)
                {
                    Debug.LogError($"Invalid action step definition. Step '{step.Key}' not found. Skipping.");
                    continue;
                }

                // skip steps that were already executed before recompilation
                if (isResuming && i <= lastExecutedStepIndex) continue;

                SetProgress(step.StepDef.Name, i + 1);
                if (AI.Config.LogCustomActions) Debug.Log($"Executing step {i + 1}/{steps.Count}: {step.StepDef.Name}");

                // validate parameters
                bool passed = true;
                for (int j = 0; j < step.StepDef.Parameters.Count; j++)
                {
                    StepParameter param = step.StepDef.Parameters[j];
                    if (param.Optional) continue;
                    if (
                        ((step.StepDef.GetParamType(param, step.Values) == StepParameter.ParamType.String || step.StepDef.GetParamType(param, step.Values) == StepParameter.ParamType.MultilineString) && string.IsNullOrWhiteSpace(step.Values[j].stringValue)) ||
                        (step.StepDef.GetParamType(param, step.Values) == StepParameter.ParamType.Int && step.Values[j].intValue == 0)
                    )
                    {
                        Debug.LogError($"Action step '{step.StepDef.Name}' is missing parameter '{param.Name}'.");
                        passed = false;
                    }
                }
                if (!passed) continue;

                // execute
                try
                {
                    // Mark this step as executed
                    EditorPrefs.SetInt(ActionHandler.AI_CURRENT_STEP + ca.Id, i);

                    // Handle variable definition if this is a SetTextVariableStep
                    if (step.StepDef is SetTextVariableStep)
                    {
                        if (SetTextVariableStep.TryExtractVariable(step.Values, out string varName, out string varValue))
                        {
                            // Resolve any variables in the value itself
                            varValue = VariableResolver.ReplaceVariables(varValue, _variables);

                            // Store the variable
                            _variables[varName] = varValue;

                            if (AI.Config.LogCustomActions)
                            {
                                Debug.Log($"Variable defined: ${varName} = \"{varValue}\"");
                            }
                        }
                    }

                    // Create a copy of values with variables resolved
                    List<ParameterValue> resolvedValues = ResolveParameterVariables(step.Values, step.StepDef.Parameters, step.StepDef, _variables);

                    await step.StepDef.Run(resolvedValues);
                    AssetDatabase.Refresh();

                    // wait for all processes to finish
                    while (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        await Task.Delay(25);
                    }
                    EditorPrefs.DeleteKey(ActionHandler.AI_ACTION_LOCK); // clear lock in case it was set by a step

                    if (AI.Config.LogCustomActions) Debug.Log($"Step {i + 1}/{steps.Count} completed successfully");
                    if (step.StepDef.InterruptsExecution) return;
                }
                catch (Exception e)
                {
                    _hadFailures = true;
                    if (ca.StopOnFailure)
                    {
                        Debug.LogError($"Error executing step '{step.StepDef.Name}': {e.Message}. Action execution stopped.");
                        Debug.LogException(e);
                        break; // Stop execution
                    }
                    else
                    {
                        Debug.LogWarning($"Error executing step '{step.StepDef.Name}': {e.Message}. Continuing with next step.");
                        Debug.LogException(e);
                        // Continue with next step
                    }
                }
            }

            // Log completion status
            if (_hadFailures)
            {
                if (ca.StopOnFailure)
                {
                    if (AI.Config.LogCustomActions) Debug.Log($"Custom action '{ca.Name}' stopped due to step failure");
                }
                else
                {
                    if (AI.Config.LogCustomActions) Debug.LogWarning($"Custom action '{ca.Name}' completed with failures");
                }
            }
            else
            {
                if (AI.Config.LogCustomActions) Debug.Log($"Custom action '{ca.Name}' completed successfully");
            }

            // clear execution state when done (either completed or failed)
            EditorPrefs.DeleteKey(ActionHandler.AI_ACTION_ACTIVE + ca.Id);
            EditorPrefs.DeleteKey(ActionHandler.AI_CURRENT_STEP + ca.Id);
        }

        /// <summary>
        /// Resolves variables in parameter values.
        /// </summary>
        public static List<ParameterValue> ResolveParameterVariables(List<ParameterValue> values, List<StepParameter> parameters, ActionStep stepDef, Dictionary<string, string> variables)
        {
            List<ParameterValue> resolved = new List<ParameterValue>();

            for (int i = 0; i < values.Count; i++)
            {
                ParameterValue newValue = new ParameterValue(values[i]);

                // Only resolve variables in string parameters
                if (i < parameters.Count)
                {
                    StepParameter param = parameters[i];
                    StepParameter.ParamType paramType = param.Type;

                    // Handle dynamic types by getting the actual type
                    if (paramType == StepParameter.ParamType.Dynamic)
                    {
                        paramType = stepDef.GetParamType(param, values);
                    }

                    if (paramType == StepParameter.ParamType.String || paramType == StepParameter.ParamType.MultilineString)
                    {
                        if (!string.IsNullOrEmpty(newValue.stringValue))
                        {
                            string resolvedValue = VariableResolver.ReplaceVariables(newValue.stringValue, variables);

                            // Check if there are any unresolved variables left after replacement
                            if (VariableResolver.ContainsVariables(resolvedValue))
                            {
                                List<string> unresolvedVars = VariableResolver.FindVariableReferences(resolvedValue);
                                throw new Exception($"Parameter '{parameters[i].Name}' contains unresolved variables: {string.Join(", ", unresolvedVars.Select(v => "$" + v))}");
                            }

                            newValue.stringValue = resolvedValue;
                        }
                    }
                }

                resolved.Add(newValue);
            }

            return resolved;
        }

        /// <summary>
        /// Restores variables by re-executing variable definitions from completed steps.
        /// This is called after recompilation to rebuild the variable state.
        /// </summary>
        private void RestoreVariables(int actionId, int lastExecutedStepIndex, List<CustomActionStep> steps)
        {
            // Re-execute variable definitions from steps that were completed
            for (int i = 0; i <= lastExecutedStepIndex && i < steps.Count; i++)
            {
                CustomActionStep step = steps[i];
                step.ResolveValues();

                if (step.StepDef is SetTextVariableStep)
                {
                    if (SetTextVariableStep.TryExtractVariable(step.Values, out string varName, out string varValue))
                    {
                        // Resolve variables in the value (in case it references other variables)
                        varValue = VariableResolver.ReplaceVariables(varValue, _variables);
                        _variables[varName] = varValue;

                        if (AI.Config.LogCustomActions)
                        {
                            Debug.Log($"Variable restored: ${varName} = \"{varValue}\"");
                        }
                    }
                }
            }
        }
    }
}