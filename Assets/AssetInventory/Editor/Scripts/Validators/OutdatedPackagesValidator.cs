using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class OutdatedPackagesValidator : Validator
    {
        public OutdatedPackagesValidator()
        {
            Type = ValidatorType.DB;
            Name = "Outdated Packages";
            Description = "Shows packages from the Asset Store where metadata was changed by the author (e.g. name, category). This results in a new storage location in the cache. The item in the old location is not automatically removed by Unity, leading to duplicated entries. Usually these can just be deleted.";
            FixCaption = "Delete";
        }

        public override async Task Validate()
        {
            CurrentState = State.Scanning;

            await Task.Yield();

            DBIssues = AI.LoadAssets()
                .Where(a => a.AssetSource == Asset.Source.AssetStorePackage &&
                        a.CurrentSubState == Asset.SubState.Outdated &&
                        !a.IsAbandoned // cannot be redownloaded in case of error
                )
                .ToList();

            CurrentState = State.Completed;
        }

        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            foreach (AssetInfo issue in DBIssues)
            {
                if (CancellationRequested) break;
                AI.RemovePackage(issue, true);
                await Task.Yield();
            }

            AI.TriggerPackageRefresh();

            await Validate();
        }
    }
}