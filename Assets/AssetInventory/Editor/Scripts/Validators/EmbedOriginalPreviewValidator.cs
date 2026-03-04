using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class EmbedOriginalPreviewValidator : Validator
    {
        public EmbedOriginalPreviewValidator()
        {
            Type = ValidatorType.DB;
            Speed = ValidatorSpeed.Fast;
            Name = "Missing Preview Images in Cache";
            Description = "Checks if there are previews that are missing in the preview cache due to changed settings that all previews should be cached instead of using the originals directly.";
        }

        public override bool IsVisible()
        {
            return !AI.Config.directMediaPreviews;
        }

        public override async Task Validate()
        {
            CurrentState = State.Scanning;
            DBIssues = await CheckPreviews();
            CurrentState = State.Completed;
        }

        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            string previewFolder = AI.GetPreviewFolder();
            string query = "update AssetFile set PreviewState = ? where Id = ?";
            foreach (AssetInfo info in DBIssues)
            {
                if (CancellationRequested) break;

                info.PreviewState = AssetFile.PreviewOptions.Custom;
                await IOUtils.TryCopyFile(info.SourcePath, info.GetPreviewFile(previewFolder), true);
                DBAdapter.DB.Execute(query, info.PreviewState, info.Id);
            }
            await Task.Yield();

            CurrentState = State.Idle;
        }

        private async Task<List<AssetInfo>> CheckPreviews()
        {
            // check which files need to be copied
            string query = "select *, AssetFile.Id as Id from AssetFile left join Asset on Asset.Id = AssetFile.AssetId where PreviewState = ?";
            List<AssetInfo> result = DBAdapter.DB.Query<AssetInfo>(query, AssetFile.PreviewOptions.UseOriginal).ToList();
            await Task.Yield();

            return result;
        }
    }
}