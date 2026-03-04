using System;
using System.Collections.Generic;

namespace AssetInventory
{
    [Serializable]
    public sealed class AssetUpdateResultDetails
    {
        public List<AssetUpdate> results;

        public override string ToString()
        {
            return "Asset Update Result Details";
        }
    }
}