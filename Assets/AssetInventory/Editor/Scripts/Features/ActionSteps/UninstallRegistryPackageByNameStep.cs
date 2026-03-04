using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace AssetInventory
{
    [Serializable]
    public sealed class UninstallRegistryPackageByNameStep : ActionStep
    {
        private RemoveRequest _request;

        public UninstallRegistryPackageByNameStep()
        {
            Key = "UninstallRegistryPackageByName";
            Name = "Uninstall Package By Name";
            Description = "Uninstall a registry package by name.";
            Category = ActionCategory.Importing;
            Parameters.Add(new StepParameter
            {
                Name = "Name",
                Description = "Name of the package, e.g. com.unity.packagename",
                DefaultValue = new ParameterValue("com.unity.")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            _request = Client.Remove(parameters[0].stringValue);

            await Task.Delay(2000); // wait for the process to start
            while (!_request.IsCompleted)
            {
                await Task.Yield();
            }
            
            // Check if the request failed
            if (_request.Status == StatusCode.Failure)
            {
                throw new Exception($"Failed to uninstall package '{parameters[0].stringValue}': {_request.Error?.message}");
            }
        }
    }
}