using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public sealed class InstallRegistryPackageByNameStep : ActionStep
    {
        public InstallRegistryPackageByNameStep()
        {
            Key = "InstallRegistryPackageByName";
            Name = "Install Package By Name";
            Description = "Install a registry package by name.";
            Category = ActionCategory.Importing;
            Parameters.Add(new StepParameter
            {
                Name = "Name",
                Description = "Name of the package, e.g. com.unity.packagename",
                DefaultValue = new ParameterValue("com.unity.")
            });
            Parameters.Add(new StepParameter
            {
                Name = "Version",
                Description = "Optional version of the package. If empty Unity will select the recommended one.",
                Optional = true
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            AssetInfo info = new AssetInfo();
            info.SafeName = parameters[0].stringValue;
            info.AssetSource = Asset.Source.RegistryPackage;

            if (!string.IsNullOrWhiteSpace(parameters[1].stringValue))
            {
                info.ForceTargetVersion(parameters[1].stringValue);
            }

            bool finished = false;
            ImportUI importUI = ImportUI.ShowWindow();
            importUI.Init(new List<AssetInfo> {info}, true, () => finished = true, false, ActionHandler.AI_ACTION_LOCK);

            while (!finished)
            {
                await Task.Yield();
            }
        }
    }
}