#if UNITY_2021_2_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
#if !UNITY_2021_2_OR_NEWER
using Unity.SharpZipLib.Zip;
#endif
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class TemplateExport : ActionProgress
    {
        private static List<AssetInfo> _assets;

        public async Task Run(List<AssetInfo> assets, TemplateInfo template, List<TemplateInfo> templates, TemplateExportSettings settings, TemplateExportEnvironment env)
        {
            _assets = assets
                .OrderBy(a => a.GetDisplayName())
                .ToList();

            // pre-processing
            _assets.ForEach(asset =>
            {
                if (string.IsNullOrWhiteSpace(asset.DisplayName)) asset.DisplayName = asset.SafeName;

                AI.LoadMedia(asset, false);
                asset.AllMedia.ForEach(m =>
                {
                    if (m.Url.StartsWith("//")) m.Url = "https:" + m.Url;
                });

                // determine right cover image
                AssetMedia cover = asset.AllMedia.FirstOrDefault(a => a.Type == "small_v2" || a.Type == "small");
                if (cover != null)
                {
                    asset.Slug = cover.Url;
                }
                else
                {
                    string file = asset.ToAsset().GetPreviewFile(AI.GetPreviewFolder());
                    if (string.IsNullOrEmpty(file))
                    {
                        asset.Slug = "images/nocover.png";
                    }
                    else
                    {
                        asset.Slug = $"../Previews/{asset.AssetId}/a-{asset.AssetId}.png";

                        // in case Previews folder is not inside target folder, copy preview file to output folder
                        string targetFile = Path.Combine(env.publishFolder, asset.Slug.Substring(3));
                        if (!File.Exists(targetFile))
                        {
                            // copy preview file to output folder
                            string targetFolder = Path.GetDirectoryName(targetFile);
                            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                            File.Copy(file, targetFile, true);
                        }
                    }
                }

                // if there is no media, fallback to cover image
                if (asset.AllMedia.Count == 0)
                {
                    AssetMedia media = new AssetMedia
                    {
                        Type = "main",
                        Url = asset.Slug
                    };
                    asset.AllMedia.Add(media);
                    asset.Media.Add(media);
                }
            });

            string tempFolder = settings.devMode && !string.IsNullOrWhiteSpace(settings.testFolder)
                ? settings.testFolder
                : IOUtils.CreateTempFolder();

            string preservedPackagesJsonPath = null;
            string preservedFilesJsonPath = null;

            // preserve json inside temp folder before deleting all files in it, then copy back afterwards 
            // search recursively for packages.json and files.json in the temp folder
            // assume that even if there are multiple occurrences, they are identical
            if (settings.devMode && settings.preserveJson && Directory.Exists(tempFolder))
            {
                string[] foundPackages = Directory.GetFiles(tempFolder, "packages.json", SearchOption.AllDirectories);
                string[] foundFiles = Directory.GetFiles(tempFolder, "files.json", SearchOption.AllDirectories);

                if (foundPackages.Length > 0)
                {
                    preservedPackagesJsonPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_packages.json");
                    File.Copy(foundPackages[0], preservedPackagesJsonPath, true);
                }
                if (foundFiles.Length > 0)
                {
                    preservedFilesJsonPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_files.json");
                    File.Copy(foundFiles[0], preservedFilesJsonPath, true);
                }
            }

            if (!string.IsNullOrWhiteSpace(tempFolder))
            {
                if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
                Directory.CreateDirectory(tempFolder);
            }

            // Resolve inheritance
            await ResolveInheritance(template, tempFolder, templates);

            // Copy template files to temp folder
            if (settings.devMode && !string.IsNullOrWhiteSpace(settings.devFolder))
            {
                IOUtils.CopyDirectory(settings.devFolder, tempFolder);
            }
            else
            {
                await ExtractTemplate(template.path, tempFolder);
            }

            // replace placeholder JSON files in the new template
            long packageSize = 0;
            long filesSize = 0;
            if (settings.devMode && settings.preserveJson)
            {
                if (!string.IsNullOrEmpty(preservedPackagesJsonPath) && File.Exists(preservedPackagesJsonPath))
                {
                    packageSize = new FileInfo(preservedPackagesJsonPath).Length;
                    foreach (string file in Directory.GetFiles(tempFolder, "packages.json", SearchOption.AllDirectories))
                    {
                        File.Copy(preservedPackagesJsonPath, file, true);
                    }
                    File.Delete(preservedPackagesJsonPath);
                }
                if (!string.IsNullOrEmpty(preservedFilesJsonPath) && File.Exists(preservedFilesJsonPath))
                {
                    filesSize = new FileInfo(preservedFilesJsonPath).Length;
                    foreach (string file in Directory.GetFiles(tempFolder, "files.json", SearchOption.AllDirectories))
                    {
                        File.Copy(preservedFilesJsonPath, file, true);
                    }
                    File.Delete(preservedFilesJsonPath);
                }
            }

            // export data
            if (!settings.devMode || !settings.preserveJson || packageSize + filesSize == 0)
            {
                ExportPackageData("packages.json", tempFolder, template);
                await ExportFileData("files.json", tempFolder, template, env);
            }
            template.hasFilesData = Directory.GetFiles(tempFolder, "files.json", SearchOption.AllDirectories).Length > 0;

            // process HTML templates
            ProcessTemplates(new[] {"*.html", "*.js", "*.md", "*.csv", "*.txt"}, tempFolder, template, env, settings.devMode ? settings.maxDetailPages : 0);
            DeleteTemplateFolders(tempFolder);

            // copy output
            Directory.CreateDirectory(env.publishFolder);
            if (!settings.devMode || settings.publishResult || string.IsNullOrWhiteSpace(settings.testFolder))
            {
                IOUtils.CopyDirectory(tempFolder, env.publishFolder);
            }
            if (!settings.devMode || string.IsNullOrWhiteSpace(settings.testFolder))
            {
                await IOUtils.DeleteFileOrDirectory(tempFolder);
            }
        }

        public static async Task ResolveInheritance(TemplateInfo template, string targetFolder, List<TemplateInfo> templates)
        {
            if (!string.IsNullOrWhiteSpace(template.inheritFrom))
            {
                TemplateInfo inheritFrom = templates.FirstOrDefault(t => t.GetNameFromFile() == template.inheritFrom);
                if (inheritFrom != null)
                {
                    // no check for circular dependencies yet
                    await ResolveInheritance(inheritFrom, targetFolder, templates);
                    await ExtractTemplate(inheritFrom.path, targetFolder);
                }
                else
                {
                    Debug.LogError($"Template '{template.inheritFrom}' not found for inheritance.");
                }

                // move files
                if (template.moveFiles != null)
                {
                    foreach (string file in template.moveFiles)
                    {
                        string[] arr = file.Split('>');
                        if (arr.Length != 2)
                        {
                            Debug.LogError($"Invalid moveFiles entry '{file}'. Should be 'source>target'.");
                            continue;
                        }
                        string sourcePath = Path.Combine(targetFolder, arr[0]);
                        string targetPath = Path.Combine(targetFolder, arr[1]);
                        if (File.Exists(sourcePath))
                        {
                            File.Move(sourcePath, targetPath);
                        }
                        else
                        {
                            Debug.LogError($"File '{file}' not found to be moved.");
                        }
                    }
                }

                // delete files
                if (template.deleteFiles != null)
                {
                    foreach (string file in template.deleteFiles)
                    {
                        string path = Path.Combine(targetFolder, file);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        else
                        {
                            Debug.LogError($"File '{file}' not found for deletion.");
                        }
                    }
                }
            }
        }

        private void ProcessTemplates(string[] filter, string folder, TemplateInfo template, TemplateExportEnvironment env, int maxDetailPages = 0)
        {
            IEnumerable<string> files = IOUtils.GetFiles(folder, filter, SearchOption.AllDirectories);
            if (files.Count() == 0) return;

            folder = folder.Replace("\\", "/");
            foreach (string file in files)
            {
                if (Path.GetFileName(Path.GetDirectoryName(file)).StartsWith("_")) continue; // skip template folders

                TemplateVariables model = new TemplateVariables
                {
                    packages = _assets,
                    dataPath = env.dataPath,
                    imagePath = env.imagePath,
                    pageSize = 250,
                    hasFilesData = template.hasFilesData,
                    internalIdsOnly = env.internalIdsOnly,
                    parameters = template.parameters,
                    affiliateParam = AI.Config.useAffiliateLinks ? $"?{AI.AFFILIATE_PARAM}" : ""
                };

                // handle special files first
                if (Path.GetFileName(file) == "package_details.html")
                {
                    for (int i = 0; i < _assets.Count; i++)
                    {
                        AssetInfo asset = _assets[i];

                        model.package = asset;
                        model.packageFiles = DBAdapter.DB.Query<AssetFile>("select * from AssetFile where AssetId=?", asset.AssetId).ToList();

                        string targetFileName = $"package_{asset.AssetId}.html";
                        if (!env.internalIdsOnly && asset.ForeignId > 0)
                        {
                            targetFileName = $"package_f{asset.ForeignId}.html";
                        }
                        string targetFile = Path.Combine(Path.GetDirectoryName(file), targetFileName);
                        string result = ProcessTemplate(folder, file, model);
                        if (!string.IsNullOrEmpty(result)) File.WriteAllText(targetFile, result);

                        if (maxDetailPages > 0 && i == maxDetailPages - 1) break;
                    }
                    File.Delete(file);
                }
                else
                {
                    string result = ProcessTemplate(folder, file, model);
                    if (!string.IsNullOrEmpty(result)) File.WriteAllText(file, result);
                }
            }
        }

        private void ExportPackageData(string fileName, string folder, TemplateInfo template)
        {
            string[] files = Directory.GetFiles(folder, fileName, SearchOption.AllDirectories);
            if (files.Length == 0) return;

            List<string> defaultPropertiesToExport = new List<string>
            {
                "AssetId", "ParentId", "AssetSource", "DisplayName", "DisplayPublisher", "DisplayCategory", "Description",
                "ForeignId", "PackageSize", "Version", "LastRelease", "LatestVersion", "PublisherId", "SupportedUnityVersions",
                "BIRPCompatible", "URPCompatible", "HDRPCompatible", "AssetRating", "RatingCount", "PriceEur", "PriceUsd", "PriceCny",
                "OfficialState", "Slug"
            };
            if (template.packageFields != null && template.packageFields.Length > 0)
            {
                // add custom fields without creating duplicates
                foreach (string field in template.packageFields)
                {
                    if (!defaultPropertiesToExport.Contains(field)) defaultPropertiesToExport.Add(field);
                }
            }
            string[] propertiesToExport = defaultPropertiesToExport.ToArray();

            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ContractResolver = new ShortenAndFilterPropertiesContractResolver(propertiesToExport),
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.None
            };
            string data = JsonConvert.SerializeObject(_assets, settings);

            foreach (string file in files)
            {
                File.WriteAllText(file, data);
            }
        }

        private async Task ExportFileData(string fileName, string folder, TemplateInfo template, TemplateExportEnvironment env)
        {
            string[] files = Directory.GetFiles(folder, fileName, SearchOption.AllDirectories);
            if (files.Length == 0) return;

            List<string> defaultPropertiesToExport = new List<string>
            {
                "Id", "AssetId", "Path", "Size", "Width", "Height", "Length"
            };
            if (template.fileFields != null && template.fileFields.Length > 0)
            {
                // add custom fields without creating duplicates
                foreach (string field in template.fileFields)
                {
                    if (!defaultPropertiesToExport.Contains(field)) defaultPropertiesToExport.Add(field);
                }
            }
            string[] propertiesToExport = defaultPropertiesToExport.ToArray();
            Dictionary<string, string> propertyMapping = new Dictionary<string, string>
            {
                {"Id", "i"},
                {"AssetId", "a"},
                {"FileName", "f"},
                {"Path", "p"},
                {"Size", "s"},
                {"Width", "w"},
                {"Height", "h"},
                {"Length", "l"}
            };
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ContractResolver = new ShortenAndFilterPropertiesContractResolver(propertiesToExport, propertyMapping),
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.None
            };
            JsonSerializer serializer = JsonSerializer.Create(settings);

            string excludeImages = env.excludeImages ? "and Type not in ('mat','terrainlayer','" + string.Join("','", AI.TypeGroups[AI.AssetGroup.Images]) + "')" : "";
            foreach (string file in files)
            {
                using (StreamWriter streamWriter = new StreamWriter(file))
                using (JsonTextWriter jsonWriter = new JsonTextWriter(streamWriter))
                {
                    jsonWriter.WriteStartArray();

                    foreach (AssetInfo info in _assets)
                    {
                        List<AssetFile> afs = DBAdapter.DB.Query<AssetFile>("select * from AssetFile where AssetId=? " + excludeImages + " order by Path", info.AssetId);

                        // embed "original" previews again into previews folder since otherwise browsing will not show previews
                        Asset asset = info.ToAsset();
                        List<AssetInfo> convertQueue = afs
                            .Where(af => af.PreviewState == AssetFile.PreviewOptions.UseOriginal)
                            .Select(af => new AssetInfo().CopyFrom(asset, af))
                            .ToList();
                        if (convertQueue.Count > 0)
                        {
                            EmbedOriginalPreviewValidator converter = new EmbedOriginalPreviewValidator();
                            converter.DBIssues = convertQueue;
                            await converter.Fix();
                        }

                        afs.ForEach(af =>
                        {
                            if (af.Path != null && af.Path.StartsWith("Assets/")) af.Path = af.Path.Substring(7);
                            serializer.Serialize(jsonWriter, af);
                        });
                        jsonWriter.Flush();
                    }
                    jsonWriter.WriteEndArray();
                }
            }
        }

        private static async Task ExtractTemplate(string path, string targetFolder)
        {

#if UNITY_2021_2_OR_NEWER
            if (!await Task.Run(() => IOUtils.ExtractArchive(path, targetFolder)))
            {
                // stop here when archive could not be extracted (e.g. path too long)
                return;
            }
#else
            FastZip fastZip = new FastZip();
            await Task.Run(() => fastZip.ExtractZip(template, targetFolder, null));
#endif
        }

        private static void DeleteTemplateFolders(string path)
        {
            if (!Directory.Exists(path)) return;

            string[] subdirectories = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);
            foreach (string subdirectory in subdirectories)
            {
                // check if still existent or deleted from deleting parent
                if (!Directory.Exists(subdirectory)) continue;

                string folderName = Path.GetFileName(subdirectory);
                if (folderName.StartsWith("_")) Directory.Delete(subdirectory, true);
            }
        }

        private static string ProcessTemplate(string root, string templatePath, object model)
        {
            // Read the template file content
            string templateText;
            try
            {
                templateText = File.ReadAllText(templatePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error reading template at '{templatePath}': {e.Message}");
                return null;
            }

            // Parse the template using Scriban
            Template template = Template.Parse(templateText);
            if (template.HasErrors)
            {
                // Log each error and throw an exception
                foreach (LogMessage message in template.Messages)
                {
                    Debug.LogError($"Error parsing template '{templatePath}': {message}");
                }
                return null;
            }

            // Create a TemplateContext and assign a FileSystemTemplateLoader
            // The FileSystemTemplateLoader is configured with a root directory from which to resolve includes
            TemplateContext context = new TemplateContext
            {
                TemplateLoader = new FileTemplateLoader(Path.Combine(root, "_templates")),
                LoopLimit = 0,
                StrictVariables = true
            };

            ScriptObject scriptObject = new ScriptObject();
            if (model != null)
            {
                scriptObject.Import(model, renamer: member => member.Name, filter: null);
            }
            scriptObject.Import("forjson", new Func<string, string>(s =>
            {
                string json = JsonConvert.ToString(s);
                return json.Substring(1, json.Length - 2); // remove quotes
            }));
            scriptObject.Import("formd", new Func<string, string>(s => s.Replace("#", "")));
            scriptObject.Import("size2str", new Func<long, string>(EditorUtility.FormatBytes));
            scriptObject.Import("reldate", new Func<DateTime, string>(StringUtils.GetRelativeTimeDifference));

            // Push the model as a global variable into the context
            // Now, the template can access model properties directly
            context.PushGlobal(scriptObject);

            // Render the template with the given model
            string output;
            try
            {
                // The member name selector simply returns the property name
                output = template.Render(context);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error rendering template '{templatePath}': {e.Message}");
                return null;
            }

            return output;
        }
    }

    public class ShortenAndFilterPropertiesContractResolver : DefaultContractResolver
    {
        private readonly HashSet<string> _propertiesToInclude;
        private readonly Dictionary<string, string> _propertyNameMapping;

        /// <summary>
        /// Initializes a new instance of the contract resolver.
        /// </summary>
        /// <param name="propertiesToInclude">
        /// Optional list of property names to include. If null, all properties will be included.
        /// </param>
        /// <param name="propertyNameMapping">
        /// Optional dictionary mapping original property names to desired short names.
        /// If null, property names will not be changed.
        /// </param>
        public ShortenAndFilterPropertiesContractResolver(IEnumerable<string> propertiesToInclude = null, Dictionary<string, string> propertyNameMapping = null)
        {
            _propertiesToInclude = propertiesToInclude != null
                ? new HashSet<string>(propertiesToInclude)
                : null;

            _propertyNameMapping = propertyNameMapping;
        }

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            // Retrieve all properties using the base implementation.
            IList<JsonProperty> properties = base.CreateProperties(type, memberSerialization);

            // If a filter is provided, filter the properties.
            if (_propertiesToInclude != null && _propertiesToInclude.Any())
            {
                properties = properties.Where(p => _propertiesToInclude.Contains(p.PropertyName)).ToList();
            }

            // If a mapping is provided, rename the properties.
            if (_propertyNameMapping != null && _propertyNameMapping.Any())
            {
                foreach (JsonProperty prop in properties)
                {
                    if (_propertyNameMapping.TryGetValue(prop.PropertyName, out string shortName))
                    {
                        prop.PropertyName = shortName;
                    }
                }
            }

            return properties;
        }
    }

    public class FileTemplateLoader : ITemplateLoader
    {
        /// <summary>
        /// The root directory from which templates will be resolved if a caller file is not provided.
        /// </summary>
        public string Root { get; }

        public FileTemplateLoader(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentNullException(nameof (root));
            Root = root;
        }

        /// <summary>
        /// Given the template context, the caller’s source span, and the template name (e.g. from an include),
        /// return the full path to the template file.
        /// </summary>
        /// <param name="context">The current template context.</param>
        /// <param name="callerSpan">The source span of the calling template (may contain a FileName).</param>
        /// <param name="templateName">The name of the template to load.</param>
        /// <returns>The full file system path to the template.</returns>
        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
        {
            return Path.Combine(Root, templateName);
        }

        /// <summary>
        /// Synchronously loads the template content from the specified path.
        /// </summary>
        /// <param name="context">The current template context.</param>
        /// <param name="callerSpan">The source span of the calling template.</param>
        /// <param name="templatePath">The full path to the template file.</param>
        /// <returns>The contents of the template file as a string.</returns>
        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            try
            {
                return File.ReadAllText(templatePath);
            }
            catch (Exception ex)
            {
                throw new FileNotFoundException($"Could not load template from path: {templatePath}", ex);
            }
        }

        /// <summary>
        /// Asynchronously loads the template content from the specified path.
        /// </summary>
        /// <param name="context">The current template context.</param>
        /// <param name="callerSpan">The source span of the calling template.</param>
        /// <param name="templatePath">The full path to the template file.</param>
        /// <returns>A ValueTask containing the template content as a string.</returns>
        public async ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            try
            {
                using (StreamReader reader = new StreamReader(templatePath))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw new FileNotFoundException($"Could not load template asynchronously from path: {templatePath}", ex);
            }
        }
    }
}
#endif