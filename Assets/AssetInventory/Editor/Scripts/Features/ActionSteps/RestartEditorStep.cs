using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetInventory
{
    [Serializable]
    public sealed class RestartEditorStep : ActionStep
    {
        public RestartEditorStep()
        {
            Key = "RestartEditor";
            Name = "Restart Editor";
            Description = "Restarts the Unity editor and reopens the current project to apply any outstanding settings.";
            InterruptsExecution = true;
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            EditorApplication.OpenProject(Directory.GetCurrentDirectory());
            await Task.Yield();
        }
    }
}