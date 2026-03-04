using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class UseOriginalPreviewValidator : Validator
    {
        public UseOriginalPreviewValidator()
        {
            Type = ValidatorType.DB;
            Speed = ValidatorSpeed.Fast;
            Name = "Unneeded Preview Images";
            Description = "Checks if there are previews that can be removed in case the original image files come from a media folder and is smaller than the requested preview size. This saves disk space.";
        }

        public override bool IsVisible()
        {
            return AI.Config.directMediaPreviews;
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

                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.UseOriginal, info.Id);

                // double-check, not to accidentally remove original files
                if (info.PreviewState != AssetFile.PreviewOptions.UseOriginal)
                {
                    IOUtils.TryDeleteFile(info.GetPreviewFile(previewFolder));
                }
            }
            await Task.Yield();

            CurrentState = State.Idle;
        }

        private async Task<List<AssetInfo>> CheckPreviews()
        {
            // check if original file can be used instead of copied previews
            int previewSize = AI.Config.upscaleSize;
            string typeStr = "'png','jpg','jpeg'";
            string query = "select *, AssetFile.Id as Id from AssetFile left join Asset on Asset.Id = AssetFile.AssetId where Asset.AssetSource = ? and (PreviewState = ? or PreviewState = ?) and Type in (" + typeStr + ") and Width > 0 and Height > 0 and (Width <= ? or Height <= ?)";
            List<AssetInfo> result = DBAdapter.DB.Query<AssetInfo>(query, Asset.Source.Directory, AssetFile.PreviewOptions.Provided, AssetFile.PreviewOptions.Custom, previewSize, previewSize).ToList();
            await Task.Yield();

            return result;
        }
    }
}