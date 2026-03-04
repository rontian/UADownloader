using System;
using SQLite;

namespace AssetInventory
{
    [Serializable]
    public class SavedSearch
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }

        public string SearchPhrase { get; set; }
        public string Type { get; set; }
        public int PackageTypes { get; set; }
        public int PackageSrPs { get; set; }
        public int ImageType { get; set; }
        public string Package { get; set; }
        public string PackageTag { get; set; }
        public string FileTag { get; set; }
        public string Publisher { get; set; }
        public string Category { get; set; }
        public string Width { get; set; }
        public string Height { get; set; }
        public string Length { get; set; }
        public string Size { get; set; }
        public bool CheckMaxWidth { get; set; }
        public bool CheckMaxHeight { get; set; }
        public bool CheckMaxLength { get; set; }
        public bool CheckMaxSize { get; set; }
        public int ColorOption { get; set; }
        public string SearchColor { get; set; }
        public string VariableDefinitions { get; set; }
    }
}