using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_2021_2_OR_NEWER
using System.IO;
#endif
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    [Serializable]
    public sealed class HTMLExportStep : ActionStep
    {
        public HTMLExportStep()
        {
            Key = "HTMLExport";
            Name = "HTML Export";
            Description = "Export the full database to HTML using a template.";
            Category = ActionCategory.Actions;

            // Load available templates
            List<TemplateInfo> templates = TemplateUtils.LoadTemplates();

            // Template parameter
            List<Tuple<string, ParameterValue>> templateOptions = new List<Tuple<string, ParameterValue>>();
            foreach (TemplateInfo template in templates)
            {
                if (!string.IsNullOrWhiteSpace(template.name))
                {
                    string templateId = template.GetNameFromFile();
                    templateOptions.Add(new Tuple<string, ParameterValue>(template.name, new ParameterValue(templateId)));
                }
            }

            // Add a default option if no templates are available
            if (templateOptions.Count == 0)
            {
                templateOptions.Add(new Tuple<string, ParameterValue>("No templates available", new ParameterValue("")));
            }

            Parameters.Add(new StepParameter
            {
                Name = "Template",
                Description = "Template to use for the HTML export.",
                Type = StepParameter.ParamType.String,
                ValueList = StepParameter.ValueType.Custom,
                Options = templateOptions,
                DefaultValue = templateOptions[0].Item2
            });

            // Target folder parameter
            Parameters.Add(new StepParameter
            {
                Name = "Target",
                Description = "Folder where the HTML export will be saved.",
                Type = StepParameter.ParamType.String,
                ValueList = StepParameter.ValueType.Folder,
                DefaultValue = new ParameterValue(AI.GetStorageFolder())
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
#if UNITY_2021_2_OR_NEWER
            // Get parameters
            string templateId = parameters[0].stringValue;
            string targetFolder = parameters[1].stringValue;

            // Load templates fresh for the run (don't rely on constructor field)
            List<TemplateInfo> templates = TemplateUtils.LoadTemplates();

            // Check if templates are available
            if (string.IsNullOrEmpty(templateId))
            {
                throw new Exception("No templates available for HTML export. Please ensure templates are present in the Templates folder.");
            }

            // Find template by filename
            TemplateInfo selectedTemplate = templates.FirstOrDefault(t => t.GetNameFromFile() == templateId);
            
            if (selectedTemplate == null)
            {
                throw new Exception($"Template with ID '{templateId}' not found. Please reconfigure this action step.");
            }

            // Skip empty separator entries
            if (string.IsNullOrWhiteSpace(selectedTemplate.name))
            {
                throw new Exception("Cannot export with an empty template name.");
            }

            // Create target folder if it doesn't exist
            Directory.CreateDirectory(targetFolder);

            // Load all assets from the database
            List<AssetInfo> assets = AI.LoadAssets()
                .Where(info => !info.Exclude)
                .Where(info => info.ParentId <= 0)
                .Where(info => info.AssetSource != Asset.Source.RegistryPackage)
                .ToList();

            // Create export environment
            TemplateExportEnvironment env = new TemplateExportEnvironment
            {
                name = "Action Export",
                publishFolder = Path.GetFullPath(targetFolder),
                dataPath = "data/",
                imagePath = "Previews/",
                excludeImages = false,
                internalIdsOnly = false
            };

            // Get or create template export settings
            if (AI.Config.templateExportSettings == null)
            {
                AI.Config.templateExportSettings = new TemplateExportSettings();
            }

            if (AI.Config.templateExportSettings.environments == null || AI.Config.templateExportSettings.environments.Count == 0)
            {
                AI.Config.templateExportSettings.environments = new List<TemplateExportEnvironment> {env};
            }

            TemplateExport exporter = new TemplateExport();
            await exporter.Run(
                assets,
                selectedTemplate,
                templates,
                AI.Config.templateExportSettings,
                env
            );
#else
            Debug.LogError("The HTML export step requires Unity 2021.2 or newer.");
            await Task.Yield();
#endif
        }
    }
}