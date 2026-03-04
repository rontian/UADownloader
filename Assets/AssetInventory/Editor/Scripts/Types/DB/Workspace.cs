using System;
using System.Collections.Generic;
using SQLite;

namespace AssetInventory
{
    [Serializable]
    public sealed class Workspace
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] [Collation("NOCASE")] public string Name { get; set; }

        // runtime
        [Ignore] public List<WorkspaceSearch> Searches { get; set; }

        public Workspace()
        {
        }

        public Workspace(string name)
        {
            Name = name;
        }

        public List<WorkspaceSearch> LoadSearches()
        {
            Searches = DBAdapter.DB.Query<WorkspaceSearch>("SELECT * FROM WorkspaceSearch WHERE WorkspaceId = ? order by OrderIdx", Id);
            return Searches;
        }

        public override string ToString()
        {
            return $"Workspace '{Name}'";
        }
    }
}