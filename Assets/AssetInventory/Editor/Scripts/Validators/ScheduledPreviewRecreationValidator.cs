using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class ScheduledPreviewRecreationValidator : Validator
    {
        public ScheduledPreviewRecreationValidator()
        {
            Type = ValidatorType.DB;
            Speed = ValidatorSpeed.Fast;
            Name = "Scheduled Preview Recreation";
            Description = "Checks if there are any preview images scheduled to be recreated.";
            Fixable = true;
            FixCaption = "Open Previews Wizard";
        }

        public override async Task Validate()
        {
            CurrentState = State.Scanning;
            string query = "select * from AssetFile where PreviewState = ? or PreviewState = ?";
            DBIssues = DBAdapter.DB.Query<AssetInfo>(query, AssetFile.PreviewOptions.Redo, AssetFile.PreviewOptions.RedoMissing).ToList();
            CurrentState = State.Completed;
            await Task.Yield();
        }

        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            PreviewWizardUI previewsUI = PreviewWizardUI.ShowWindow();
            previewsUI.Init();

            await Task.Yield();
            CurrentState = State.Idle;
        }
    }
}