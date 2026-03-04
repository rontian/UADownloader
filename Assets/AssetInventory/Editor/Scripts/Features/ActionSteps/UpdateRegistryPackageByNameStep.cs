using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.PackageManager;

namespace AssetInventory
{
    [Serializable]
    public sealed class UpdateRegistryPackageByNameStep : ActionStep
    {
        public UpdateRegistryPackageByNameStep()
        {
            Key = "UpdateRegistryPackageByName";
            Name = "Update Package";
            Description = "Update a registry package to the newest recommended version or if set to the one defined by the update strategy. If no package name is specified, all packages with available updates will be updated.";
            Category = ActionCategory.Importing;
            Parameters.Add(new StepParameter
            {
                Name = "Name",
                Description = "Name of the package, e.g. com.unity.packagename. If left empty, all packages with available updates will be updated.",
                Optional = true
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            Dictionary<string, PackageInfo> packageCollection = AssetStore.GetProjectPackages();
            if (packageCollection == null) return;

            List<AssetInfo> updates = new List<AssetInfo>();
            List<AssetInfo> assets = AI.LoadAssets();
            int unmatchedCount = 0;
            foreach (PackageInfo packageInfo in packageCollection.Values)
            {
                if (packageInfo.source == PackageSource.BuiltIn) continue;
                if (!string.IsNullOrWhiteSpace(parameters[0].stringValue) && parameters[0].stringValue != packageInfo.name) continue;

                AssetInfo matchedAsset = assets.FirstOrDefault(info => info.SafeName == packageInfo.name);
                if (matchedAsset == null)
                {
                    matchedAsset = new AssetInfo();
                    matchedAsset.AssetSource = Asset.Source.RegistryPackage;
                    matchedAsset.SafeName = packageInfo.name;
                    matchedAsset.DisplayName = packageInfo.displayName;
                    matchedAsset.Version = packageInfo.version;
                    matchedAsset.Id = int.MaxValue - unmatchedCount;
                    matchedAsset.AssetId = int.MaxValue - unmatchedCount;
                    unmatchedCount++;
                }
                if (matchedAsset.IsUpdateAvailable()) updates.Add(matchedAsset);
            }
            if (updates.Count == 0) return;

            bool finished = false;
            ImportUI importUI = ImportUI.ShowWindow();
            importUI.Init(updates, true, () => finished = true, false, ActionHandler.AI_ACTION_LOCK);

            while (!finished)
            {
                await Task.Yield();
            }
        }
    }
}