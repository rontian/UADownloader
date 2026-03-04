using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.PackageManager;

namespace AssetInventory
{
    [Serializable]
    public sealed class InstallRegistryPackageByPathStep : ActionStep
    {
        public InstallRegistryPackageByPathStep()
        {
            Key = "InstallRegistryPackageByPath";
            Name = "Install Package By Path";
            Description = "Install a registry package by reference to local file system.";
            Category = ActionCategory.Importing;
            Parameters.Add(new StepParameter
            {
                Name = "Path",
                Description = "Path of the package, e.g. 'C:/MyProject/Packages/com.companyname.packagename' or '../../Downloads/replica.tgz'"
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            AssetInfo info = new AssetInfo();
            info.Location = parameters[0].stringValue.Replace("\\", "/");
            info.AssetSource = Asset.Source.RegistryPackage;
            info.PackageSource = info.Location.ToLowerInvariant().Contains(".tgz") ? PackageSource.LocalTarball : PackageSource.Local;

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