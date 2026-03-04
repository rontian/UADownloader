using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    public abstract class Validator
    {
        public enum ValidatorType
        {
            DB,
            FileSystem
        }

        public enum ValidatorSpeed
        {
            Fast,
            Slow
        }

        public enum State
        {
            Idle,
            Scanning,
            Completed,
            Fixing
        }

        public ValidatorType Type { get; protected set; }
        public ValidatorSpeed Speed { get; protected set; } = ValidatorSpeed.Fast;
        public string Name { get; protected set; }
        public string Description { get; protected set; }
        public bool Fixable { get; protected set; } = true;
        public string FixCaption { get; protected set; } = "Fix";
        public List<AssetInfo> DBIssues { get; set; } = new List<AssetInfo>();
        public List<string> FileIssues { get; set; } = new List<string>();

        // runtime properties
        public State CurrentState { get; protected set; }
        public bool CancellationRequested { get; set; }
        public int Progress { get; set; }
        public int MaxProgress { get; set; }
        protected int ProgressId { get; set; }
        public bool IsRunning => CurrentState == State.Scanning || CurrentState == State.Fixing;

        public int IssueCount => Type == ValidatorType.DB ? DBIssues.Count : FileIssues.Count;

        public virtual bool IsVisible() => true;
        public abstract Task Validate();
        public abstract Task Fix();
    }
}