using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class PreviewWizardUI : BasicEditorUI
    {
        private const string BASE_JOIN = "inner join Asset on Asset.Id = AssetFile.AssetId where Asset.Exclude = false";

        [Serializable]
        private class TypeCount
        {
            public int Count { get; set; }
            public string Type { get; set; }
        }

        private Vector2 _scrollPos;
        private List<AssetInfo> _assets;
        private List<AssetInfo> _allAssets;
        private List<AssetInfo> _allFiles;
        private int _totalFiles;
        private int _providedFiles;
        private int _originalFiles;
        private int _recreatedFiles;
        private int _erroneousFiles;
        private int _missingFiles;
        private int _noPrevFiles;
        private int _scheduledFiles;
        private int _imageFiles;
        private bool _showAdv;
        private bool _showTypeBreakdown;
        private List<TypeCount> _typeBreakdown;
        private PreviewPipeline _previewPipeline;
        private readonly IncorrectPreviewsValidator _validator = new IncorrectPreviewsValidator();

        public static PreviewWizardUI ShowWindow()
        {
            PreviewWizardUI window = GetWindow<PreviewWizardUI>("Previews Wizard");
            window.minSize = new Vector2(460, 300);
            window.maxSize = new Vector2(window.minSize.x, 1500);

            return window;
        }

        private void OnEnable()
        {
            if (_allAssets == null || _allAssets.Count == 0) _allAssets = AI.LoadAssets();
        }

        public void Init(List<AssetInfo> assets = null, List<AssetInfo> allAssets = null)
        {
            _assets = assets;
            _allAssets = allAssets;

            GeneratePreviewOverview();
        }

        private void GeneratePreviewOverview()
        {
            string assetFilter = PreviewPipeline.GetAssetFilter(_assets);
            string countQuery = "select count(*) from AssetFile";

            _totalFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} {assetFilter}");
            _imageFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} {assetFilter} and AssetFile.Type in ('" + string.Join("','", AI.TypeGroups[AI.AssetGroup.Images]) + "')");
            _providedFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.Provided);
            _originalFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.UseOriginal);
            _recreatedFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.Custom);
            _erroneousFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.Error);
            _missingFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.None);
            _noPrevFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.NotApplicable);
            _scheduledFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and (AssetFile.PreviewState = ? or AssetFile.PreviewState = ?) {assetFilter}", AssetFile.PreviewOptions.Redo, AssetFile.PreviewOptions.RedoMissing);

            // Get type breakdown for scheduled files
            string typeBreakdownQuery = $"select count(*) as Count, Type from AssetFile af left join Asset on Asset.Id = af.AssetId where (PreviewState = ? or PreviewState = ?) {assetFilter} group by Type";
            _typeBreakdown = DBAdapter.DB.Query<TypeCount>(typeBreakdownQuery, AssetFile.PreviewOptions.Redo, AssetFile.PreviewOptions.RedoMissing).ToList();
        }

        private void Schedule(AssetFile.PreviewOptions state)
        {
            string assetFilter = PreviewPipeline.GetAssetFilter(_assets);
            string query = $"update AssetFile set PreviewState = ? from (select * from Asset where Exclude = false) as Asset where Asset.Id = AssetFile.AssetId and AssetFile.PreviewState = ? {assetFilter}";
            DBAdapter.DB.Execute(query, (state == AssetFile.PreviewOptions.Custom || state == AssetFile.PreviewOptions.Provided || state == AssetFile.PreviewOptions.Redo) ? AssetFile.PreviewOptions.Redo : AssetFile.PreviewOptions.RedoMissing, state);

            GeneratePreviewOverview();
        }

        private void Schedule(string queryExt = "")
        {
            string assetFilter = PreviewPipeline.GetAssetFilter(_assets);

            string query = $"update AssetFile set PreviewState = ? from (select * from Asset where Exclude = false) as Asset where Asset.Id = AssetFile.AssetId and PreviewState in (1,2,3) {queryExt} {assetFilter}";
            DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.Redo);

            query = $"update AssetFile set PreviewState = ? from (select * from Asset where Exclude = false) as Asset where Asset.Id = AssetFile.AssetId and PreviewState not in (1,2,3) {queryExt} {assetFilter}";
            DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.RedoMissing);

            GeneratePreviewOverview();
        }

        public override void OnGUI()
        {
            int labelWidth = 120;
            int buttonWidth = 70;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("This wizard will help you recreate preview images in case they are missing or incorrect.", EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical();
            if (GUILayout.Button("?", GUILayout.Width(20)))
            {
                EditorUtility.DisplayDialog("Preview Images Overview", "When indexing Unity packages, preview images are typically bundled with them. These are often good but not always. This can result in empty previews, pink images, dark images and more. Colors and lighting will also differ between Unity versions where the previews were initially created. Audio files will for example have different shades of yellow. Bundled preview images are limited to 128 by 128 pixels.\n\nAsset Inventory can easily recreate preview images and offers advanced options like creating bigger previews.", "OK");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("Current Selection", EditorStyles.largeLabel);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
            EditorGUILayout.LabelField(_assets != null && _assets.Count > 0 ? (_assets.Count + (_assets.Count == 1 ? $" ({_assets[0].GetDisplayName()})" : "")) : "-Full Database-", EditorStyles.wordWrappedLabel);
            if (_assets != null && _assets.Count > 0 && GUILayout.Button(UIStyles.Content("x", "Clear Selection"), GUILayout.Width(20)))
            {
                _assets = null;
                GeneratePreviewOverview();
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            GUILabelWithText("Total Files", $"{_totalFiles:N0}", labelWidth);
            EditorGUI.BeginDisabledGroup(_totalFiles == 0);
            if (GUILayout.Button("Schedule Recreation", GUILayout.ExpandWidth(false)))
            {
                if (EditorUtility.DisplayDialog("Confirm", $"Are you sure you want to schedule recreation for all {_totalFiles:N0} files? This will replace all existing previews.", "Continue", "Cancel"))
                {
                    Schedule();
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            DrawPreviewStateRow("Pre-Provided", _providedFiles, labelWidth, AssetFile.PreviewOptions.Provided, "Preview images that were provided with the package.");
            DrawPreviewStateRow("Recreated", _recreatedFiles, labelWidth, AssetFile.PreviewOptions.Custom, "Preview images that were recreated by Asset Inventory.");
            DrawPreviewStateRow("Missing", _missingFiles, labelWidth, AssetFile.PreviewOptions.None, "File that do not have a preview image yet but should have one.");
            DrawPreviewStateRow("Erroneous", _erroneousFiles, labelWidth, AssetFile.PreviewOptions.Error, "Preview images where a previous recreation attempt failed.");
            DrawPreviewStateRow("Not Applicable", _noPrevFiles, labelWidth, AssetFile.PreviewOptions.NotApplicable, "Files for which typically no previews are created, e.g. documents, scripts, controllers. Only a generic icon will be shown.", advancedOnly: true);
            DrawPreviewStateRow("Using Original", _originalFiles, labelWidth, AssetFile.PreviewOptions.UseOriginal, "Image files that are used directly as previews since they are small and don't need recreation.", advancedOnly: true, additionalDisableCondition: AI.Config.directMediaPreviews);

            EditorGUILayout.BeginHorizontal();
            GUILabelWithText("Image Files", $"{_imageFiles:N0}", labelWidth);
            EditorGUI.BeginDisabledGroup(_imageFiles == 0);
            if (GUILayout.Button("Schedule Recreation", GUILayout.ExpandWidth(false)))
            {
                Schedule("and AssetFile.Type in ('" + string.Join("','", AI.TypeGroups[AI.AssetGroup.Images]) + "')");
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            GUILabelWithText("Scheduled", $"{_scheduledFiles:N0}", labelWidth);

            EditorGUILayout.Space();
            UIStyles.DrawUILine(Color.grey, 1, 5);
            _showTypeBreakdown = EditorGUILayout.BeginFoldoutHeaderGroup(_showTypeBreakdown, "Scheduled by Type");
            if (_showTypeBreakdown)
            {
                if (_typeBreakdown != null && _typeBreakdown.Count > 0)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.Width(200));
                    EditorGUILayout.LabelField("Count", EditorStyles.boldLabel, GUILayout.Width(100));
                    EditorGUILayout.EndHorizontal();

                    foreach (var item in _typeBreakdown.OrderByDescending(t => t.Count))
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{UIStyles.INDENT}{item.Type}", GUILayout.Width(200));
                        EditorGUILayout.LabelField($"{item.Count:N0}", GUILayout.Width(100));

                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Remove this type from queue"), GUILayout.Width(25), GUILayout.Height(18)))
                        {
                            ClearQueue(item.Type);
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.LabelField($"{UIStyles.INDENT}No files scheduled for recreation.", EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space();
            _showAdv = EditorGUILayout.BeginFoldoutHeaderGroup(_showAdv, "Advanced");
            if (_showAdv)
            {
                if (GUILayout.Button("Show Preview Folder", GUILayout.Width(200)))
                {
                    string path = AI.GetPreviewFolder();
                    if (_assets != null && _assets.Count == 1)
                    {
                        path = IOUtils.ToShortPath(_assets[0].GetPreviewFolder(AI.GetPreviewFolder()));
                    }
                    EditorUtility.RevealInFinder(path);
                }
                EditorGUI.BeginDisabledGroup(AI.Actions.ActionsInProgress);
                if (GUILayout.Button(UIStyles.Content("Revert to Provided", "Will replace existing recreated previews with those provided originally within the packages."), GUILayout.Width(200)))
                {
                    RestorePreviews();
                }
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(UIStyles.Content("Clean Queue", "Will remove accidentally scheduled items for which no preview can be created (e.g. cs files)."), GUILayout.Width(200)))
                {
                    CleanQueue();
                }
                if (GUILayout.Button(UIStyles.Content("Clear Queue", "Will remove the scheduled items from the queue again."), GUILayout.Width(200)))
                {
                    ClearQueue();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            if (_validator.IsRunning)
            {
                EditorGUILayout.BeginHorizontal();
                UIStyles.DrawProgressBar((float)_validator.Progress / _validator.MaxProgress, $"Progress: {_validator.Progress}/{_validator.MaxProgress}");
                if (GUILayout.Button("Cancel", GUILayout.ExpandWidth(false), GUILayout.Height(14)))
                {
                    _validator.CancellationRequested = true;
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (_previewPipeline != null && _previewPipeline.IsRunning())
            {
                EditorGUILayout.BeginHorizontal();
                string text = _assets == null || _assets.Count > 1 ? $"Progress: {_previewPipeline.MainProgress}/{_previewPipeline.MainCount} packages" : $"Progress: {_previewPipeline.SubProgress}/{_previewPipeline.SubCount}";
                UIStyles.DrawProgressBar((float)_previewPipeline.SubProgress / _previewPipeline.SubCount, text);
                EditorGUI.BeginDisabledGroup(_previewPipeline.CancellationRequested);
                if (GUILayout.Button("Cancel", GUILayout.ExpandWidth(false), GUILayout.Height(14)))
                {
                    _previewPipeline.CancellationRequested = true;
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(_scheduledFiles == 0);
                if (GUILayout.Button($"Recreate {_scheduledFiles:N0} Scheduled", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                {
                    _ = RecreatePreviews();
                }
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button(UIStyles.Content("Verify", "Inspect all preview images and check for issues like containing Unity default placeholders or shader errors."), GUILayout.Width(buttonWidth), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT))) InspectPreviews();
                if (GUILayout.Button("Refresh", GUILayout.Width(buttonWidth), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT))) GeneratePreviewOverview();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void CleanQueue()
        {
            List<string> types = new List<string>();
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Audio]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Fonts]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Images]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Materials]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Models]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Prefabs]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Videos]);
            string previewTypes = "'" + string.Join("','", types) + "'";

            string assetFilter = PreviewPipeline.GetAssetFilter(_assets, "AssetId");
            string query = $@"
                UPDATE AssetFile
                SET PreviewState = ?
                WHERE 
                  (PreviewState = ? or PreviewState = ?)
                  AND (
                      Type NOT IN ({previewTypes})
                  )
                  AND AssetId IN (
                      SELECT Id FROM Asset WHERE Exclude = 0
                  )
                  {assetFilter};
                ";
            DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.NotApplicable, AssetFile.PreviewOptions.Redo, AssetFile.PreviewOptions.RedoMissing);

            GeneratePreviewOverview();
        }

        private void ClearQueue(string type = null)
        {
            string confirmMessage = type != null
                ? $"Are you sure you want to remove all '{type}' files from the preview recreation queue?"
                : "Are you sure you want to clear the preview recreation queue? The previous state of items is not always known (except for missing). This might result in items being marked as recreated instead of pre-provided. That is usually not an issue though.";

            if (!EditorUtility.DisplayDialog("Confirm", confirmMessage, "Continue", "Cancel")) return;

            string assetFilter = PreviewPipeline.GetAssetFilter(_assets, "AssetId");
            string typeFilter = type != null ? "AND Type = ?" : "";

            string query = $@"
                UPDATE AssetFile
                SET PreviewState = ?
                WHERE 
                  PreviewState = ?
                  {typeFilter}
                  AND AssetId IN (
                      SELECT Id FROM Asset WHERE Exclude = 0
                  )
                  {assetFilter};
                ";

            if (type != null)
            {
                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.None, AssetFile.PreviewOptions.RedoMissing, type);
                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.Custom, AssetFile.PreviewOptions.Redo, type);
            }
            else
            {
                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.None, AssetFile.PreviewOptions.RedoMissing);
                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.Custom, AssetFile.PreviewOptions.Redo);
            }

            GeneratePreviewOverview();
        }

        private async void InspectPreviews()
        {
            if (_validator.CurrentState == Validator.State.Scanning || _validator.CurrentState == Validator.State.Fixing) return;

            string query = "select * from AssetFile where (PreviewState = ? or PreviewState = ?)";
            if (_assets != null && _assets.Count > 0) query += " and AssetId in (" + string.Join(", ", _assets.Select(a => a.AssetId)) + ")";
            List<AssetInfo> files = DBAdapter.DB.Query<AssetInfo>(query, AssetFile.PreviewOptions.Provided, AssetFile.PreviewOptions.Custom).ToList();

            _validator.CancellationRequested = false;
            await _validator.Validate(files);
            if (_validator.DBIssues.Count > 0)
            {
                int defaultCount = _validator.DBIssues.Count(f => f.URPCompatible);
                int errorCount = _validator.DBIssues.Count(f => !f.URPCompatible);
                string message = $"Found {_validator.DBIssues.Count:N0} issues with preview images.\n\nDefault previews: {defaultCount:N0} (Mark for recreation)\nShader errors: {errorCount:N0} (Mark as error)\n\nDo you want to proceed?";
                if (EditorUtility.DisplayDialog("Preview Issues Found", message, "Yes", "No"))
                {
                    await _validator.Fix();
                    AI.TriggerPackageRefresh();
                    GeneratePreviewOverview();
                }
            }
            else
            {
                string msg = "All preview images appear correct.";
                if (_scheduledFiles > 0) msg += $" {_scheduledFiles:N0} files already scheduled for recreation.";
                EditorUtility.DisplayDialog("No Issues Found", msg, "OK");
            }
        }

        private async void RestorePreviews()
        {
            _previewPipeline = new PreviewPipeline();
            AI.Actions.RegisterRunningAction(ActionHandler.ACTION_PREVIEWS_RESTORE, _previewPipeline, "Restoring previews");
            int restored = await _previewPipeline.RestorePreviews(_assets, _allAssets);
            _previewPipeline.FinishProgress();

            Debug.Log($"Previews restored: {restored}");

            AI.TriggerPackageRefresh();
            GeneratePreviewOverview();
        }

        private async Task RecreatePreviews()
        {
            CleanQueue();

            _previewPipeline = new PreviewPipeline();
            AI.Actions.RegisterRunningAction(ActionHandler.ACTION_PREVIEWS_RECREATE, _previewPipeline, "Recreating previews");
            int created = await _previewPipeline.RecreateScheduledPreviews(_assets, _allAssets);
            _previewPipeline.FinishProgress();

            Debug.Log($"Preview recreation done: {created} created.");

            AI.TriggerPackageRefresh();
            GeneratePreviewOverview();
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OpenSearchWithFilter(AssetFile.PreviewOptions previewState)
        {
            // Get the IndexUI window (assuming it's already open)
            IndexUI indexWindow = GetWindow<IndexUI>(null, false);
            if (indexWindow == null) return;

            // Build search phrase based on preview state
            string searchPhrase = $"=AssetFile.PreviewState={(int)previewState}";

            // If _assets is null or empty, pass null to search all packages
            // Otherwise, pass the first asset to filter by that package
            AssetInfo filterAsset = (_assets != null && _assets.Count > 0) ? _assets[0] : null;

            indexWindow.OpenInSearch(filterAsset, force: true, showFilterTab: true, searchPhrase: searchPhrase);
            indexWindow.Focus();
        }

        private void DrawPreviewStateRow(string label, int count, int labelWidth, AssetFile.PreviewOptions previewState, string tooltip, bool advancedOnly = false, bool additionalDisableCondition = false)
        {
            EditorGUILayout.BeginHorizontal();
            GUILabelWithText($"{UIStyles.INDENT}{label}", $"{count:N0}", labelWidth, tooltip);

            if (!advancedOnly || ShowAdvanced())
            {
                if (GUILayout.Button(EditorGUIUtility.IconContent("Search Icon", "|Show in Search"), GUILayout.Width(25), GUILayout.Height(18)))
                {
                    OpenSearchWithFilter(previewState);
                }
            }

            EditorGUI.BeginDisabledGroup(count == 0 || additionalDisableCondition);
            if (!advancedOnly || ShowAdvanced())
            {
                if (GUILayout.Button("Schedule Recreation", GUILayout.ExpandWidth(false)))
                {
                    bool shouldSchedule = true;

                    if (previewState == AssetFile.PreviewOptions.Error)
                    {
                        shouldSchedule = EditorUtility.DisplayDialog("Confirm", $"Are you sure you want to schedule recreation for {count:N0} erroneous files? These files had previous recreation errors, probably due to shader errors.", "Continue", "Cancel");
                    }
                    else if (previewState == AssetFile.PreviewOptions.NotApplicable)
                    {
                        shouldSchedule = EditorUtility.DisplayDialog("Confirm", $"Are you sure you want to schedule recreation for {count:N0} files marked as not applicable? These files typically don't have previews (e.g., scripts, documents).", "Continue", "Cancel");
                    }

                    if (shouldSchedule)
                    {
                        Schedule(previewState);
                    }
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }
    }
}