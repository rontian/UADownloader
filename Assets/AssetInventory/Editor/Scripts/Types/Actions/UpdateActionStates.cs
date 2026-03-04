using System;
using System.Collections.Generic;

namespace AssetInventory
{
    [Serializable]
    public sealed class UpdateActionStates
    {
        public string name;
        public List<UpdateActionState> actions = new List<UpdateActionState>();

        public UpdateActionStates()
        {
        }

        public override string ToString()
        {
            return $"Update Action States '{name}'";
        }
    }
}