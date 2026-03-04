using System;

namespace AssetInventory
{
    [Serializable]
    public sealed class AssetUpdateResult
    {
        public AssetUpdateResultDetails result;

        public override string ToString()
        {
            return "Asset Update Result";
        }
    }
}