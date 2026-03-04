using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AssetInventory
{
    public sealed class ActionUI : BasicEditorUI
    {
        private CustomAction _action;
        private List<CustomActionStep> _steps;
        private Vector2 _scrollPos;
        private Action _onSave;

        public static ActionUI ShowWindow()
        {
            ActionUI window = GetWindow<ActionUI>("Action Wizard");
            window.minSize = new Vector2(690, 300);

            return window;
        }

        public void Init(CustomAction action, Action onSave = null)
        {
            _action = action;
            _onSave = onSave;

            _steps = DBAdapter.DB.Query<CustomActionStep>("SELECT * FROM CustomActionStep WHERE ActionId = ? order by OrderIdx", _action.Id);
            ResolveStepDefs();

            _serializedStepsObject = null;
        }

        private void ResolveStepDefs()
        {
            foreach (CustomActionStep step in _steps)
            {
                step.ResolveValues();
            }
        }

        private sealed class StepsWrapper : ScriptableObject
        {
            public List<CustomActionStep> steps = new List<CustomActionStep>();
        }

        private ReorderableList StepsListControl
        {
            get
            {
                if (_stepsListControl == null) InitStepsControl();
                return _stepsListControl;
            }
        }
        private ReorderableList _stepsListControl;

        private SerializedObject SerializedStepsObject
        {
            get
            {
                // reference can become null on reload
                if (_serializedStepsObject == null || _serializedStepsObject.targetObjects.FirstOrDefault() == null) InitStepsControl();
                return _serializedStepsObject;
            }
        }
        private SerializedObject _serializedStepsObject;
        private SerializedProperty _stepsProperty;
        private int _selectedStepIndex = -1;

        private void InitStepsControl()
        {
            StepsWrapper obj = CreateInstance<StepsWrapper>();
            obj.steps = _steps;

            _serializedStepsObject = new SerializedObject(obj);
            _stepsProperty = _serializedStepsObject.FindProperty("steps");
            _stepsListControl = new ReorderableList(_serializedStepsObject, _stepsProperty, true, true, true, true);
            _stepsListControl.drawElementCallback = DrawStepsListItem;
            _stepsListControl.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Steps to Execute");
            _stepsListControl.onAddCallback = OnAddStep;
            _stepsListControl.onRemoveCallback = OnRemoveStep;
            _stepsListControl.onReorderCallbackWithDetails = OnReorderCallbackWithDetails;
        }

        private void OnReorderCallbackWithDetails(ReorderableList list, int oldIndex, int newIndex)
        {
            // move step from old to new position since list does for some reason not persist this
            // newIndex is already adjusted with removal in mind
            CustomActionStep item = _steps[oldIndex];
            _steps.RemoveAt(oldIndex);
            _steps.Insert(newIndex, item);
        }

        private void OnAddStep(ReorderableList list)
        {
            string curCategory = null;

            GenericMenu menu = new GenericMenu();
            foreach (ActionStep step in AI.Actions.ActionSteps)
            {
                if (curCategory != step.Category.ToString())
                {
                    curCategory = step.Category.ToString();
                    menu.AddDisabledItem(new GUIContent($"--- {StringUtils.CamelCaseToWords(curCategory)} ---"));
                }
                menu.AddItem(new GUIContent(step.Name), false, () => AddStep(step));
            }
            menu.ShowAsContext();
        }

        private void OnRemoveStep(ReorderableList list)
        {
            if (_selectedStepIndex < 0 || _selectedStepIndex >= _steps.Count) return;
            _steps.RemoveAt(_selectedStepIndex);
            _selectedStepIndex = -1;
        }

        private void DrawStepsListItem(Rect rect, int index, bool isActive, bool isFocused)
        {
            // draw alternating-row background
            if (Event.current.type == EventType.Repaint && index % 2 == 1)
            {
                // choose a tiny overlay that will darken/lighten regardless of the exact theme colors
                Color overlay = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f) // on dark (Pro) skin, brighten a hair
                    : new Color(0f, 0f, 0f, 0.025f); // on light skin, darken a hair

                EditorGUI.DrawRect(rect, overlay);
            }

            if (index >= _steps.Count) return;
            if (isFocused) _selectedStepIndex = index;
            if (!isFocused && _selectedStepIndex == index) _selectedStepIndex = -1;

            CustomActionStep step = _steps[index];
            if (step.StepDef == null || step.Values == null) step.ResolveValues();
            if (step.StepDef == null)
            {
                GUI.Label(new Rect(rect.x, rect.y + 2, rect.width, EditorGUIUtility.singleLineHeight),
                    $"Invalid step definition. Step '{step.Key}' not found.",
                    UIStyles.ColoredText(UIStyles.errorColor));
                return;
            }

            GUI.Label(new Rect(rect.x, rect.y + 2, 150, EditorGUIUtility.singleLineHeight),
                UIStyles.Content(step.StepDef.Name, step.StepDef.Description),
                UIStyles.entryStyle);

            int offset = 150;

            // Build dictionary of variables defined in previous steps for validation
            Dictionary<string, string> availableVariables = BuildAvailableVariables(index);

            for (int i = 0; i < step.StepDef.Parameters.Count; i++)
            {
                if (step.Values.Count <= i) step.Values.Add(new ParameterValue());

                StepParameter param = step.StepDef.Parameters[i];
                GUI.Label(new Rect(rect.x + offset, rect.y + 2, 53, EditorGUIUtility.singleLineHeight),
                    UIStyles.Content(param.Name + (param.Optional ? "*" : ""), param.Description),
                    UIStyles.miniLabelRight);

                StepParameter.ParamType finalType = param.Type;
                if (finalType == StepParameter.ParamType.Dynamic)
                {
                    finalType = step.StepDef.GetParamType(param, step.Values);
                }

                List<Tuple<string, ParameterValue>> finalOptions = param.Options;
                if (param.Type == StepParameter.ParamType.Dynamic)
                {
                    finalOptions = step.StepDef.GetParamOptions(param, step.Values);
                }
                if (finalOptions != null)
                {
                    // render dropdown with values to select from, no custom values
                    int curIndex = 0;
                    if (finalType == StepParameter.ParamType.String || finalType == StepParameter.ParamType.MultilineString) curIndex = finalOptions.FindIndex(o => o.Item2.stringValue == step.Values[i].stringValue);
                    if (finalType == StepParameter.ParamType.Int) curIndex = finalOptions.FindIndex(o => o.Item2.intValue == step.Values[i].intValue);

                    int newIndex = EditorGUI.Popup(new Rect(rect.x + offset + 55, rect.y + 2, 180, EditorGUIUtility.singleLineHeight),
                        curIndex, finalOptions.Select(o => o.Item1.Replace("/", "\\")).ToArray());

                    if (newIndex != curIndex)
                    {
                        if (finalType == StepParameter.ParamType.String || finalType == StepParameter.ParamType.MultilineString) step.Values[i].stringValue = finalOptions[newIndex].Item2.stringValue;
                        if (finalType == StepParameter.ParamType.Int) step.Values[i].intValue = finalOptions[newIndex].Item2.intValue;
                    }
                }
                else
                {
                    switch (finalType)
                    {
                        case StepParameter.ParamType.String:
                            step.Values[i].stringValue = GUI.TextField(
                                new Rect(rect.x + offset + 55, rect.y + 2, 180, EditorGUIUtility.singleLineHeight),
                                step.Values[i].stringValue);
                            break;

                        case StepParameter.ParamType.MultilineString:
                            step.Values[i].stringValue = EditorGUI.TextArea(
                                new Rect(rect.x + offset + 55, rect.y + 2, 180, EditorGUIUtility.singleLineHeight),
                                step.Values[i].stringValue);
                            break;

                        case StepParameter.ParamType.Int:
                            step.Values[i].intValue = EditorGUI.IntField(
                                new Rect(rect.x + offset + 55, rect.y + 2, 180, EditorGUIUtility.singleLineHeight),
                                step.Values[i].intValue);
                            break;

                        case StepParameter.ParamType.Bool:
                            step.Values[i].boolValue = EditorGUI.Toggle(
                                new Rect(rect.x + offset + 55, rect.y + 2, 20, EditorGUIUtility.singleLineHeight),
                                step.Values[i].boolValue);
                            break;
                    }
                }

                // Display warning icon for undefined variables in string parameters
                if (finalType == StepParameter.ParamType.String || finalType == StepParameter.ParamType.MultilineString)
                {
                    string paramValue = step.Values[i].stringValue;
                    if (!string.IsNullOrEmpty(paramValue))
                    {
                        List<string> undefinedVars = VariableResolver.ValidateVariables(paramValue, availableVariables);
                        if (undefinedVars.Count > 0)
                        {
                            // Display warning icon with tooltip
                            Rect iconRect = new Rect(rect.x + offset + 240, rect.y + 2, 20, EditorGUIUtility.singleLineHeight);
                            string tooltip = "Undefined variables: " + string.Join(", ", undefinedVars);
                            GUIContent warningContent = new GUIContent(EditorGUIUtility.IconContent("console.warnicon").image, tooltip);

                            // Draw the icon with orange/yellow tint
                            Color originalColor = GUI.color;
                            GUI.color = new Color(1f, 0.7f, 0f, 1f); // Orange color
                            GUI.Label(iconRect, warningContent);
                            GUI.color = originalColor;
                        }
                    }
                }

                if (isFocused && GUI.Button(new Rect(rect.x + rect.width - 24, rect.y + 1, 24, 20), EditorGUIUtility.IconContent("d_PlayButton@2x", "|Run Step Now")))
                {
                    try
                    {
                        // Build variables dictionary from all previous steps
                        Dictionary<string, string> variables = BuildAvailableVariables(index);

                        // If this step is a SetTextVariableStep, resolve and add its variable to the dictionary
                        if (step.StepDef is SetTextVariableStep)
                        {
                            if (SetTextVariableStep.TryExtractVariable(step.Values, out string varName, out string varValue))
                            {
                                // Resolve any variables in the value itself
                                varValue = VariableResolver.ReplaceVariables(varValue ?? "", variables);
                                variables[varName] = varValue;
                            }
                        }

                        // Resolve variables in the step parameters
                        List<ParameterValue> resolvedValues = UserActionRunner.ResolveParameterVariables(
                            step.Values,
                            step.StepDef.Parameters,
                            step.StepDef,
                            variables);

                        // Run the step with resolved values
                        step.StepDef.Run(resolvedValues);
                        AssetDatabase.Refresh();
                    }
                    catch (Exception e)
                    {
                        EditorUtility.DisplayDialog("Error Running Step", $"Failed to run step: {e.Message}", "OK");
                    }
                }
                offset += 250;
            }
        }

        public override void OnGUI()
        {
            if (_action == null) return;

            _action.Name = EditorGUILayout.TextField("Name", _action.Name);
            _action.Description = EditorGUILayout.TextField("Description", _action.Description);
            _action.StopOnFailure = EditorGUILayout.Toggle(UIStyles.Content("Stop on Failure", "If enabled, the action will stop executing remaining steps when a step fails. If disabled, failed steps are logged but execution continues."), _action.StopOnFailure);
            _action.RunMode = (CustomAction.Mode)EditorGUILayout.EnumPopup("Run Mode", _action.RunMode, GUILayout.Width(300));

            EditorGUILayout.Space(15);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
            if (SerializedStepsObject != null)
            {
                SerializedStepsObject.Update();
                StepsListControl.DoLayoutList();
                SerializedStepsObject.ApplyModifiedProperties();
            }
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save & Close", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                Save();
                Close();
            }
            if (GUILayout.Button("Save & Run", GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                Save();
                _ = AI.Actions.RunUserAction(_action);
                AssetDatabase.Refresh();
            }
            GUILayout.EndHorizontal();
        }

        private void Save()
        {
            DBAdapter.DB.Update(_action);

            for (int i = 0; i < _steps.Count; i++)
            {
                CustomActionStep step = _steps[i];
                step.OrderIdx = i;

                // assemble parameters
                step.PersistValues();

                if (step.Id > 0)
                {
                    DBAdapter.DB.Update(step);
                }
                else
                {
                    DBAdapter.DB.Insert(step);
                }
            }

            // delete removed steps
            DBAdapter.DB.Execute("delete from CustomActionStep where ActionId=? and Id not in (" +
                string.Join(",", _steps.Select(s => s.Id)) + ")", _action.Id);

            AI.Actions.Init(true);

            _onSave?.Invoke();
        }

        private void AddStep(ActionStep step)
        {
            CustomActionStep newStep = new CustomActionStep
            {
                Key = step.Key,
                ActionId = _action.Id,
                OrderIdx = _steps.Count,
                Values = step.Parameters.Select(p => new ParameterValue(p.DefaultValue)).ToList()
            };

            if (_selectedStepIndex >= 0)
            {
                _steps.Insert(_selectedStepIndex + 1, newStep);
            }
            else
            {
                _steps.Add(newStep);
            }
        }

        /// <summary>
        /// Builds a dictionary of variables defined in previous steps (steps 0 to stepIndex-1).
        /// Used for validation to check if variable references are defined and for resolving variables during test runs.
        /// </summary>
        private Dictionary<string, string> BuildAvailableVariables(int stepIndex)
        {
            Dictionary<string, string> variables = new Dictionary<string, string>();

            // Scan all steps before the current one
            for (int i = 0; i < stepIndex && i < _steps.Count; i++)
            {
                CustomActionStep step = _steps[i];
                if (step.StepDef == null || step.Values == null) continue;

                // Check if this step defines a variable
                if (step.StepDef is SetTextVariableStep)
                {
                    if (SetTextVariableStep.TryExtractVariable(step.Values, out string varName, out string varValue))
                    {
                        // Resolve any variables in the value itself (in case it references other variables)
                        varValue = VariableResolver.ReplaceVariables(varValue ?? "", variables);
                        variables[varName] = varValue;
                    }
                }
            }

            return variables;
        }
    }
}