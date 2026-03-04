#if UNITY_2022_2_OR_NEWER
using UnityEditor;
using UnityEngine;
using UnityEditor.Search;
using System.Collections.Generic;
using System.IO;
using UnityEditorInternal;

namespace AssetInventory
{
    // Registers a Unity Search (Ctrl+K) provider
    public static class SearchIntegration
    {
        private const string ProviderId = "assetinventory";
        private const string FilterPrefix = "assetinv:";

        // busy guard for actions triggered from Unity Search
        private static bool _isBusy;
        private static int _busyProgressId;

        private static bool BeginBusy(string caption)
        {
            if (_isBusy) return false;
            _isBusy = true;
            _busyProgressId = MetaProgress.Start(caption);
            RequestSearchUIRepaint();
            return true;
        }

        private static void EndBusy()
        {
            if (_busyProgressId > 0) MetaProgress.Remove(_busyProgressId);
            _busyProgressId = 0;
            _isBusy = false;
            RequestSearchUIRepaint();
        }

        private static void RequestSearchUIRepaint()
        {
            EditorApplication.delayCall += () => InternalEditorUtility.RepaintAllViews();
        }

        private static AssetSearch.Options BuildOptions(SearchContext context, out string residualPhrase)
        {
            string query = context?.searchQuery ?? string.Empty;
            if (query.StartsWith(FilterPrefix)) query = query.Substring(FilterPrefix.Length).TrimStart();

            AssetSearch.Options opt = new AssetSearch.Options
            {
                SearchPhrase = string.Empty,
                IgnoreExcludedExtensions = false,
                MaxResults = 100,
                CurrentPage = 1,
                InMemory = AssetSearch.InMemoryMode.None
            };

            // very light parser for common filters from Unity Search
            // Supports: type:, srp:, width:/height:/length:/size: with ">=" or "<=" or bare number (>=)
            List<string> remaining = new List<string>();
            foreach (string rawToken in query.Split(' '))
            {
                string token = rawToken.Trim();
                if (string.IsNullOrEmpty(token)) continue;

                int colonIdx = token.IndexOf(':');
                if (colonIdx > 0)
                {
                    string key = token.Substring(0, colonIdx).ToLowerInvariant();
                    string val = token.Substring(colonIdx + 1);

                    switch (key)
                    {
                        case "type":
                        case "t":
                            opt.RawSearchType = val;
                            continue;
                        case "srp":
                            switch (val.ToLowerInvariant())
                            {
                                case "birp":
                                    opt.SelectedPackageSRPs = 2;
                                    continue;
                                case "urp":
                                    opt.SelectedPackageSRPs = 3;
                                    continue;
                                case "hdrp":
                                    opt.SelectedPackageSRPs = 4;
                                    continue;
                            }
                            break;
                        case "width":
                            ParseNumeric(val, out string w, out bool wMax);
                            opt.SearchWidth = w;
                            opt.CheckMaxWidth = wMax;
                            continue;
                        case "height":
                            ParseNumeric(val, out string h, out bool hMax);
                            opt.SearchHeight = h;
                            opt.CheckMaxHeight = hMax;
                            continue;
                        case "length":
                            ParseNumeric(val, out string l, out bool lMax);
                            opt.SearchLength = l;
                            opt.CheckMaxLength = lMax;
                            continue;
                        case "size":
                            ParseNumeric(val, out string s, out bool sMax);
                            opt.SearchSize = s;
                            opt.CheckMaxSize = sMax;
                            continue;
                        case "filetag":
                            remaining.Add($"ft:{val}");
                            continue;
                        case "tag": // map to inline package tag so AssetSearch picks it up
                        case "packagetag":
                            remaining.Add($"pt:{val}");
                            continue;
                        case "preview":
                            switch (val.ToLowerInvariant())
                            {
                                case "has":
                                case "yes":
                                case "true":
                                case "1":
                                    opt.SelectedPreviewFilter = 2;
                                    continue;
                                case "no":
                                case "none":
                                case "false":
                                case "0":
                                    opt.SelectedPreviewFilter = 3;
                                    continue;
                            }
                            break;
                    }
                }

                remaining.Add(token);
            }

            opt.SearchPhrase = string.Join(" ", remaining).Trim();
            residualPhrase = opt.SearchPhrase;

            return opt;
        }

        private static void ParseNumeric(string input, out string number, out bool isMax)
        {
            isMax = false;
            number = string.Empty;
            if (string.IsNullOrEmpty(input)) return;

            if (input.StartsWith(">="))
            {
                number = input.Substring(2);
                isMax = false;
            }
            else if (input.StartsWith("<="))
            {
                number = input.Substring(2);
                isMax = true;
            }
            else
            {
                number = input;
                isMax = false;
            }
        }

