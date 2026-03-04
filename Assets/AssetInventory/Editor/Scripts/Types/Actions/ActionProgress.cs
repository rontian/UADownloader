using System;

namespace AssetInventory
{
    public abstract class ActionProgress
    {
        public int ProgressId { get; set; }
        public string CurrentMain { get; protected set; }
        public int MainCount { get; protected set; }
        public int MainProgress { get; protected set; }
        public string CurrentSub { get; protected set; }
        public int SubCount { get; protected set; }
        public int SubProgress { get; protected set; }
        public bool CancellationRequested { get; set; } // TODO: convert to CancellationToken?
        public bool ReadOnly { get; protected set; }

        public DateTime StartedAt { get; set; }
        public double LastDuration { get; set; }

        public void WithProgress(string caption)
        {
            ProgressId = MetaProgress.Start(caption);
            CurrentMain = caption;
            StartedAt = DateTime.Now;
        }

        public void SetProgress(string caption, int progress)
        {
            CurrentMain = caption;
            MainProgress = progress;
            MetaProgress.Report(ProgressId, MainProgress, MainCount, CurrentMain);
        }

        public void RestartProgress(string caption)
        {
            if (ProgressId > 0) FinishProgress();
            ProgressId = MetaProgress.Start(caption);
            CurrentMain = caption;
        }

        public void FinishProgress()
        {
            if (ProgressId > 0)
            {
                MetaProgress.Remove(ProgressId);
                ProgressId = 0;
                LastDuration = (DateTime.Now - StartedAt).TotalSeconds;
            }
        }

        public void Cancel()
        {
            CancellationRequested = true;
        }

        public bool IsRunning()
        {
            return ProgressId > 0;
        }
    }
}