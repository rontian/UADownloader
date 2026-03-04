using System.Collections.Generic;

namespace AssetInventory
{
    public class TemplateExportSettings
    {
        public List<TemplateExportEnvironment> environments = new List<TemplateExportEnvironment>();
        public int environmentIndex;

        // dev settings
        public bool devMode;
        public string devFolder;
        public string testFolder;
        public bool preserveJson;
        public int maxDetailPages = 0;
        public bool publishResult = true;
        public bool revealResult = true;
    }
}