        [SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            SearchProvider provider = new SearchProvider(ProviderId, "Asset Inventory")
            {
                active = true,
                priority = 999,
                filterId = FilterPrefix,
                fetchItems = (context, items, prov) =>
                {
                    AssetSearch.Options opt = BuildOptions(context, out _);
                    AssetSearch.Result res = AssetSearch.Execute(opt);
                    if (res?.Files != null)
                    {
                        foreach (AssetInfo info in res.Files)
                        {
                            string id = $"{FilterPrefix}{info.Id}";
                            string label = string.IsNullOrEmpty(info.FileName) ? info.GetDisplayName() : info.FileName;
                            string desc = ""; //string.IsNullOrEmpty(info.Path) ? info.GetDisplayName(true) : info.ShortPath + " — " + info.GetDisplayName();

                            items.Add(prov.CreateItem(context, id, label, desc, null, info));
                        }
                    }
                    return null;
                },
                fetchPropositions = EnumeratePropositions,
                fetchColumns = FetchColumns,
                fetchThumbnail = (item, context) =>
                {
                    if (item?.data is AssetInfo info)
                    {
                        string previewPath = info.GetPreviewFile(AI.GetPreviewFolder());
                        if (!string.IsNullOrEmpty(previewPath) && File.Exists(previewPath))
                        {
                            byte[] bytes = File.ReadAllBytes(previewPath);
                            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            tex.LoadImage(bytes);
                            tex.name = info.FileName;
                            return tex;
                        }

                        // fallback icon using AssetInfo's existing method
                        return (Texture2D)info.GetFallbackIcon();
                    }
                    return null;
                },
                fetchLabel = (item, context) => item?.label ?? string.Empty,
                fetchDescription = (item, context) => _isBusy ? "Asset Inventory is working… Please wait." : (item?.description ?? string.Empty),
                isExplicitProvider = false
            };

            // Add to scene
            SearchAction addAction = new SearchAction(ProviderId, "Import", null, "Add To Scene", (System.Action<SearchItem[]>)((items) =>
            {
                if (_isBusy) return;
                if (items != null && items.Length > 0 && items[0]?.data is AssetInfo info)
                {
                    AddAssetToScene(info);
                }
            }));
            addAction.enabled = (items) => !_isBusy;
            provider.actions.Add(addAction);

            // Open item
            SearchAction openAction = new SearchAction(ProviderId, "Open", null, "Open Item", (System.Action<SearchItem[]>)((items) =>
            {
                if (_isBusy) return;
                if (items != null && items.Length > 0 && items[0]?.data is AssetInfo info)
                {
                    OpenAsset(info);
                }
            }));
            openAction.enabled = (items) => !_isBusy;
            provider.actions.Add(openAction);

            // Search action (open Asset Inventory)
            SearchAction searchAction = new SearchAction(ProviderId, "Search", null, "Search in Asset Inventory", (System.Action<SearchItem[]>)((items) =>
            {
                // Get the filename from the selected item
                string searchPhrase = string.Empty;
                if (items != null && items.Length > 0)
                {
                    // Extract filename from the first selected item
                    if (items[0]?.data is AssetInfo info)
                    {
                        searchPhrase = string.IsNullOrEmpty(info.FileName) ? info.GetDisplayName() : info.FileName;
                    }
                }
                ShowWindow(searchPhrase);
            }));
            searchAction.enabled = (items) => !_isBusy;
            provider.actions.Add(searchAction);

            return provider;
        }

