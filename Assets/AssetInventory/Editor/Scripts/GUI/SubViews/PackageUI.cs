using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class PackageUI : BasicEditorUI
    {
        private enum Mode
        {
            NewLocation,
            New,
            Edit
        }

        private Mode _mode;
        private Vector2 _scrollPos;
        private Vector2 _scrollDescr;
        private AssetInfo _info;
        private Asset _asset;
        private Action<Asset> _onSave;

        private string _newLocation = "https://github.com/WetzoldStudios/traVRsal-sdk.git";
        private int _gitRefSource;
        private int _gitBranchIdx;
        private int _gitTagIdx;
        private int _gitPRIdx;
        private string _gitCommit;
        private string[] _gitRefSourceOptions;
        private GitHandler _git;
        private bool _initDone;

        public static PackageUI ShowWindow()
        {
            PackageUI window = GetWindow<PackageUI>("Package Data");
            window.minSize = new Vector2(400, 500);

            return window;
        }

        public void Init(AssetInfo info, Action<Asset> onSave)
        {
            _info = info;
            _mode = info == null || info.Id == 0 ? Mode.NewLocation : Mode.Edit;

            if (_mode == Mode.Edit)
            {
                _asset = DBAdapter.DB.Find<Asset>(_info.AssetId); // load fresh from DB and store that exact copy later again
                _asset.PreviewTexture = _info.PreviewTexture;
                if (_asset.PreviewTexture == null)
                {
                    // create grey texture
                    _asset.PreviewTexture = new Texture2D(100, 100);
                    _asset.PreviewTexture.SetPixel(0, 0, Color.grey);
                    _asset.PreviewTexture.Apply();
                }
            }
            else
            {
                if (_info == null)
                {
                    _info = new AssetInfo();
                    _info.AssetSource = Asset.Source.CustomPackage;
                }
                _asset = _info.ToAsset();
            }
            _onSave = onSave;
        }

        private void InitUI()
        {
            _initDone = true;
            _gitRefSourceOptions = new[] {"HEAD", "Branch", "Tag", "Pull Request", "Commit"};
        }

        public override void OnGUI()
        {
            if (!_initDone) InitUI();
            if (_mode == Mode.Edit && string.IsNullOrEmpty(_asset?.Location)) // can happen after domain reload
            {
                Close();
                return;
            }
            int labelWidth = 90;

            if (_mode == Mode.NewLocation)
            {
                EditorGUILayout.HelpBox("Step 1: Enter the Url of the Git repository.", MessageType.Info);
                EditorGUILayout.Space();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Location", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                _newLocation = EditorGUILayout.TextField(_newLocation);
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_newLocation));
                if (GUILayout.Button("Next", GUILayout.ExpandWidth(false)))
                {
                    _git = new GitHandler(_newLocation);
                    _git.GatherRemoteInfo();
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();

                if (_git != null)
                {
                    EditorGUILayout.Space();
                    if (!_git.IsValid)
                    {
                        EditorGUILayout.HelpBox($"Connection error: {_git.LastError}", MessageType.Error);
                        return;
                    }

                    EditorGUILayout.HelpBox("Step 2: Select the correct reference.", MessageType.Info);
                    EditorGUILayout.Space();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Reference", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                    _gitRefSource = EditorGUILayout.Popup(_gitRefSource, _gitRefSourceOptions);
                    GUILayout.EndHorizontal();

                    switch (_gitRefSource)
                    {
                        case 1:
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Branch", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                            _gitBranchIdx = EditorGUILayout.Popup(_gitBranchIdx, _git.ShortBranches);
                            GUILayout.EndHorizontal();
                            break;

                        case 2:
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Tag", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                            _gitTagIdx = EditorGUILayout.Popup(_gitTagIdx, _git.ShortTags);
                            GUILayout.EndHorizontal();
                            break;

                        case 3:
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Pull Request", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                            _gitTagIdx = EditorGUILayout.Popup(_gitTagIdx, _git.ShortPRs);
                            GUILayout.EndHorizontal();
                            break;

                        case 4:
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Commit Id", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                            _gitCommit = EditorGUILayout.TextField(_gitCommit);
                            GUILayout.EndHorizontal();
                            break;
                    }
                }
                return;
            }

            labelWidth = 130;
            EditorGUILayout.HelpBox("Change the values below to update the package data. The technical names are mandatory if you want filters or selection dropdowns to work properly.", MessageType.Info);
            EditorGUILayout.Space();

            GUILabelWithTextNoMax("Type", StringUtils.CamelCaseToWords(_asset.AssetSource.ToString()) + (_asset.AssetSource == Asset.Source.RegistryPackage ? $" ({_asset.PackageSource})" : ""), labelWidth);

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Location", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            EditorGUILayout.LabelField(_asset.Location, EditorStyles.wordWrappedLabel);

            switch (_mode)
            {
                case Mode.New:
                    break;

                case Mode.Edit:
                    GUILayout.FlexibleSpace();

                    GUILayout.BeginVertical();
                    int tWidth = 65;
                    GUILayout.Box(_asset.PreviewTexture, EditorStyles.centeredGreyMiniLabel, GUILayout.MaxWidth(tWidth), GUILayout.MaxHeight(tWidth));
                    if (GUILayout.Button("Change...", GUILayout.MaxWidth(tWidth))) ChangePreview();
                    EditorGUILayout.Space();
                    GUILayout.EndVertical();
                    EditorGUILayout.Space();
                    break;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UIStyles.Content("Name", "Overrides the technical name"), EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.DisplayName = EditorGUILayout.TextField(_asset.DisplayName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Technical Name", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            EditorGUI.BeginDisabledGroup(true);
            _asset.SafeName = EditorGUILayout.TextField(_asset.SafeName);
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UIStyles.Content("Publisher", "Overrides the technical publisher name"), EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.DisplayPublisher = EditorGUILayout.TextField(_asset.DisplayPublisher);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Technical Publisher", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.SafePublisher = EditorGUILayout.TextField(_asset.SafePublisher);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UIStyles.Content("Category", "Overrides the technical category name"), EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.DisplayCategory = EditorGUILayout.TextField(_asset.DisplayCategory);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Technical Category", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.SafeCategory = EditorGUILayout.TextField(_asset.SafeCategory);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Version", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.Version = EditorGUILayout.TextField(_asset.Version);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Unity Versions", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.SupportedUnityVersions = EditorGUILayout.TextField(_asset.SupportedUnityVersions);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Render Pipelines", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.BIRPCompatible = EditorGUILayout.ToggleLeft("BIRP", _asset.BIRPCompatible, GUILayout.Width(60));
            _asset.URPCompatible = EditorGUILayout.ToggleLeft("URP", _asset.URPCompatible, GUILayout.Width(60));
            _asset.HDRPCompatible = EditorGUILayout.ToggleLeft("HDRP", _asset.HDRPCompatible, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("License", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.License = EditorGUILayout.TextField(_asset.License);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("License Location", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.LicenseLocation = EditorGUILayout.TextField(_asset.LicenseLocation);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Price EUR", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.PriceEur = EditorGUILayout.FloatField(_asset.PriceEur);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Price USD", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.PriceUsd = EditorGUILayout.FloatField(_asset.PriceUsd);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Price CNY", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _asset.PriceCny = EditorGUILayout.FloatField(_asset.PriceCny);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Description", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _scrollDescr = EditorGUILayout.BeginScrollView(_scrollDescr, GUILayout.ExpandHeight(true));
            _asset.Description = EditorGUILayout.TextArea(_asset.Description, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_mode == Mode.New ? "Create" : "Save", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                SaveData();
                Close();
            }
        }

        private void ChangePreview()
        {
            string assetPreviewFile = EditorUtility.OpenFilePanel("Select image", "", "png");
            if (string.IsNullOrEmpty(assetPreviewFile)) return;

            try
            {
                // load immediately
                byte[] fileData = File.ReadAllBytes(assetPreviewFile);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);

                // copy file
                string targetDir = Path.Combine(AI.GetPreviewFolder(), _asset.Id.ToString());
                string targetFile = Path.Combine(targetDir, "a-" + _asset.Id + Path.GetExtension(assetPreviewFile));
                Directory.CreateDirectory(targetDir);
                File.Copy(assetPreviewFile, targetFile, true);
                AssetUtils.RemoveFromPreviewCache(targetFile);

                // set once all critical parts are done
                _asset.PreviewTexture = tex;
                _info.PreviewTexture = tex;
            }
            catch (Exception e)
            {
                Debug.LogError("Error loading image: " + e.Message);
            }
        }

        private void SaveData()
        {
            if (string.IsNullOrWhiteSpace(_asset.DisplayName) && string.IsNullOrWhiteSpace(_asset.SafeName))
            {
                EditorUtility.DisplayDialog("Error", "Either name or technical name must be set.", "OK");
                return;
            }
            if ((_asset.SafeCategory != null && _asset.SafeCategory.Contains("/"))
                || (_asset.SafeName != null && _asset.SafeName.Contains("/"))
                || (_asset.SafePublisher != null && _asset.SafePublisher.Contains("/"))
               )
            {
                EditorUtility.DisplayDialog("Error", "Safe items must not contain any forward slashes.", "OK");
                return;
            }

            DBAdapter.DB.Update(_asset);

            _onSave?.Invoke(_asset);
        }
    }
}