using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class SubPackageRenderPipelineValidator : Validator
    {
        public SubPackageRenderPipelineValidator()
        {
            Type = ValidatorType.DB;
            Name = "Sub-Package Render Pipeline Compatibility";
            Description = "Shows sub-packages with incorrect render pipeline compatibility settings based on their names.";
            FixCaption = "Fix";
        }

        public override async Task Validate()
        {
            CurrentState = State.Scanning;

            await Task.Yield();

            // Load all sub-packages (ParentId > 0)
            List<AssetInfo> subPackages = AI.LoadAssets()
                .Where(a => a.ParentId > 0)
                .ToList();

            DBIssues.Clear();

            foreach (AssetInfo asset in subPackages)
            {
                if (CancellationRequested) break;

                // Calculate what the correct values should be based on heuristics
                bool shouldBeBIRP = AssetUtils.ShouldBeBIRPCompatible(asset.SafeName);
                bool shouldBeURP = AssetUtils.ShouldBeURPCompatible(asset.SafeName);
                bool shouldBeHDRP = AssetUtils.ShouldBeHDRPCompatible(asset.SafeName);

                // Check if current values differ from what they should be
                if (asset.BIRPCompatible != shouldBeBIRP
                    || asset.URPCompatible != shouldBeURP
                    || asset.HDRPCompatible != shouldBeHDRP)
                {
                    DBIssues.Add(new AssetInfo(asset));
                }
            }

            CurrentState = State.Completed;
        }

        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            foreach (AssetInfo issue in DBIssues)
            {
                if (CancellationRequested) break;

                // Reload the asset from DB to get latest version
                Asset asset = DBAdapter.DB.Find<Asset>(issue.Id);
                if (asset == null) continue;

                // Apply the heuristic pipeline compatibility logic
                UnityPackageImporter.SetHeuristicPipelineCompatibility(asset);

                // Persist the changes
                DBAdapter.DB.Update(asset);
            }

            AI.TriggerPackageRefresh();

            await Validate();
        }
    }
}