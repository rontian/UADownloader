using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    public sealed class FreeAssetFinder : AssetImporter
    {
        private static readonly Regex pattern = new Regex(@"assetstore\.unity\.com/packages.*-(\d+)", RegexOptions.Compiled);

        public async Task<List<AssetDetails>> Run(bool force = false)
        {
            List<AssetDetails> result = new List<AssetDetails>();
            List<AssetInfo> candidates = AI.LoadAssets()
                .Where(info =>
                    info.AssetSource == Asset.Source.AssetStorePackage &&
                    info.ParentId <= 0 &&
                    info.PublisherId > 0 &&
                    info.PriceEur > 0
                )
                .OrderBy(info => info.PublisherId)
                .ToList();

            // gather candidates
            HashSet<int> results = new HashSet<int>();

            MainCount = candidates.Count;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (CancellationRequested) break;

                SetProgress($"Parsing {candidates[i].DisplayPublisher}", i + 1);

                // extract asset ids from descriptions
                MatchCollection matches = pattern.Matches(candidates[i].Description);
                foreach (Match m in matches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int item))
                    {
                        results.Add(item);
                    }
                }

                // check inside dependencies
                candidates[i].GetPackageDependencies()?.ForEach(dep =>
                {
                    results.Add(dep.id);
                });
            }

            // check if purchased already
            List<AssetInfo> assets = AI.LoadAssets().Where(a => a.AssetSource == Asset.Source.AssetStorePackage).ToList();
            List<int> ids = results.ToList();

            MainCount = ids.Count;
            for (int i = 0; i < ids.Count; i++)
            {
                if (CancellationRequested) break;

                SetProgress($"Checking ({ids[i]})", i + 1);

                if (assets.Any(a => a.ForeignId == ids[i])) continue;

                AssetDetails details = await AssetStore.RetrieveAssetDetails(ids[i]);
                if (details?.originPrice != null && details.originPrice != "0.00")
                {
                    result.Add(details);
                }
            }

            return result.OrderBy(a => a.name).ToList();
        }
    }
}