using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory
{
    [Serializable]
    public sealed class UpdateAction
    {
        public enum Phase
        {
            Independent,
            Pre,
            Index,
            Post
        }

        public string key;
        public string name;
        public string description;
        public Phase phase = Phase.Independent;
        public bool supportsForce;
        public bool nonBlocking;
        public bool allowParallel;
        public bool hidden;

        // runtime
        public bool scheduled;
        public List<ActionProgress> progress = new List<ActionProgress>();

        public UpdateAction()
        {
        }

        public bool IsRunning()
        {
            return progress.Any(p => p.IsRunning());
        }

        public void MarkStarted()
        {
            scheduled = false;
        }

        public void CheckStopped()
        {
            if (progress.Any(p => p.IsRunning())) return;

            progress.Clear();
        }

        public override string ToString()
        {
            return $"Update Action '{name}' ({key})";
        }
    }
}