        private static IEnumerable<SearchProposition> EnumeratePropositions(SearchContext context, SearchPropositionOptions options)
        {
            if (!options.flags.HasAny(SearchPropositionFlags.QueryBuilder)) yield break;

            // Icons
            Texture2D typeIcon = EditorGUIUtility.IconContent("FilterByType@2x", "d_FilterByType@2x").image as Texture2D;
            Texture2D numberIcon = EditorGUIUtility.IconContent("d_ScaleTool").image as Texture2D;
            Texture2D tagIcon = EditorGUIUtility.IconContent("d_Favorite").image as Texture2D;
            Texture2D srpIcon = EditorGUIUtility.IconContent("d_Preset.Context").image as Texture2D;

            // Type group shortcuts (map to RawSearchType understood by AssetSearch via AI.AssetGroup)
            foreach (string t in new[] {"Audio", "Images", "Videos", "Prefabs", "Materials", "Shaders", "Models", "Animations", "Fonts", "Scripts", "Libraries", "Documents"})
            {
                yield return new SearchProposition(category: "Type", label: t, replacement: $"type:{t}", icon: typeIcon, color: GetCategoryColor("Type"));
            }

            // SRP filters
            {
                yield return new SearchProposition(category: "SRP", label: "BiRP", replacement: "srp:birp", icon: srpIcon, color: GetCategoryColor("SRP"));
                yield return new SearchProposition(category: "SRP", label: "URP", replacement: "srp:urp", icon: srpIcon, color: GetCategoryColor("SRP"));
                yield return new SearchProposition(category: "SRP", label: "HDRP", replacement: "srp:hdrp", icon: srpIcon, color: GetCategoryColor("SRP"));
            }

            // Dimensions
            {
                yield return new SearchProposition(category: "Dimensions", label: "Width >= 1024", replacement: "width:>=1024", icon: numberIcon, color: GetCategoryColor("Dimensions"));
                yield return new SearchProposition(category: "Dimensions", label: "Height >= 1024", replacement: "height:>=1024", icon: numberIcon, color: GetCategoryColor("Dimensions"));
                yield return new SearchProposition(category: "Dimensions", label: "Width <= 512", replacement: "width:<=512", icon: numberIcon, color: GetCategoryColor("Dimensions"));
                yield return new SearchProposition(category: "Dimensions", label: "Height <= 512", replacement: "height:<=512", icon: numberIcon, color: GetCategoryColor("Dimensions"));
            }

            // Length (seconds)
            {
                yield return new SearchProposition(category: "Length", label: "Length >= 5s", replacement: "length:>=5", icon: numberIcon, color: GetCategoryColor("Length"));
                yield return new SearchProposition(category: "Length", label: "Length <= 3s", replacement: "length:<=3", icon: numberIcon, color: GetCategoryColor("Length"));
            }

            // Size (KB)
            {
                yield return new SearchProposition(category: "Size", label: "Size <= 256KB", replacement: "size:<=256", icon: numberIcon, color: GetCategoryColor("Size"));
                yield return new SearchProposition(category: "Size", label: "Size >= 1024KB", replacement: "size:>=1024", icon: numberIcon, color: GetCategoryColor("Size"));
            }

            // Tags (map to inline tokens that AssetSearch understands)
            {
                yield return new SearchProposition(category: "Tags", label: "Package Tag...", replacement: "packagetag:", help: "Use packagetag:<name>", icon: tagIcon, color: GetCategoryColor("Tags"));
                yield return new SearchProposition(category: "Tags", label: "File Tag...", replacement: "filetag:", help: "Use filetag:<name>", icon: tagIcon, color: GetCategoryColor("Tags"));
            }

            // Preview filters
            {
                yield return new SearchProposition(category: "Preview", label: "Has Preview", replacement: "preview:has", icon: typeIcon, color: GetCategoryColor("Preview"));
                yield return new SearchProposition(category: "Preview", label: "No Preview", replacement: "preview:no", icon: typeIcon, color: GetCategoryColor("Preview"));
            }
        }

        private static IEnumerable<SearchColumn> FetchColumns(SearchContext context, IEnumerable<SearchItem> items)
        {
            yield return new SearchColumn("AssetInventory/Size", "size", "AssetInventory/Size");
            yield return new SearchColumn("AssetInventory/Dimensions", "dimensions", "AssetInventory/Dimensions");
            yield return new SearchColumn("AssetInventory/Type", "type", "AssetInventory/Type");
            yield return new SearchColumn("AssetInventory/Source", "source", "AssetInventory/Source");
            yield return new SearchColumn("AssetInventory/Length", "length", "AssetInventory/Length");
            yield return new SearchColumn("AssetInventory/Publisher", "publisher", "AssetInventory/Publisher");
            yield return new SearchColumn("AssetInventory/PackageName", "packagename", "AssetInventory/PackageName");
            yield return new SearchColumn("AssetInventory/AICaption", "aicaption", "AssetInventory/AICaption");
        }

        private static Color GetCategoryColor(string category)
        {
            switch (category.ToLowerInvariant())
            {
                case "type": return new Color(0.30f, 0.55f, 0.95f); // blue
                case "srp": return new Color(0.65f, 0.40f, 0.95f); // purple
                case "dimensions": return new Color(1.00f, 0.60f, 0.20f); // orange
                case "length": return new Color(0.20f, 0.70f, 0.70f); // teal
                case "size": return new Color(0.35f, 0.80f, 0.45f); // green
                case "tags": return new Color(0.90f, 0.30f, 0.60f); // magenta-ish
                case "preview": return new Color(0.80f, 0.50f, 0.20f); // brown-ish
            }
            return new Color(0.6f, 0.6f, 0.6f);
        }

        [SearchColumnProvider("AssetInventory/Size")]
        public static void InitializeSizeColumn(SearchColumn column)
        {
            column.getter = args =>
            {
                if (args.item?.data is AssetInfo info) return EditorUtility.FormatBytes(info.Size);
                return string.Empty;
            };
        }

