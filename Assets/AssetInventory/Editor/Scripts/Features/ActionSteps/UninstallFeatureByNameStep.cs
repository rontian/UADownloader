using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.PackageManager.Requests;

namespace AssetInventory
{
    [Serializable]
    public sealed class UninstallFeatureByNameStep : ActionStep
    {
        private RemoveRequest _request;

        public UninstallFeatureByNameStep()
        {
            Key = "UninstallFeatureByName";
            Name = "Uninstall Feature By Name";
            Description = "Uninstall a Unity feature package collection by name.";
            Category = ActionCategory.Importing;
            Parameters.Add(new StepParameter
            {
                Name = "Name",
                Description = "Id of the feature (the part after com.unity.feature), e.g. cinematic",
                DefaultValue = new ParameterValue("cinematic")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            string featureId = parameters[0].stringValue;
            if (!featureId.StartsWith("com.unity.feature."))
            {
                featureId = "com.unity.feature." + featureId;
            }
            List<AssetInfo> assets = AI.LoadAssets();
            AssetInfo info = assets.FirstOrDefault(a => a.AssetSource == Asset.Source.RegistryPackage && a.SafeName == featureId);
            if (info == null) return;

            List<AssetInfo> installed = info.GetInstalledFeaturePackageContent(assets);
            if (installed.Count == 0) return;

            bool finished = false;
            RemovalUI removalUI = RemovalUI.ShowWindow();
            removalUI.Init(installed, () => finished = true);

            while (!finished)
            {
                await Task.Yield();
            }
        }
    }
}