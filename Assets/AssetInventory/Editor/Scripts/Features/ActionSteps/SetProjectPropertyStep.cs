using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif
using UnityEngine;

namespace AssetInventory
{
    [Serializable]
    public sealed class SetProjectPropertyStep : ActionStep
    {
        public enum InputHandlingMode
        {
            Old = 0,
            New = 1,
            Both = 2
        }

        public SetProjectPropertyStep()
        {
            Key = "SetProjectProperty";
            Name = "Set Project Property";
            Description = "Set the value of the project property.";
            Category = ActionCategory.Settings;
            Parameters.Add(new StepParameter
            {
                Name = "Property",
                Description = "Property to set.",
                ValueList = StepParameter.ValueType.Custom,
                Options = new List<Tuple<string, ParameterValue>>
                {
                    new Tuple<string, ParameterValue>("Company Name", new ParameterValue("CompanyName")),
                    new Tuple<string, ParameterValue>("Product Name", new ParameterValue("ProductName")),
                    new Tuple<string, ParameterValue>("Version", new ParameterValue("Version")),
                    new Tuple<string, ParameterValue>("Splash Screen", new ParameterValue("SplashScreen")),
                    new Tuple<string, ParameterValue>("Scripting Backend", new ParameterValue("ScriptingBackend")),
                    new Tuple<string, ParameterValue>("Incremental GC", new ParameterValue("IncrementalGC")),
                    new Tuple<string, ParameterValue>("Input Manager", new ParameterValue("InputManager")),
                    new Tuple<string, ParameterValue>("Enter Playmode Options", new ParameterValue("EnterPlaymodeOptions")),
                    new Tuple<string, ParameterValue>("Reload Domain", new ParameterValue("ReloadDomain")),
                    new Tuple<string, ParameterValue>("Reload Scene", new ParameterValue("ReloadScene"))
                }
            });
            Parameters.Add(new StepParameter
            {
                Name = "Value",
                Description = "New value for the property.",
                Type = StepParameter.ParamType.Dynamic
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            switch (parameters[0].stringValue)
            {
                case "CompanyName":
                    PlayerSettings.companyName = parameters[1].stringValue;
                    break;

                case "ProductName":
                    PlayerSettings.productName = parameters[1].stringValue;
                    break;

                case "Version":
                    PlayerSettings.bundleVersion = parameters[1].stringValue;
                    break;

                case "InputManager":
                    switch (parameters[1].stringValue)
                    {
                        case "Old":
                            SetInputHandling(InputHandlingMode.Old);
                            break;

                        case "New":
                            SetInputHandling(InputHandlingMode.New);
                            break;

                        case "Both":
                            SetInputHandling(InputHandlingMode.Both);
                            break;
                    }

                    break;

                case "IncrementalGC":
                    PlayerSettings.gcIncremental = parameters[1].boolValue;
                    break;

                case "SplashScreen":
                    PlayerSettings.SplashScreen.show = parameters[1].boolValue;
                    break;

                case "ScriptingBackend":
#if UNITY_2021_2_OR_NEWER
                    BuildTargetGroup selectedBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
                    if (selectedBuildTargetGroup != BuildTargetGroup.Unknown)
                    {
                        NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(selectedBuildTargetGroup);
                        if (namedBuildTarget != NamedBuildTarget.Unknown)
                        {
                            PlayerSettings.SetScriptingBackend(namedBuildTarget, parameters[1].stringValue == "Mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
                        }
                    }
#else
                    BuildTargetGroup currentGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
                    PlayerSettings.SetScriptingBackend(currentGroup, parameters[1].stringValue == "Mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
#endif
                    break;

                case "EnterPlaymodeOptions":
                    EditorSettings.enterPlayModeOptionsEnabled = parameters[1].boolValue;
                    break;

                case "ReloadDomain":
                    SetReloadDomain(parameters[1].boolValue);
                    break;

                case "ReloadScene":
                    SetReloadScene(parameters[1].boolValue);
                    break;
            }
            AssetDatabase.SaveAssets();
            await Task.Yield();
        }

        public override StepParameter.ParamType GetParamType(StepParameter param, List<ParameterValue> parameters)
        {
            switch (parameters[0].stringValue)
            {
                case "EnterPlaymodeOptions":
                case "IncrementalGC":
                case "ReloadDomain":
                case "ReloadScene":
                case "SplashScreen":
                    return StepParameter.ParamType.Bool;
            }

            // default
            return StepParameter.ParamType.String;
        }

        public override StepParameter.ValueType GetParamValueList(StepParameter param, List<ParameterValue> parameters)
        {
            switch (parameters[0].stringValue)
            {
                case "InputManager":
                case "ScriptBackend":
                    return StepParameter.ValueType.Custom;
            }

            // default
            return param.ValueList;
        }

        public override List<Tuple<string, ParameterValue>> GetParamOptions(StepParameter param, List<ParameterValue> parameters)
        {
            switch (parameters[0].stringValue)
            {
                case "ScriptingBackend":
                    return new List<Tuple<string, ParameterValue>>
                    {
                        new Tuple<string, ParameterValue>("Mono", new ParameterValue("Mono")),
                        new Tuple<string, ParameterValue>("IL2CPP", new ParameterValue("IL2CPP"))
                    };

                case "InputManager":
                    return new List<Tuple<string, ParameterValue>>
                    {
                        new Tuple<string, ParameterValue>("Old", new ParameterValue("Old")),
                        new Tuple<string, ParameterValue>("New", new ParameterValue("New")),
                        new Tuple<string, ParameterValue>("Both", new ParameterValue("Both"))
                    };
            }

            // default
            return param.Options;
        }

        public static void SetInputHandling(InputHandlingMode mode)
        {
            const string PROJECT_SETTINGS_PATH = "ProjectSettings/ProjectSettings.asset";

            AssetDatabase.SaveAssets(); // flush all changes to disk
            if (!File.Exists(PROJECT_SETTINGS_PATH))
            {
                throw new FileNotFoundException("ProjectSettings.asset not found!");
            }

            string[] lines = File.ReadAllLines(PROJECT_SETTINGS_PATH);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("activeInputHandler:")) // Locate input setting
                {
                    lines[i] = $"  activeInputHandler: {(int)mode}";
                    break;
                }
            }

            File.WriteAllLines(PROJECT_SETTINGS_PATH, lines);
            AssetDatabase.Refresh();
        }

        private static void SetReloadDomain(bool enable)
        {
            EnterPlayModeOptions options = EditorSettings.enterPlayModeOptions;

            if (!enable)
            {
                options |= EnterPlayModeOptions.DisableDomainReload;
            }
            else
            {
                options &= ~EnterPlayModeOptions.DisableDomainReload;
            }

            EditorSettings.enterPlayModeOptions = options;
        }

        private static void SetReloadScene(bool enable)
        {
            EnterPlayModeOptions options = EditorSettings.enterPlayModeOptions;

            if (!enable)
            {
                options |= EnterPlayModeOptions.DisableSceneReload;
            }
            else
            {
                options &= ~EnterPlayModeOptions.DisableSceneReload;
            }

            EditorSettings.enterPlayModeOptions = options;
        }
    }
}
