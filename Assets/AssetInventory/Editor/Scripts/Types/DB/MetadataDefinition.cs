using System;
using SQLite;

namespace AssetInventory
{
    [Serializable]
    public sealed class MetadataDefinition
    {
        public enum DataType
        {
            Text = 0,
            BigText = 1,
            Boolean = 2,
            Number = 3,
            DecimalNumber = 4,
            Url = 5,
            Date = 6,
            DateTime = 7,
            SingleSelect = 8
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] [Collation("NOCASE")] public string Name { get; set; }
        public DataType Type { get; set; }
        public string ValueList { get; set; }
        public bool RestrictAssetGroup { get; set; }
        public AI.AssetGroup ApplicableGroup { get; set; }
        public bool RestrictAssetSource { get; set; }
        public Asset.Source ApplicableSource { get; set; }

        public MetadataDefinition()
        {
        }

        public MetadataDefinition(string name)
        {
            Name = name;
        }
        public override string ToString()
        {
            return $"Metadata Definition '{Name}' ({Type})";
        }
    }
}