        [SearchColumnProvider("AssetInventory/Dimensions")]
        public static void InitializeDimensionsColumn(SearchColumn column)
        {
            column.getter = args =>
            {
                if (args.item?.data is AssetInfo info)
                {
                    if (info.Width > 0 && info.Height > 0) return $"{info.Width}×{info.Height}";
                }
                return string.Empty;
            };
        }

        [SearchColumnProvider("AssetInventory/Type")]
        public static void InitializeTypeColumn(SearchColumn column)
        {
            column.getter = args =>
            {
                if (args.item?.data is AssetInfo info) return info.Type ?? string.Empty;
                return string.Empty;
            };
        }

        [SearchColumnProvider("AssetInventory/Source")]
        public static void InitializeSourceColumn(SearchColumn column)
        {
            column.getter = args =>
            {
                if (args.item?.data is AssetInfo info)
                {
                    switch (info.AssetSource)
                    {
                        case Asset.Source.AssetStorePackage: return "Asset Store";
                        case Asset.Source.RegistryPackage: return "Registry";
                        case Asset.Source.CustomPackage: return "Custom";
                        case Asset.Source.Directory: return "Directory";
                        case Asset.Source.Archive: return "Archive";
                        case Asset.Source.AssetManager: return "Asset Manager";
                        default: return info.AssetSource.ToString();
                    }
                }
                return string.Empty;
            };
        }

        [SearchColumnProvider("AssetInventory/Length")]
        public static void InitializeLengthColumn(SearchColumn column)
        {
            column.getter = args =>
            {
                if (args.item?.data is AssetInfo info && info.Length > 0) return $"{info.Length:F1}s";
                return string.Empty;
            };
        }

        [SearchColumnProvider("AssetInventory/Publisher")]
        public static void InitializePublisherColumn(SearchColumn column)
        {
            column.getter = args =>
            {
                if (args.item?.data is AssetInfo info) return info.GetDisplayPublisher() ?? string.Empty;
                return string.Empty;
            };
        }

        [SearchColumnProvider("AssetInventory/PackageName")]
        public static void InitializePackageNameColumn(SearchColumn column)
        {
            column.getter = args =>
            {
                if (args.item?.data is AssetInfo info) return info.GetRoot().GetDisplayName() ?? string.Empty;
                return string.Empty;
            };
        }

        [SearchColumnProvider("AssetInventory/AICaption")]
        public static void InitializeAICaptionColumn(SearchColumn column)
        {
            column.getter = args =>
            {
                if (args.item?.data is AssetInfo info) return info.AICaption ?? string.Empty;
                return string.Empty;
            };
        }

        private static void ShowWindow(string searchPhrase = null)
        {
            IndexUI window = EditorWindow.GetWindow<IndexUI>("Asset Inventory");
            if (window != null)
            {
                window.minSize = new Vector2(650, 300);
                if (!string.IsNullOrEmpty(searchPhrase))
                {
                    window.SetInitialSearch(searchPhrase);
                }
            }
        }

        private static async void OpenAsset(AssetInfo info)
        {
            if (!BeginBusy("Opening Item")) return;
            try
            {
                string targetPath;
                if (info.InProject)
                {
                    targetPath = info.ProjectPath;
                }
                else
                {
                    targetPath = await AI.EnsureMaterializedAsset(info);
                    if (info.Id == 0) return; // was deleted
                }

                if (targetPath != null) EditorUtility.OpenWithDefaultApp(targetPath);
            }
            finally
            {
                EndBusy();
            }
        }

        private static async void AddAssetToScene(AssetInfo info)
        {
            if (!BeginBusy("Adding To Scene")) return;
            try
            {
                string targetPath;
                if (info.InProject)
                {
                    targetPath = info.ProjectPath;
                }
                else
                {
                    // Use AI.CopyTo to handle materialization and import
                    targetPath = await AI.CopyTo(info, AI.Config.importFolder, true, false, false, false, true);
                    if (targetPath == null) return; // error occurred
                }

                if (targetPath != null)
                {
                    // Load the asset and add to scene
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
                    if (prefab == null) return; // not a prefab, stop at importing

                    GameObject instanceObj = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    if (instanceObj == null) return;

                    SceneView sceneView = SceneView.lastActiveSceneView;
                    Vector3 targetPosition = sceneView != null ? sceneView.pivot : Vector3.zero;
                    instanceObj.transform.position = targetPosition;

                    Undo.RegisterCreatedObjectUndo(instanceObj, "Add Asset From Asset Inventory");
                    Selection.activeGameObject = instanceObj;
                    if (instanceObj.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(instanceObj.scene);
                }
            }
            finally
            {
                EndBusy();
            }
        }
    }
}
#endif
