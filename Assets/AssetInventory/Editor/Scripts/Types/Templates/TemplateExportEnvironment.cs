namespace AssetInventory
{
    public class TemplateExportEnvironment
    {
        public string name = "Default";
        public string publishFolder;
        public string dataPath = "data/";
        public string imagePath = "Previews/";
        public bool excludeImages;
        public bool internalIdsOnly;

        public TemplateExportEnvironment()
        {
        }

        public TemplateExportEnvironment(string name)
        {
            this.name = name;
        }
    }
}