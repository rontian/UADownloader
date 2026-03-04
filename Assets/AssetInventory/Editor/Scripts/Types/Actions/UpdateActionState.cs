using System;

namespace AssetInventory
{
    [Serializable]
    public sealed class UpdateActionState
    {
        public string key;
        public bool enabled = true;

        public UpdateActionState()
        {
        }

        public override string ToString()
        {
            return $"Update Action State '{key}' ({enabled})";
        }
    }
}