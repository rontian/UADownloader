using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;

namespace AssetInventory
{
    public static class TemplateUtils
    {
        /// <summary>
        /// Loads all available templates from the Templates folder.
        /// </summary>
        /// <param name="addSeparator">If true, adds an empty separator entry between regular and sample templates.</param>
        /// <returns>List of loaded templates.</returns>
        public static List<TemplateInfo> LoadTemplates(bool addSeparator = true)
        {
            List<TemplateInfo> templates = new List<TemplateInfo>();

            string templateFolder = GetTemplateRootFolder();
            if (string.IsNullOrEmpty(templateFolder)) return templates;

            IOUtils.GetFiles(templateFolder, new List<string> {"*.bytes"}, SearchOption.AllDirectories).ForEach(f =>
            {
                TemplateInfo ti = new TemplateInfo();
                ti.path = f;

                // check for existing descriptor, otherwise create on the fly
                string descriptor = ti.GetDescriptorPath();
                if (File.Exists(descriptor))
                {
                    ti = JsonConvert.DeserializeObject<TemplateInfo>(File.ReadAllText(descriptor));
                    ti.path = f;
                    ti.hasDescriptor = true;
                }
                else
                {
                    ti.date = File.GetCreationTime(f);
                }
                if (string.IsNullOrWhiteSpace(ti.name)) ti.name = StringUtils.CamelCaseToWords(ti.GetNameFromFile(f));

                templates.Add(ti);
            });
            templates = templates.OrderBy(t => t.isSample).ThenBy(t => t.name, StringComparer.InvariantCultureIgnoreCase).ToList();

            // Add separator for sample templates if needed
            if (addSeparator)
            {
                int idx = templates.FindIndex(t => t.isSample);
                if (idx > 0)
                {
                    TemplateInfo tmpTi = new TemplateInfo();
                    tmpTi.name = "";
                    templates.Insert(idx, tmpTi);
                }
            }

            return templates;
        }

        /// <summary>
        /// Gets the root folder where templates are stored.
        /// </summary>
        /// <returns>Path to the template root folder.</returns>
        public static string GetTemplateRootFolder()
        {
            return AssetDatabase.FindAssets("t:Folder", new[] {"Assets", "Packages"})
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.Replace("\\", "/").ToLowerInvariant().EndsWith("inventory/editor/templates"));
        }
    }
}

