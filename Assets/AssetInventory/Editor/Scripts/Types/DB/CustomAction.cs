using System;
using SQLite;

namespace AssetInventory
{
    [Serializable]
    public sealed class CustomAction
    {
        public enum Mode
        {
            Manual = 0,
            AtInstallation = 1
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] [Collation("NOCASE")] public string Name { get; set; }
        public string Description { get; set; }
        public bool StopOnFailure { get; set; } = true;
        public Mode RunMode { get; set; }

        public CustomAction()
        {
        }

        public CustomAction(string name) : this()
        {
            Name = name;
        }

        public override string ToString()
        {
            return $"Custom Action '{Name}'";
        }
    }
}