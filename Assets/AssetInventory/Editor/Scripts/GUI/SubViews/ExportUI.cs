using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class ExportUI : BasicEditorUI
    {
        private const string REMAINING_EXTENSIONS = "All the Rest";
        private const string TEMP_FOLDER = "AITemplateCache";
        private const int TILE_SIZE = 300;

        private string _separator = ";";
        private Vector2 _scrollPos;
        private bool _fileMode;
        private List<AssetInfo> _assets;
        private List<ED> _exportFields;
        private List<ED> _overrideFields;
        private List<ED> _exportTypes;
        private int _selectedExportOption;
        private bool _addHeader = true;
        private bool _showFields = true;
        private bool _clearTarget;
        private bool _overrideExisting;
        private List<AssetInfo> _packages;
        private int _packageCount;
        private bool _exportInProgress;
        private List<string> _exportableExtensions;
        private int _curProgress;
        private int _maxProgress;
        private ActionProgress _progress;
        private bool _autoDownload;
        private bool _flattenStructure;
        private bool _metaFiles;

        // Wizard related fields
        private bool _wizardActive = true;
        private List<ExportTypeInfo> _exportTypeInfos;
        private Vector2 _wizardScrollPos;

#if UNITY_2021_2_OR_NEWER
        private FileSystemWatcher _watcher;
        private bool _triggerExport;
        private int _selectedTemplate;
        private string _templateFolder;
        private List<TemplateInfo> _templates;
        private string[] _templateNames;
        private string _overridesFolder;
        private List<string> _overrideCandidates;

        public void OnEnable()
        {
            LoadTemplates();
            PrepareOverrides();

            EditorApplication.update += () =>
            {
                if (_triggerExport) ExportTemplate();
            };

            // Initialize export type infos with descriptions and icons
            InitExportTypeInfos();
        }

        public void OnDisable()
        {
            if (_watcher != null) StopTemplateWatcher();
        }

        private void LoadTemplates()
        {
            _templates = TemplateUtils.LoadTemplates();
            _templateFolder = TemplateUtils.GetTemplateRootFolder();
            _templateNames = _templates.Select(t => t.name).ToArray();
        }
#else
        public void OnEnable()
        {
            // Initialize export type infos with descriptions and icons
            InitExportTypeInfos();
        }
#endif

        public static ExportUI ShowWindow()
        {
            ExportUI window = GetWindow<ExportUI>("Asset Export");
            window.minSize = new Vector2(500, 300);

            return window;
        }

        public void Init(List<AssetInfo> assets, bool fileMode = false, int exportType = 0, int[] columns = null)
        {
            _fileMode = fileMode;
            _assets = assets;
            if (!_fileMode) _assets = _assets.Where(a => a.SafeName != Asset.NONE).ToList();

            _packages = assets.GroupBy(a => a.AssetId).Select(a => a.First()).ToList(); // cast to list to make it serializable during script reloads
            _packageCount = _packages.Count;
            _wizardActive = !_fileMode; // only one type supported right now
            if (_fileMode) _flattenStructure = true;

            _exportableExtensions = AI.TypeGroups.SelectMany(tg => tg.Value).ToList();

            _selectedExportOption = exportType;
            _exportFields = new List<ED>
            {
                new ED("Asset/Id"),
                new ED("Asset/ParentId"),
                new ED("Asset/ForeignId", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.ForeignId)),
                new ED("Asset/AssetRating", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Rating)),
                new ED("Asset/AssetSource", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Source)),
                new ED("Asset/AssetLink", false),
                new ED("Asset/Backup", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Backup)),
                new ED("Asset/BIRPCompatible", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.BIRP)),
                new ED("Asset/CompatibilityInfo", false),
                new ED("Asset/CurrentState", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.InternalState)),
                new ED("Asset/CurrentSubState", false),
                new ED("Asset/Description", false),
                new ED("Asset/DisplayCategory", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Category)),
                new ED("Asset/DisplayName", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Name)),
                new ED("Asset/DisplayPublisher", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Publisher)),
                new ED("Asset/ETag", false),
                new ED("Asset/Exclude", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Exclude)),
                new ED("Asset/Extract", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Extract)),
                new ED("Asset/FirstRelease", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.ReleaseDate)),
                new ED("Asset/HDRPCompatible", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.HDRP)),
                new ED("Asset/Hotness", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Popularity)),
                new ED("Asset/IsHidden", false),
                new ED("Asset/KeyFeatures", false),
                new ED("Asset/Keywords"),
                new ED("Asset/LastOnlineRefresh", false),
                new ED("Asset/LastRelease", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.UpdateDate)),
                new ED("Asset/LatestVersion", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Version)),
                new ED("Asset/License", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.License)),
                new ED("Asset/LicenseLocation", false),
                new ED("Asset/Location", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Location)),
                new ED("Asset/OriginalLocation", false),
                new ED("Asset/PackageSize", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Size)),
                new ED("Asset/PackageSource"),
                new ED("Asset/PackageTags", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Tags)),
                new ED("Asset/PriceEur", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Price)),
                new ED("Asset/PriceUsd", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Price)),
                new ED("Asset/PriceCny", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Price)),
                new ED("Asset/PurchaseDate", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.PurchaseDate)),
                new ED("Asset/RatingCount", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.RatingCount)),
                new ED("Asset/Registry", false),
                new ED("Asset/ReleaseNotes", false),
                new ED("Asset/Repository", false),
                new ED("Asset/Revision"),
                new ED("Asset/SafeCategory"),
                new ED("Asset/SafeName"),
                new ED("Asset/SafePublisher"),
                new ED("Asset/Slug", false),
                new ED("Asset/SupportedUnityVersions", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.UnityVersions)),
                new ED("Asset/UpdateStrategy", false),
                new ED("Asset/URPCompatible", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.URP)),
                new ED("Asset/UseAI", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.AICaptions)),
                new ED("Asset/Version", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Version))
            };
            _overrideFields = new List<ED>
            {
                new ED("Asset/AssetRating", false),
                new ED("Asset/BIRPCompatible", false),
                new ED("Asset/CompatibilityInfo", false),
                new ED("Asset/Description", false),
                new ED("Asset/DisplayCategory", false),
                new ED("Asset/DisplayName", false),
                new ED("Asset/DisplayPublisher", false),
                new ED("Asset/FirstRelease", false),
                new ED("Asset/ForeignId", false),
                new ED("Asset/HDRPCompatible", false),
                new ED("Asset/Hotness", false),
                new ED("Asset/KeyFeatures", false),
                new ED("Asset/Keywords", false),
                new ED("Asset/LastRelease", false),
                new ED("Asset/LatestVersion", false),
                new ED("Asset/License", false),
                new ED("Asset/LicenseLocation", false),
                new ED("Asset/PackageTags", false),
                new ED("Asset/PriceEur", false),
                new ED("Asset/PriceUsd", false),
                new ED("Asset/PriceCny", false),
                new ED("Asset/PurchaseDate", false),
                new ED("Asset/RatingCount", false),
                new ED("Asset/Registry", false),
                new ED("Asset/ReleaseNotes", false),
                new ED("Asset/Repository", false),
                new ED("Asset/Revision", false),
                new ED("Asset/SafeCategory", false),
                new ED("Asset/SafePublisher", false),
                new ED("Asset/Slug", false),
                new ED("Asset/SupportedUnityVersions", false),
                new ED("Asset/URPCompatible", false),
                new ED("Asset/Version", false)
            };
            _exportTypes = new List<ED>
            {
                new ED(AI.AssetGroup.Audio.ToString()),
                new ED(AI.AssetGroup.Images.ToString()),
                new ED(AI.AssetGroup.Videos.ToString()),
                new ED(AI.AssetGroup.Models.ToString()),
                new ED(AI.AssetGroup.Documents.ToString(), false),
                new ED(AI.AssetGroup.Scripts.ToString(), false),
                new ED(AI.AssetGroup.Shaders.ToString(), false),
                new ED(AI.AssetGroup.Animations.ToString(), false),
                new ED(REMAINING_EXTENSIONS, false)
            };

            // Initialize export type infos with descriptions and icons
            InitExportTypeInfos();
        }

        private void InitExportTypeInfos()
        {
            // Define export type information with descriptions and preview images
            _exportTypeInfos = new List<ExportTypeInfo>
            {
                new ExportTypeInfo(
                    0,
                    "CSV Export",
                    "Exports package metadata to a CSV file. Useful for creating reports or processing data in spreadsheet applications.",
                    CreatePlaceholderPreview("CSV Export", "Column data in spreadsheet format", new Color(0.8f, 0.9f, 1f))),

                new ExportTypeInfo(
                    4,
                    "Template Export",
                    "Exports packages using customizable templates. Perfect for creating documentation websites or catalogs of your assets.",
                    CreatePlaceholderPreview("Template Export", "Web-based documentation format", new Color(1f, 0.8f, 0.9f))),

                new ExportTypeInfo(
                    2,
                    "Asset Export",
                    "Exports the actual asset files to an external folder. You can filter by file type and include meta files for use in other projects.",
                    CreatePlaceholderPreview("Asset Export", "Files exported to external folder", new Color(0.8f, 1f, 0.8f))),

                new ExportTypeInfo(
                    1,
                    "License Export",
                    "Generates a Markdown file containing license information from all selected packages. Great for documenting third-party assets in your project.",
                    CreatePlaceholderPreview("License Export", "Markdown formatted license data", new Color(0.9f, 0.8f, 1f))),

                new ExportTypeInfo(
                    3,
                    "Package Override",
                    "Creates JSON override files that can be used to customize package metadata without modifying the original assets.",
                    CreatePlaceholderPreview("Package Override", "JSON configuration file format", new Color(1f, 0.9f, 0.8f)))

            };
        }

        /// <summary>
        /// Creates a placeholder preview texture with text for an export type
        /// </summary>
        private Texture2D CreatePlaceholderPreview(string title, string subtitle, Color backgroundColor)
        {
            // Create texture with given background color
            int width = 400;
            int height = 240;
            Texture2D texture = new Texture2D(width, height);

            // Fill with background color
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            texture.SetPixels(pixels);

            // Add border
            Color borderColor = new Color(
                backgroundColor.r * 0.7f,
                backgroundColor.g * 0.7f,
                backgroundColor.b * 0.7f
            );

            // Draw borderlines (top, right, bottom, left)
            for (int x = 0; x < width; x++)
            {
                texture.SetPixel(x, 0, borderColor);
                texture.SetPixel(x, height - 1, borderColor);
            }
            for (int y = 0; y < height; y++)
            {
                texture.SetPixel(0, y, borderColor);
                texture.SetPixel(width - 1, y, borderColor);
            }

            // We can't easily render text onto a texture directly in Unity Editor scripts,
            // so we'll create a patterned design to make it look like a document/result

            // Draw simplified representation based on export type
            if (title.Contains("CSV"))
            {
                // Draw a grid pattern for CSV
                for (int y = 40; y < height - 20; y += 30)
                {
                    for (int x = 20; x < width - 20; x++)
                    {
                        texture.SetPixel(x, y, borderColor);
                    }
                }

                for (int x = 100; x < width - 20; x += 100)
                {
                    for (int y = 20; y < height - 20; y++)
                    {
                        texture.SetPixel(x, y, borderColor);
                    }
                }
            }
            else if (title.Contains("License"))
            {
                // Draw lines representing text for markdown
                for (int y = 50; y < height - 40; y += 18)
                {
                    int lineWidth = UnityEngine.Random.Range(width / 2, width - 40);
                    for (int x = 30; x < lineWidth; x++)
                    {
                        texture.SetPixel(x, y, borderColor);
                    }
                }

                // Draw some header lines
                for (int x = 30; x < width - 60; x++)
                {
                    texture.SetPixel(x, 30, borderColor);
                }
            }
            else if (title.Contains("Asset"))
            {
                // Draw file icons
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 4; col++)
                    {
                        int startX = 40 + col * 90;
                        int startY = 40 + row * 70;

                        // Draw folder/file icon
                        for (int y = 0; y < 40; y++)
                        {
                            for (int x = 0; x < 50; x++)
                            {
                                if (x < 40 && y < 30)
                                {
                                    texture.SetPixel(startX + x, startY + y, borderColor);
                                }
                            }
                        }
                    }
                }
            }
            else if (title.Contains("Override"))
            {
                // Draw JSON-like structure
                string[] lines = new string[]
                {
                    "{",
                    "   \"name\": \"Package Name\",",
                    "   \"version\": \"1.0.0\",",
                    "   \"description\": \"...\",",
                    "   \"category\": \"...\",",
                    "   \"tags\": [ \"...\", \"...\" ]",
                    "}"
                };

                for (int i = 0; i < lines.Length; i++)
                {
                    int y = 50 + i * 25;
                    int lineWidth = 20 + lines[i].Length * 5;
                    for (int x = 30; x < lineWidth; x++)
                    {
                        texture.SetPixel(x, y, borderColor);
                    }
                }
            }
            else if (title.Contains("Template"))
            {
                // Draw web page layout
                // Header
                for (int y = height - 40; y < height - 20; y++)
                {
                    for (int x = 20; x < width - 20; x++)
                    {
                        texture.SetPixel(x, y, borderColor);
                    }
                }

                // Sidebar
                for (int y = 40; y < height - 40; y++)
                {
                    for (int x = 20; x < 100; x++)
                    {
                        if ((y - 40) % 30 < 20)
                        {
                            texture.SetPixel(x, y, borderColor);
                        }
                    }
                }

                // Content area with grid
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 2; col++)
                    {
                        int startX = 120 + col * 130;
                        int startY = 180 - row * 60;

                        for (int y = 0; y < 40; y++)
                        {
                            for (int x = 0; x < 110; x++)
                            {
                                if (x < 100 && y < 30)
                                {
                                    texture.SetPixel(startX + x, startY + y, borderColor);
                                }
                            }
                        }
                    }
                }
            }

            texture.Apply();
            return texture;
        }

        private bool IsVisibleColumn(int[] columns, AssetTreeViewControl.Columns column)
        {
            return columns != null && columns.Contains((int)column);
        }

        public override void OnGUI()
        {
            if (_assets == null || _assets.Count == 0)
            {
                Close();
                return;
            }

            if (_wizardActive)
            {
                DrawWizard();
            }
            else
            {
                DrawExportOptions();
            }
        }

        private void DrawWizard()
        {
            GUILayout.Space(10);

            // Header
            GUILayout.Label("Select How To Export", UIStyles.centerLabel);
            GUILayout.Space(10);

            // Create scrollview for the export type grid
            _wizardScrollPos = EditorGUILayout.BeginScrollView(_wizardScrollPos);

            // Get available width
            float availableWidth = EditorGUIUtility.currentViewWidth - 20; // allowing for scrollbar and margins
            float leftMargin = 20; // (availableWidth - totalRowWidth) / 2;

            // Calculate the number of tiles per row based on width
            // For these large preview tiles, we'll limit to 1 per row on smaller screens and 2 on larger screens
            int tilesPerRow = Mathf.Max(1, Mathf.FloorToInt(availableWidth / (TILE_SIZE + 30)));

            // Start horizontal layout for centering
            GUILayout.BeginHorizontal();
            GUILayout.Space(leftMargin);

            // Draw the export type tiles in a grid
            GUILayout.BeginVertical();

            int count = 0;
            for (int i = 0; i < _exportTypeInfos.Count; i++)
            {
                if (count % tilesPerRow == 0)
                {
                    if (count > 0)
                    {
                        GUILayout.EndHorizontal();
                        // Add vertical spacing between rows
                        GUILayout.Space(30);
                    }
                    GUILayout.BeginHorizontal();
                }

                // Draw the tile
                if (DrawExportTypeTile(_exportTypeInfos[i]))
                {
                    _selectedExportOption = _exportTypeInfos[i].Index;
                    _wizardActive = false;
                }

                // Add horizontal spacing between tiles in the same row
                if ((count % tilesPerRow) < tilesPerRow - 1 && i < _exportTypeInfos.Count - 1)
                {
                    GUILayout.Space(30);
                }

                count++;
            }

            // End the last row
            if (_exportTypeInfos.Count > 0)
            {
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();

            GUILayout.Space(10);
        }

        private bool DrawExportTypeTile(ExportTypeInfo info)
        {
            bool wasClicked = false;

            // Calculate the total size needed for the tile
            float padding = 10f;
            float innerWidth = TILE_SIZE - (padding * 2);
            float titleHeight = 22f;
            float buttonHeight = 30f;
            float previewPadding = 5f;

            // Calculate preview area size maintaining aspect ratio (400x240)
            float previewWidth = innerWidth;
            float previewHeight = previewWidth * (240f / 400f); // Maintain aspect ratio
            float descriptionHeight = 60f;

            float totalHeight = padding + titleHeight + previewPadding + previewHeight + previewPadding + descriptionHeight + buttonHeight + padding;

            // Get the rect for the entire tile - explicitly use _tileSize here
            Rect tileRect = GUILayoutUtility.GetRect(TILE_SIZE, totalHeight, GUILayout.Width(TILE_SIZE), GUILayout.ExpandWidth(false));

            // Draw box background
            GUI.Box(tileRect, GUIContent.none, GUI.skin.box);

            // Check for clicks on the entire tile
            if (Event.current.type == EventType.MouseDown && tileRect.Contains(Event.current.mousePosition))
            {
                wasClicked = true;
                Event.current.Use();
            }

            // Calculate inner rects for each element
            Rect titleRect = new Rect(tileRect.x + padding, tileRect.y + padding, innerWidth, titleHeight);
            Rect previewRect = new Rect(tileRect.x + padding, titleRect.yMax + padding, innerWidth, previewHeight);
            Rect descriptionRect = new Rect(tileRect.x + padding, previewRect.yMax + padding, innerWidth, descriptionHeight);
            Rect buttonRect = new Rect(tileRect.x + padding, descriptionRect.yMax + padding, innerWidth, buttonHeight);

            // Draw title
            GUI.Label(titleRect, info.Name, EditorStyles.boldLabel);

            // Draw preview image
            if (info.Icon != null)
            {
                // Create a style for centered preview with border
                GUIStyle previewStyle = new GUIStyle(GUI.skin.box);
                previewStyle.normal.background = EditorGUIUtility.whiteTexture;

                // Draw background for preview area
                Color oldColor = GUI.color;
                GUI.color = new Color(0.9f, 0.9f, 0.9f); // Light gray background
                GUI.Box(previewRect, GUIContent.none, previewStyle);
                GUI.color = oldColor;

                // Draw the image scaled to fit
                GUI.DrawTexture(previewRect, info.Icon, ScaleMode.ScaleToFit);
            }
            else
            {
                // Fallback if image is null
                GUI.Box(previewRect, "No Preview Available");
            }

            // Draw description
            GUI.Label(descriptionRect, info.Description, EditorStyles.wordWrappedLabel);

            // Draw button
            if (GUI.Button(buttonRect, "Select"))
            {
                wasClicked = true;
            }

            return wasClicked;
        }

        private void DrawExportOptions()
        {
            int labelWidth = 110;
            EditorGUI.BeginDisabledGroup(_exportInProgress);

            if (!_fileMode)
            {
                // Back button
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(EditorGUIUtility.IconContent("d_back@2x"), GUILayout.Width(24), GUILayout.Height(22)))
                {
                    _wizardActive = true;
                }
                EditorGUILayout.LabelField($"{_exportTypeInfos.First(e => e.Index == _selectedExportOption).Name}", EditorStyles.boldLabel);
                GUILayout.EndHorizontal();

                EditorGUILayout.Space(10);
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            if (_fileMode)
            {
                EditorGUILayout.LabelField($"{_assets.Count:N0} files", EditorStyles.label);
            }
            else
            {
                if (_packageCount == 1)
                {
                    EditorGUILayout.LabelField($"Custom Selection ({_assets.First().GetDisplayName()})");
                }
                else
                {
                    EditorGUILayout.LabelField($"Custom Selection ({_packageCount} packages)");
                }
            }
            GUILayout.EndHorizontal();

            switch (_selectedExportOption)
            {
                case 0:
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Header Line", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    _addHeader = EditorGUILayout.Toggle(_addHeader);
                    GUILayout.EndHorizontal();

                    EditorGUILayout.Space();
                    _showFields = EditorGUILayout.BeginFoldoutHeaderGroup(_showFields, "Fields");
                    if (_showFields)
                    {
                        EditorGUILayout.Space();
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("Select All")) _exportFields.ForEach(f => f.isSelected = true);
                        if (GUILayout.Button("Select None")) _exportFields.ForEach(f => f.isSelected = false);
                        if (GUILayout.Button("Select Default")) _exportFields.ForEach(f => f.isSelected = f.isDefault);
                        if (GUILayout.Button("Select Visible Columns")) _exportFields.ForEach(f => f.isSelected = f.isVisibleColumn);
                        GUILayout.EndHorizontal();
                        EditorGUILayout.Space();

                        _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
                        foreach (ED ed in _exportFields)
                        {
                            GUILayout.BeginHorizontal();
                            ed.isSelected = EditorGUILayout.Toggle(ed.isSelected, GUILayout.Width(20));
                            EditorGUILayout.LabelField(ed.field);
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndScrollView();
                    }
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Export...", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT))) ExportCSV();
                    break;

                case 1:
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("The export will only include information about packages that actually contain license data.", MessageType.Info);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Export...", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT))) ExportLicenses();
                    break;

                case 2:
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Clear Target", "Deletes any previously existing export for the specific package, otherwise only copies new files."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    _clearTarget = EditorGUILayout.Toggle(_clearTarget);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Flatten", "Put all files in the target folder directly independent of the sub-folders they are contained in."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    _flattenStructure = EditorGUILayout.Toggle(_flattenStructure);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Download", "Triggers download of package automatically in case it is not available yet in the cache."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    _autoDownload = EditorGUILayout.Toggle(_autoDownload);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Meta Files", "Exports also meta files if they exist."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    _metaFiles = EditorGUILayout.Toggle(_metaFiles);
                    GUILayout.EndHorizontal();

                    if (!_fileMode)
                    {
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("File Types", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        if (GUILayout.Button("Typical", GUILayout.ExpandWidth(false))) _exportTypes.ForEach(et => et.isSelected = et.isDefault);
                        if (GUILayout.Button("All", GUILayout.ExpandWidth(false))) _exportTypes.ForEach(et => et.isSelected = true);
                        if (GUILayout.Button("None", GUILayout.ExpandWidth(false))) _exportTypes.ForEach(et => et.isSelected = false);
                        GUILayout.EndHorizontal();

                        int typeWidth = 70;
                        for (int i = 0; i < _exportTypes.Count; i++)
                        {
                            // show always three items per row
                            if (i % 3 == 0)
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Space(117);
                            }
                            _exportTypes[i].isSelected = EditorGUILayout.Toggle(_exportTypes[i].isSelected, GUILayout.Width(20));
                            EditorGUILayout.LabelField(_exportTypes[i].pointer, GUILayout.Width(typeWidth));
                            if (i % 3 == 2 || i == _exportTypes.Count - 1) GUILayout.EndHorizontal();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.HelpBox("Make sure you own the appropriate rights in case you intend to use assets in other contexts than Unity!", MessageType.Warning);
                    if (_exportInProgress) UIStyles.DrawProgressBar((float)_curProgress / _maxProgress, $"{_curProgress}/{_maxProgress}");
                    if (GUILayout.Button(_exportInProgress ? "Export in progress..." : "Export...", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                    {
                        ExportAssets();
                    }
                    break;

                case 3:
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Override Existing", ""), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    _overrideExisting = EditorGUILayout.Toggle(_overrideExisting);
                    GUILayout.EndHorizontal();

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Fields to override", EditorStyles.boldLabel);
                    EditorGUILayout.Space();
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Select All")) _overrideFields.ForEach(f => f.isSelected = true);
                    if (GUILayout.Button("Select None")) _overrideFields.ForEach(f => f.isSelected = false);
                    GUILayout.EndHorizontal();
                    EditorGUILayout.Space();

                    _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
                    foreach (ED ed in _overrideFields)
                    {
                        GUILayout.BeginHorizontal();
                        ed.isSelected = EditorGUILayout.Toggle(ed.isSelected, GUILayout.Width(20));
                        EditorGUILayout.LabelField(ed.field);
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(_exportInProgress ? "Export in progress..." : "Export", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT))) ExportOverrides();
                    break;

                case 4:
                    #if !UNITY_2021_2_OR_NEWER
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("The template export feature requires Unity 2021.2 or newer to work.", MessageType.Warning);
                    #else
                    if (_templates.Count > 0)
                    {
                        EditorGUI.BeginDisabledGroup(AI.Config.templateExportSettings.devMode);
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Template", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        if (_selectedTemplate >= _templates.Count) _selectedTemplate = 0; // in case template was deleted manually
                        EditorGUI.BeginChangeCheck();
                        _selectedTemplate = EditorGUILayout.Popup(_selectedTemplate, _templateNames);
                        if (EditorGUI.EndChangeCheck())
                        {
                            PrepareOverrides();
                        }
                        TemplateInfo curTemplate = _templates[_selectedTemplate];
                        if (ShowAdvanced())
                        {
                            if (GUILayout.Button(UIStyles.Content("New...", "Create a new empty template."), GUILayout.Width(60)))
                            {
                                NameUI nameUI = new NameUI();
                                nameUI.Init("My Template", CreateTemplate);
                                PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 0, 0), nameUI);
                            }
                            if (GUILayout.Button(UIStyles.Content("Copy...", "Creates a full independent copy of the original template including all files."), GUILayout.Width(60)))
                            {
                                NameUI nameUI = new NameUI();
                                nameUI.Init("My Template", CopyTemplate);
                                PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 0, 0), nameUI);
                            }
                            if (GUILayout.Button(UIStyles.Content("Extend...", "Creates a template extension referencing the original template where the original files will selectively be replaced by the ones in this template."), GUILayout.Width(60)))
                            {
                                NameUI nameUI = new NameUI();
                                nameUI.Init("My Template", ExtendTemplate);
                                PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 0, 0), nameUI);
                            }
                            EditorGUI.BeginDisabledGroup(curTemplate.readOnly);
                            if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Delete template"), GUILayout.Width(30)))
                            {
                                if (curTemplate.hasDescriptor) File.Delete(curTemplate.GetDescriptorPath());
                                File.Delete(curTemplate.path);
                                AssetDatabase.Refresh();
                                LoadTemplates();
                            }
                            EditorGUI.EndDisabledGroup();
                        }
                        GUILayout.EndHorizontal();
                        EditorGUI.EndDisabledGroup();

                        _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));

                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        AI.Config.templateExportSettings.environmentIndex = EditorGUILayout.Popup(AI.Config.templateExportSettings.environmentIndex, AI.Config.templateExportSettings.environments.Select(e => e.name).ToArray());
                        if (ShowAdvanced())
                        {
                            if (GUILayout.Button("New...", GUILayout.Width(60)))
                            {
                                NameUI nameUI = new NameUI();
                                nameUI.Init("My Config", name =>
                                {
                                    AI.Config.templateExportSettings.environments.Add(new TemplateExportEnvironment(name));
                                    AI.Config.templateExportSettings.environmentIndex = AI.Config.templateExportSettings.environments.Count - 1;
                                    AI.SaveConfig();
                                });
                                PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 0, 0), nameUI);
                            }
                            if (AI.Config.templateExportSettings.environments.Count > 1 && GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Delete configuration"), GUILayout.Width(30)))
                            {
                                AI.Config.templateExportSettings.environments.RemoveAt(AI.Config.templateExportSettings.environmentIndex);
                                AI.Config.templateExportSettings.environmentIndex--;
                                AI.SaveConfig();
                            }
                        }
                        GUILayout.EndHorizontal();

                        TemplateExportEnvironment env = AI.Config.templateExportSettings.environments[AI.Config.templateExportSettings.environmentIndex];

                        EditorGUI.BeginChangeCheck();
                        BeginIndentBlock();

                        if (curTemplate.fixedTargetFolder)
                        {
                            env.publishFolder = Path.GetDirectoryName(AI.GetPreviewFolder());
                            DrawFolder("Target Folder", env.publishFolder, null, null, labelWidth);
                        }
                        else
                        {
                            DrawFolder("Target Folder", env.publishFolder, null, newFolder =>
                            {
                                env.publishFolder = newFolder;
                                AI.SaveConfig();
                            }, labelWidth);
                        }

                        if (ShowAdvanced())
                        {
                            if (curTemplate.needsImagePath)
                            {
                                GUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Image Path", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                                env.imagePath = EditorGUILayout.TextField(env.imagePath);
                                GUILayout.EndHorizontal();
                            }

                            if (curTemplate.needsDataPath)
                            {
                                GUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Data Path", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                                env.dataPath = EditorGUILayout.TextField(env.dataPath);
                                GUILayout.EndHorizontal();

                                if (curTemplate.needsImagePath)
                                {
                                    GUILayout.BeginHorizontal();
                                    EditorGUILayout.LabelField(UIStyles.Content("Exclude Images", "Will not export images for the file search as that might make icons and textures available for download."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                                    env.excludeImages = EditorGUILayout.Toggle(env.excludeImages);
                                    GUILayout.EndHorizontal();
                                }
                            }
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(UIStyles.Content("Internal Ids Only", "Will name all package details file as package_[id].html. Otherwise if an asset is from or linked to the Asset Store, it will use package_f[foreignId].html to create more stable links."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                            env.internalIdsOnly = EditorGUILayout.Toggle(env.internalIdsOnly);
                            GUILayout.EndHorizontal();
                        }

                        EndIndentBlock();
                        if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

                        if (ShowAdvanced() || AI.Config.templateExportSettings.devMode)
                        {
                            EditorGUILayout.Space(20);
                            EditorGUI.BeginChangeCheck();
                            AI.Config.templateExportSettings.devMode = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.templateExportSettings.devMode, "Template Development Mode");
                            if (AI.Config.templateExportSettings.devMode)
                            {
                                EditorGUILayout.HelpBox("Development mode is now active and the export will use the settings below. This allows you to create and quickly iterate on your own templates. Close section to deactivate.", MessageType.Warning);
                                EditorGUILayout.Space();

                                DrawFolder("Dev Folder", AI.Config.templateExportSettings.devFolder, null, newFolder =>
                                {
                                    AI.Config.templateExportSettings.devFolder = newFolder;
                                    AI.SaveConfig();

                                    if (!string.IsNullOrWhiteSpace(newFolder))
                                    {
                                        if (IOUtils.IsDirectoryEmpty(newFolder))
                                        {
                                            IOUtils.ExtractArchive(curTemplate.path, newFolder);
                                        }
                                        else
                                        {
                                            EditorUtility.DisplayDialog("Folder not empty", "The development folder is not empty. The contents of the template was not automatically extracted there.", "OK");
                                        }
                                    }
                                }, labelWidth);

                                if (!string.IsNullOrWhiteSpace(AI.Config.templateExportSettings.devFolder))
                                {
                                    GUILayout.BeginHorizontal();
                                    GUILayout.Space(labelWidth + 6);

                                    if (GUILayout.Button(UIStyles.Content("Publish", "Compress development folder into a package of the same name and copy it into the templates folder."), GUILayout.ExpandWidth(false)))
                                    {
                                        PackageDevTemplate();
                                    }
                                    if (_watcher == null)
                                    {
                                        if (GUILayout.Button(UIStyles.Content("Start Directory Monitoring", "Will continuously monitor the development directory for changes and trigger automatic exports.")))
                                        {
                                            StartTemplateWatcher(AI.Config.templateExportSettings.devFolder);
                                        }
                                    }
                                    else
                                    {
                                        if (GUILayout.Button("Stop Directory Monitoring")) StopTemplateWatcher();
                                    }
                                    if (!string.IsNullOrWhiteSpace(curTemplate.inheritFrom))
                                    {
                                        if (GUILayout.Button("Override File...")) ShowOverrides();
                                    }

                                    GUILayout.EndHorizontal();
                                    EditorGUILayout.Space();
                                }

                                DrawFolder("Test Folder", AI.Config.templateExportSettings.testFolder, null, newFolder =>
                                {
                                    AI.Config.templateExportSettings.testFolder = newFolder;
                                    AI.SaveConfig();
                                }, labelWidth);

                                GUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Detail Pages", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                                AI.Config.templateExportSettings.maxDetailPages = EditorGUILayout.DelayedIntField(AI.Config.templateExportSettings.maxDetailPages, GUILayout.Width(50));
                                EditorGUILayout.LabelField("(0 = all)", EditorStyles.miniLabel);
                                GUILayout.EndHorizontal();

                                GUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Flags", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                                GUILayout.BeginVertical();
                                GUILayout.BeginHorizontal();
                                if (curTemplate.needsDataPath && !string.IsNullOrWhiteSpace(AI.Config.templateExportSettings.testFolder))
                                {
                                    AI.Config.templateExportSettings.preserveJson = EditorGUILayout.ToggleLeft(UIStyles.Content("Preserve Json", "Do not export data to Json but reuse already generated Json artifacts. Will be ignored if no Json exists yet."), AI.Config.templateExportSettings.preserveJson, GUILayout.Width(110));
                                }
                                if (!string.IsNullOrWhiteSpace(AI.Config.templateExportSettings.testFolder))
                                {
                                    AI.Config.templateExportSettings.publishResult = EditorGUILayout.ToggleLeft(UIStyles.Content("Publish Result", "Copy exported files from temporary to target directory."), AI.Config.templateExportSettings.publishResult, GUILayout.Width(110));
                                }
                                AI.Config.templateExportSettings.revealResult = EditorGUILayout.ToggleLeft(UIStyles.Content("Open " + (Application.platform == RuntimePlatform.OSXEditor ? "Finder" : "Explorer"), "Opens file browser once the export is done."), AI.Config.templateExportSettings.revealResult, GUILayout.Width(110));
                                GUILayout.EndHorizontal();
                                if (string.IsNullOrWhiteSpace(AI.Config.templateExportSettings.testFolder))
                                {
                                    EditorGUILayout.LabelField("Setting a test folder will unlock additional flags for more convenient development.", UIStyles.greyMiniLabel);
                                }
                                GUILayout.EndVertical();
                                GUILayout.EndHorizontal();

                                EditorGUILayout.Space();
                                GUILayout.BeginHorizontal();
                                if (curTemplate.hasDescriptor)
                                {
                                    if (GUILayout.Button("Open Descriptor"))
                                    {
                                        EditorUtility.RevealInFinder(curTemplate.GetDescriptorPath());
                                    }
                                }
                                else
                                {
                                    if (GUILayout.Button("Create Descriptor"))
                                    {
                                        string descriptor = curTemplate.GetDescriptorPath();
                                        File.WriteAllText(descriptor, JsonConvert.SerializeObject(curTemplate, Formatting.Indented));

                                        EditorUtility.DisplayDialog("Descriptor Created", $"Descriptor file '{descriptor}' has been created.", "OK");

                                        AssetDatabase.Refresh();
                                        LoadTemplates();
                                    }
                                }
                                if (GUILayout.Button(UIStyles.Content("Start Local Server", "Starts a Python server on http://localhost:8000/ to serve the template files.")))
                                {
#if UNITY_EDITOR_OSX
                                string command = "/usr/bin/python3";
#else
                                    string command = "python";
#endif
                                    IOUtils.ExecuteCommand(command, "-m http.server 8000", env.publishFolder, false, true);
                                    Application.OpenURL("http://localhost:8000" + (!string.IsNullOrWhiteSpace(curTemplate.entryPath) ? $"/{curTemplate.entryPath}" : ""));
                                }
                                GUILayout.EndHorizontal();
                            }
                            if (EditorGUI.EndChangeCheck())
                            {
                                if (!AI.Config.templateExportSettings.devMode && _watcher != null) StopTemplateWatcher();
                                AI.SaveConfig();
                                if (AI.Config.templateExportSettings.devMode && _watcher != null) _triggerExport = true;
                            }
                            EditorGUILayout.EndFoldoutHeaderGroup();
                        }
                        GUILayout.EndScrollView();
                        GUILayout.FlexibleSpace();
                        EditorGUI.BeginDisabledGroup(_watcher != null);
                        if (GUILayout.Button((_exportInProgress ? "Export in progress..." : "Export") + (_watcher != null ? " (automatically upon changes)" : ""), UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                        {
                            ExportTemplate();
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                    else
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.HelpBox("There are no templates available. Please create a template first and put it into the 'AssetInventory/Editor/Templates' folder. Normally there should be at least two default templates available, which might also hint to a broken installation.", MessageType.Warning);
                    }
                    #endif
                    break;
            }
            EditorGUI.EndDisabledGroup();
        }

#if UNITY_2021_2_OR_NEWER
        private async void PrepareOverrides()
        {
            _overridesFolder = IOUtils.CreateTempFolder(TEMP_FOLDER, true);
            await TemplateExport.ResolveInheritance(_templates[_selectedTemplate], _overridesFolder, _templates);
            _overrideCandidates = IOUtils.GetFiles(_overridesFolder, "", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.InvariantCultureIgnoreCase).ToList();
        }

        private void ShowOverrides()
        {
            GenericMenu menu = new GenericMenu();
            foreach (string file in _overrideCandidates)
            {
                string relPath = file.Substring(_overridesFolder.Length + 1);
                string target = Path.Combine(AI.Config.templateExportSettings.devFolder, relPath);
                if (File.Exists(target))
                {
                    menu.AddDisabledItem(new GUIContent(relPath));
                }
                else
                {
                    menu.AddItem(new GUIContent(relPath), false, () => OverrideFile(file));
                }
            }
            menu.ShowAsContext();
        }

        private void OverrideFile(string file)
        {
            string target = Path.Combine(AI.Config.templateExportSettings.devFolder, file.Substring(_overridesFolder.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }

        private void CreateTemplate(string newName)
        {
            string destination = Path.Combine(TemplateUtils.GetTemplateRootFolder(), $"{newName}.zip.bytes");

            if (File.Exists(destination))
            {
                EditorUtility.DisplayDialog("Error", "A template with that name already exists.", "OK");
                return;
            }

            // create zip
            IOUtils.CreateEmptyZip(destination);

            AssetDatabase.Refresh();
            LoadTemplates();
        }

        private void CopyTemplate(string newName)
        {
            string source = _templates[_selectedTemplate].path;
            string safeName = AssetUtils.GuessSafeName(newName).Replace(" ", "");
            string destination = Path.Combine(Path.GetDirectoryName(source), $"{safeName}.zip.bytes");
            if (File.Exists(destination))
            {
                EditorUtility.DisplayDialog("Error", "A template with that name already exists.", "OK");
                return;
            }
            File.Copy(source, destination);
            if (_templates[_selectedTemplate].hasDescriptor)
            {
                string descriptor = _templates[_selectedTemplate].GetDescriptorPath();
                string newDescriptor = Path.Combine(Path.GetDirectoryName(descriptor), $"{safeName}.json");
                File.Copy(descriptor, newDescriptor);

                // adjust descriptor
                TemplateInfo ti = JsonConvert.DeserializeObject<TemplateInfo>(File.ReadAllText(newDescriptor));
                ti.name = newName;
                ti.date = DateTime.Now;
                ti.version = 1;
                ti.readOnly = false;
                File.WriteAllText(newDescriptor, JsonConvert.SerializeObject(ti, Formatting.Indented));
            }

            AssetDatabase.Refresh();
            LoadTemplates();
        }

        private void ExtendTemplate(string newName)
        {
            string source = _templates[_selectedTemplate].path;
            string safeName = AssetUtils.GuessSafeName(newName).Replace(" ", "");
            string destination = Path.Combine(Path.GetDirectoryName(source), $"{safeName}.zip.bytes");
            string newDescriptor = Path.Combine(Path.GetDirectoryName(source), $"{safeName}.json");

            if (File.Exists(destination))
            {
                EditorUtility.DisplayDialog("Error", "A template with that name already exists.", "OK");
                return;
            }

            // create descriptor and copy from original
            TemplateInfo ti = new TemplateInfo();
            ti.name = newName;
            ti.inheritFrom = _templates[_selectedTemplate].GetNameFromFile();
            ti.needsDataPath = _templates[_selectedTemplate].needsDataPath;
            ti.needsImagePath = _templates[_selectedTemplate].needsImagePath;
            ti.fixedTargetFolder = _templates[_selectedTemplate].fixedTargetFolder;
            ti.entryPath = _templates[_selectedTemplate].entryPath;
            ti.parameters = _templates[_selectedTemplate].parameters;
            ti.readOnly = false;
            ti.isSample = false;
            ti.date = DateTime.Now;
            File.WriteAllText(newDescriptor, JsonConvert.SerializeObject(ti, Formatting.Indented));

            // create zip
            IOUtils.CreateEmptyZip(destination);

            AssetDatabase.Refresh();
            LoadTemplates();
        }

        private void PackageDevTemplate()
        {
            TemplateInfo ti = _templates[_selectedTemplate];
            string source = AI.Config.templateExportSettings.devFolder;
            string target = ti.path;

            IOUtils.CompressFolder(source, target);

            if (ti.hasDescriptor)
            {
                ti.date = DateTime.Now;
                File.WriteAllText(ti.GetDescriptorPath(), JsonConvert.SerializeObject(ti, Formatting.Indented));
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Template Export", $"Template '{ti.GetNameFromFile()}' has been exported to '{target}'.", "OK");
        }

        private void StopTemplateWatcher()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        private void StartTemplateWatcher(string path)
        {
            _watcher = new FileSystemWatcher();
            _watcher.Path = path;
            _watcher.IncludeSubdirectories = true;
            _watcher.Filter = "*.*";
            _watcher.InternalBufferSize = 65536;

            _watcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite;

            _watcher.Changed += OnChanged;
            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += (_, args) => { Debug.LogWarning($"Template dev folder monitoring error: {args.GetException()}"); };

            _watcher.EnableRaisingEvents = true;
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            Debug.Log($"Picking up template file rename: {e.OldFullPath} -> {e.FullPath}");
            _triggerExport = true;
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            Debug.Log($"Picking up template file delete: {e.FullPath}");
            _triggerExport = true;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            Debug.Log($"Picking up template file create: {e.FullPath}");
            _triggerExport = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            Debug.Log($"Picking up template file change: {e.FullPath}");
            _triggerExport = true;
        }

        private async void ExportTemplate()
        {
            if (_exportInProgress) return;
            _triggerExport = false;
            _exportInProgress = true;

            AI.AskForAffiliate();

            Debug.Log("Export");
            try
            {
                TemplateExportEnvironment env = AI.Config.templateExportSettings.environments[AI.Config.templateExportSettings.environmentIndex];

                // reload template info from disk to support easy template changes
                if (AI.Config.templateExportSettings.devMode) LoadTemplates();

                TemplateExport exporter = new TemplateExport();
                AI.Actions.RegisterRunningAction(ActionHandler.ACTION_SUB_PACKAGES_INDEX, exporter, "Indexing sub-packages");
                await exporter.Run(
                    _assets,
                    _templates[_selectedTemplate],
                    _templates,
                    AI.Config.templateExportSettings,
                    env
                );
                exporter.FinishProgress();
                if (AI.Config.templateExportSettings.revealResult) EditorUtility.RevealInFinder(env.publishFolder);
            }
            catch (Exception e)
            {
                Debug.LogError($"Exporting template failed: {e}");
            }
            _exportInProgress = false;
        }
#endif

        private async void ExportAssets()
        {
            string folder = EditorUtility.OpenFolderPanel("Select storage folder for exports", AI.Config.exportFolder2, "");
            if (string.IsNullOrEmpty(folder)) return;

            if (_clearTarget && Directory.Exists(folder)) await IOUtils.DeleteFileOrDirectory(folder);
            Directory.CreateDirectory(folder);

            AI.Config.exportFolder2 = Path.GetFullPath(folder);
            AI.SaveConfig();

            _exportInProgress = true;
            _curProgress = 0;
            _maxProgress = _packages.Count;

            foreach (AssetInfo info in _packages)
            {
                _curProgress++;
                await Task.Yield();

                if (!info.IsIndexed)
                {
                    Debug.LogError($"Skipping package '{info}' since it is not yet indexed.");
                    continue;
                }

                if (!info.IsDownloaded && !info.IsMaterialized)
                {
                    if (info.IsAbandoned)
                    {
                        Debug.LogWarning($"Package '{info}' is not locally available and also abandoned and cannot be downloaded anymore. Continuing with next package.");
                        continue;
                    }
                    if (!_autoDownload)
                    {
                        Debug.LogWarning($"Package '{info}' is not downloaded and cannot be exported. Continuing with next package.");
                        continue;
                    }
                    AI.GetObserver().Attach(info);
                    if (!info.PackageDownloader.IsDownloadSupported()) continue;

                    info.PackageDownloader.Download(true);
                    do
                    {
                        await Task.Yield();
                    } while (info.IsDownloading());
                    await Task.Delay(3000); // ensure all file operations have finished, can otherwise lead to issues
                    info.Refresh();
                    if (!info.IsDownloaded)
                    {
                        Debug.LogError($"Downloading '{info}' failed. Continuing with next package.");
                        continue;
                    }
                }

                string targetFolder = Path.Combine(folder, _flattenStructure ? "" : info.SafeName);
                Directory.CreateDirectory(targetFolder);

                // extract package
                string cachePath = AI.GetMaterializedAssetPath(info.ToAsset());
                bool existing = Directory.Exists(cachePath);

                // gather all indexed files
                IEnumerable<AssetFile> files;
                if (_fileMode)
                {
                    // files to export are already known
                    files = _assets.Where(a => a.AssetId == info.AssetId);
                }
                else
                {
                    files = DBAdapter.DB.Query<AssetFile>("SELECT * FROM AssetFile WHERE AssetId = ?", info.AssetId).ToList();
                }
                foreach (AssetFile af in files)
                {
                    if (!_fileMode)
                    {
                        bool include = false;
                        foreach (ED type in _exportTypes)
                        {
                            if (!type.isSelected) continue;
                            if (type.pointer != REMAINING_EXTENSIONS)
                            {
                                if (Enum.TryParse(type.pointer, out AI.AssetGroup group))
                                {
                                    if (AI.TypeGroups[group].Contains(af.Type)) include = true;
                                }
                            }
                            else
                            {
                                if (!_exportableExtensions.Contains(af.Type)) include = true;
                            }
                        }
                        if (!include) continue;
                    }

                    string targetFile = Path.Combine(targetFolder, _flattenStructure ? af.FileName : af.GetPath(true));
                    string targetMeta = targetFile + ".meta";
                    if (File.Exists(targetFile) && (!_metaFiles || File.Exists(targetMeta))) continue;

                    string sourceFile = await AI.EnsureMaterializedAsset(info.ToAsset(), af);
                    if (string.IsNullOrEmpty(sourceFile)) continue;

                    string targetDir = Directory.GetParent(targetFile)?.ToString();
                    if (targetDir == null) continue;

                    Directory.CreateDirectory(targetDir);
                    File.Copy(sourceFile, targetFile, true);

                    if (_metaFiles)
                    {
                        string sourceMeta = sourceFile + ".meta";
                        if (File.Exists(sourceMeta)) File.Copy(sourceMeta, targetMeta, true);
                    }
                }
                if (!existing) await IOUtils.DeleteFileOrDirectory(cachePath);
            }
            _exportInProgress = false;
            EditorUtility.RevealInFinder(folder);
        }

        private async void ExportOverrides()
        {
            _exportInProgress = true;
            _curProgress = 0;
            _maxProgress = _packages.Count;

            foreach (AssetInfo info in _packages)
            {
                _curProgress++;
                if (info.AssetSource != Asset.Source.CustomPackage && info.AssetSource != Asset.Source.Archive)
                {
                    Debug.LogWarning($"Skipping package '{info}' since it is not a custom package or archive.");
                    continue;
                }
                await Task.Yield();

                string targetFile = info.GetLocation(true) + ".overrides.json";
                if (!_overrideExisting && File.Exists(targetFile)) continue;

                PackageOverrides po = new PackageOverrides();
                foreach (ED field in _overrideFields.Where(f => f.isSelected))
                {
                    switch (field.field)
                    {
                        case "PackageTags":
                            po.tags = info.PackageTags.Select(pt => pt.Name).ToArray();
                            break;

                        default:
                            if (field.FieldInfo != null)
                            {
                                FieldInfo fi = typeof (PackageOverrides).GetField(field.field.ToLowercaseFirstLetter());
                                if (fi != null)
                                {
                                    fi.SetValue(po, field.FieldInfo.GetValue(info));
                                }
                                else
                                {
                                    Debug.LogError($"Override field '{field.field}' not found.");
                                }
                            }
                            else
                            {
                                Debug.LogError($"Override source field '{field.field}' not found.");
                            }
                            break;
                    }
                }

                File.WriteAllText(targetFile, JsonConvert.SerializeObject(po, new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Ignore
                }));
            }
            _exportInProgress = false;
        }

        private void ExportLicenses()
        {
            string file = EditorUtility.SaveFilePanel("Target file", AI.Config.exportFolder3, "ThirdParty", "md");
            if (string.IsNullOrEmpty(file)) return;

            _exportInProgress = true;

            AI.Config.exportFolder3 = Directory.GetParent(Path.GetFullPath(file))?.ToString();
            AI.SaveConfig();

            // TODO: switch to configurable templates
            List<string> result = new List<string>();
            result.Add("# Third Party Licenses");
            result.Add("");
            result.Add("The following third-party packages are included: ");
            result.Add("");

            List<AssetInfo> list = _assets.Where(a => !string.IsNullOrWhiteSpace(a.License))
                .GroupBy(a => a.GetDisplayName() + " - " + a.License)
                .Select(g => g.First())
                .OrderBy(a => a.GetDisplayName())
                .ToList();
            foreach (AssetInfo info in list)
            {
                result.Add($"## {info.GetDisplayName(true)}");
                result.Add("");
                result.Add(info.License);
                if (!string.IsNullOrWhiteSpace(info.LicenseLocation)) result.Add($"([Details]({info.LicenseLocation}))");
                result.Add("");
            }
            try
            {
                File.WriteAllLines(file, result);
                EditorUtility.RevealInFinder(file);
            }
            catch (Exception e)
            {
                Debug.LogError($"Exporting to file failed: {e}");
                EditorUtility.DisplayDialog("Export Failed", "License export failed. Most likely the target file is already opened in another application. See console for details.", "OK");
            }

            _exportInProgress = false;

        }

        private void ExportCSV()
        {
            string file = EditorUtility.SaveFilePanel("Target file", AI.Config.exportFolder, "assets", "csv");
            if (string.IsNullOrEmpty(file)) return;

            _exportInProgress = true;

            AI.Config.exportFolder = Directory.GetParent(Path.GetFullPath(file))?.ToString();
            AI.SaveConfig();

            List<string> result = new List<string>();

            if (_addHeader)
            {
                List<object> line = new List<object>();
                foreach (ED field in _exportFields.Where(f => f.isSelected))
                {
                    line.Add(field.field);
                }
                result.Add(string.Join(_separator, line));
            }

            foreach (AssetInfo info in _assets.Where(a => a.SafeName != Asset.NONE))
            {
                List<object> line = new List<object>();
                foreach (ED field in _exportFields.Where(f => f.isSelected))
                {
                    switch (field.field)
                    {
                        case "AssetLink":
                            line.Add(info.GetItemLink());
                            break;

                        case "PackageTags":
                            line.Add(string.Join(",", info.PackageTags.Select(pt => pt.Name)));
                            break;

                        default:
                            if (field.FieldInfo != null)
                            {
                                line.Add(field.FieldInfo.GetValue(info));
                            }
                            else
                            {
                                Debug.LogError($"Export field '{field.field}' not found.");
                            }
                            break;
                    }

                    // make sure delimiter and line breaks are not used 
                    if (line.Last() is string s)
                    {
                        line[line.Count - 1] = s.Replace(_separator, ",").Replace("\n", string.Empty).Replace("\r", string.Empty);
                    }
                }
                result.Add(string.Join(_separator, line));
            }
            try
            {
                File.WriteAllLines(file, result);
                EditorUtility.RevealInFinder(file);
            }
            catch (Exception e)
            {
                Debug.LogError($"Exporting to file failed: {e}");
                EditorUtility.DisplayDialog("Export Failed", "CSV export failed. Most likely the target file is already opened in another application. See console for details.", "OK");
            }

            _exportInProgress = false;
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }

    /// <summary>
    /// Class to hold export type information for the wizard UI
    /// </summary>
    public class ExportTypeInfo
    {
        public int Index { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public Texture Icon { get; private set; }

        public ExportTypeInfo(int index, string name, string description, Texture icon)
        {
            Index = index;
            Name = name;
            Description = description;
            Icon = icon;
        }
    }

    [Serializable]
    public sealed class ED
    {
        public string pointer;
        public bool isDefault;
        public bool isVisibleColumn;
        public bool isSelected;

        public string table;
        public string field;

        public PropertyInfo FieldInfo
        {
            get
            {
                if (field == null) return null;
                if (_fieldInfo == null) _fieldInfo = typeof (AssetInfo).GetProperty(field);
                return _fieldInfo;
            }
        }

        private PropertyInfo _fieldInfo;

        public ED(string pointer, bool isDefault = true, bool isVisibleColumn = false)
        {
            this.isDefault = isDefault;
            this.isVisibleColumn = isVisibleColumn;
            this.pointer = pointer;

            isSelected = isDefault;

            if (pointer.IndexOf('/') >= 0)
            {
                table = pointer.Split('/')[0];
                field = pointer.Split('/')[1];
            }
        }
    }
}