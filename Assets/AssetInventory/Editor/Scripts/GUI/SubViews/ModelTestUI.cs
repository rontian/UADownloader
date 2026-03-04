#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OllamaSharp.Models;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class ModelTestUI : BasicEditorUI
    {
        private const float MODEL_COLUMN_WIDTH = 150f;
        private const float IMAGE_COLUMN_WIDTH = 200f;
        private const float ROW_HEIGHT = 120f;
        private const float DISABLED_ROW_HEIGHT = 40f;
        private const float PREVIEW_SIZE = 80f;
        private const string BLIP_BACKEND = "[BLIP]";

        private List<Model> _models;
        private List<TestImage> _testImages;
        private Dictionary<string, bool> _modelEnabled;
        private Dictionary<string, bool> _imageEnabled;
        private Dictionary<(string model, string image), string> _results;
        private Dictionary<(string model, string image), float> _cellTimes;
        private Dictionary<string, float> _modelTotalTimes;
        private Vector2 _scroll;
        private bool _showPrompt;
        private bool _isRunning;
        private CancellationTokenSource _cts;

        public static ModelTestUI ShowWindow()
        {
            ModelTestUI window = GetWindow<ModelTestUI>("AI Model Tester");
            window.minSize = new Vector2(800, 600);
            window.Init();

            return window;
        }

        private void Init()
        {
            _testImages = GetTestImages() ?? new List<TestImage>();
            _models = Intelligence.OllamaModels?
                .OrderBy(m => m.Name, StringComparer.InvariantCultureIgnoreCase)
                .ToList() ?? new List<Model>();
            if (_models.Any()) _models.Add(new Model {Name = BLIP_BACKEND});

            _modelEnabled = _models.ToDictionary(m => m.Name, m => m.Name != BLIP_BACKEND);
            _imageEnabled = _testImages.ToDictionary(t => t.path, _ => true);
            _results = new Dictionary<(string, string), string>();
            _cellTimes = new Dictionary<(string, string), float>();
            _modelTotalTimes = new Dictionary<string, float>();
            _scroll = Vector2.zero;
            _isRunning = false;
            _cts = null;
        }

        private void OnEnable() => Init();

        public override void OnGUI()
        {
            if (_models == null || _testImages == null || !_models.Any() || !_testImages.Any())
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Data not yet ready", MessageType.Info);
                if (GUILayout.Button("Reload Data", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT))) Init();
                return;
            }

            // Calculate total content width
            float contentWidth = MODEL_COLUMN_WIDTH + _testImages.Count * IMAGE_COLUMN_WIDTH;

            // Scrollable area
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Header row
            EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth), GUILayout.Height(ROW_HEIGHT));
            GUILayout.Space(MODEL_COLUMN_WIDTH);
            foreach (TestImage img in _testImages)
            {
                GUILayout.Space(6);
                EditorGUILayout.BeginVertical(GUILayout.Width(IMAGE_COLUMN_WIDTH), GUILayout.Height(ROW_HEIGHT));
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                _imageEnabled[img.path] = EditorGUILayout.Toggle(_imageEnabled[img.path], GUILayout.Width(20));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                if (img.texture != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Box(img.texture, UIStyles.centerLabel, GUILayout.Width(PREVIEW_SIZE), GUILayout.Height(PREVIEW_SIZE));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            // Data rows
            GUIStyle cellStyle = new GUIStyle(GUI.skin.textArea) {wordWrap = true};
            GUIStyle nameStyle = new GUIStyle(GUI.skin.label) {wordWrap = true};

            foreach (Model model in _models)
            {
                float rowHeight = _modelEnabled[model.Name] ? ROW_HEIGHT : DISABLED_ROW_HEIGHT;
                EditorGUILayout.BeginHorizontal(GUILayout.Width(contentWidth), GUILayout.Height(rowHeight));

                // Model column
                EditorGUILayout.BeginVertical(GUILayout.Width(MODEL_COLUMN_WIDTH), GUILayout.Height(rowHeight));
                EditorGUILayout.BeginHorizontal();
                _modelEnabled[model.Name] = EditorGUILayout.Toggle(_modelEnabled[model.Name], GUILayout.Width(16));
                GUILayout.Label(model.Name, nameStyle, GUILayout.Width(MODEL_COLUMN_WIDTH - 25));
                EditorGUILayout.EndHorizontal();

                // Display total time for the model
                if (_modelTotalTimes.TryGetValue(model.Name, out float totalTime))
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(23);
                    EditorGUILayout.LabelField($"Total: {totalTime:F2}s", EditorStyles.miniLabel, GUILayout.Width(MODEL_COLUMN_WIDTH - 25));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();

                // Caption cells
                foreach (TestImage img in _testImages)
                {
                    EditorGUILayout.BeginVertical(GUILayout.Width(IMAGE_COLUMN_WIDTH), GUILayout.Height(rowHeight));
                    if (!_imageEnabled[img.path] || !_modelEnabled[model.Name])
                    {
                        EditorGUILayout.LabelField("-", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(IMAGE_COLUMN_WIDTH), GUILayout.ExpandHeight(true));
                    }
                    else
                    {
                        (string m, string i) key = (model.Name, img.path);
                        _results.TryGetValue(key, out string caption);
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.TextArea(caption ?? string.Empty, cellStyle, GUILayout.Width(IMAGE_COLUMN_WIDTH), GUILayout.Height(rowHeight - 30));
                        EditorGUI.EndDisabledGroup();

                        // Display timing and rerun button
                        EditorGUILayout.BeginHorizontal();
                        EditorGUI.BeginDisabledGroup(_isRunning);
                        if (GUILayout.Button("Run", EditorStyles.miniButton, GUILayout.Width(50)))
                        {
                            _ = RunSingleCaptionTestAsync(model.Name, img.path, true);
                        }
                        EditorGUI.EndDisabledGroup();
                        if (_cellTimes.TryGetValue(key, out float cellTime))
                        {
                            EditorGUILayout.LabelField($"{cellTime:F2}s", EditorStyles.miniLabel, GUILayout.Width(50));
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();

            GUILayout.FlexibleSpace();
            _showPrompt = EditorGUILayout.BeginFoldoutHeaderGroup(_showPrompt, "Prompt");
            if (_showPrompt)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("Default", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.TextArea(Intelligence.ModelPrompt);
                EditorGUILayout.EndVertical();
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.BeginVertical(GUILayout.Width(40), GUILayout.MaxHeight(150));
                GUILayout.FlexibleSpace();
                if (AI.Config.ollamaPrompt == null)
                {
                    if (GUILayout.Button("Customize", GUILayout.ExpandWidth(false)))
                    {
                        AI.Config.ollamaPrompt = Intelligence.ModelPrompt;
                        AI.SaveConfig();
                    }
                }
                else
                {
                    if (GUILayout.Button("Use Default", GUILayout.ExpandWidth(false)))
                    {
                        AI.Config.ollamaPrompt = null;
                        AI.SaveConfig();
                    }
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();

                if (AI.Config.ollamaPrompt != null)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField("Custom", EditorStyles.centeredGreyMiniLabel);
                    AI.Config.ollamaPrompt = EditorGUILayout.TextArea(AI.Config.ollamaPrompt);
                    EditorGUILayout.EndVertical();
                    if (EditorGUI.EndChangeCheck()) AI.SaveConfig();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Controls
            EditorGUILayout.BeginHorizontal();
            if (!_isRunning)
            {
                if (GUILayout.Button("Create All Test Captions", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                {
                    _cts = new CancellationTokenSource();
                    _ = RunCaptionTestsAsync(_cts.Token);
                }
            }
            else
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                {
                    _cts.Cancel();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private async Task RunCaptionTestsAsync(CancellationToken token)
        {
            _isRunning = true;
            try
            {
                foreach (Model model in _models.Where(m => _modelEnabled[m.Name]))
                {
                    float modelStartTime = Time.realtimeSinceStartup;
                    foreach (TestImage img in _testImages.Where(t => _imageEnabled[t.path]))
                    {
                        token.ThrowIfCancellationRequested();
                        await RunSingleCaptionTestAsync(model.Name, img.path);
                    }
                    float modelEndTime = Time.realtimeSinceStartup;
                    _modelTotalTimes[model.Name] = modelEndTime - modelStartTime;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _isRunning = false;
                _cts.Dispose();
                _cts = null;
                Repaint();
            }
        }

        private async Task RunSingleCaptionTestAsync(string modelName, string imagePath, bool setRunning = false)
        {
            if (setRunning) _isRunning = true;
            int oldBackend = AI.Config.aiBackend;
            try
            {
                (string m, string i) key = (modelName, imagePath);
                _results[key] = "Running...";
                Repaint();

                AI.Config.aiBackend = modelName == BLIP_BACKEND ? 0 : 1;

                float startTime = Time.realtimeSinceStartup;
                List<CaptionResult> blipResults = await CaptionCreator.CaptionImage(new List<string> {imagePath}, modelName);
                float endTime = Time.realtimeSinceStartup;

                string caption = blipResults?.FirstOrDefault()?.caption ?? string.Empty;
                _results[key] = caption;
                _cellTimes[key] = endTime - startTime;

                // Update model total time
                float modelTotal = _cellTimes.Where(kv => kv.Key.model == modelName).Sum(kv => kv.Value);
                _modelTotalTimes[modelName] = modelTotal;

                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error running single caption test: {ex.Message}");
            }
            AI.Config.aiBackend = oldBackend;
            if (setRunning) _isRunning = false;
        }

        private List<TestImage> GetTestImages()
        {
            List<TestImage> images = new List<TestImage>();

            string path = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:Texture2D asset-inventory-logo").FirstOrDefault());
            Texture2D logo = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            images.Add(new TestImage
            {
                path = path,
                texture = logo
            });

            string[] inventoryGuids = AssetDatabase.FindAssets("AssetInventory t:Folder");
            foreach (string guid in inventoryGuids)
            {
                string invPath = AssetDatabase.GUIDToAssetPath(guid);
                string testFolder = $"{invPath}/Editor/Images/Test";
                if (!AssetDatabase.IsValidFolder(testFolder)) continue;

                string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] {testFolder});
                foreach (string texGuid in texGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(texGuid);
                    string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
                    string absPath = Path.Combine(projectRoot, assetPath).Replace("\\", "/");
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    images.Add(new TestImage {path = absPath, texture = texture});
                }
                break;
            }
            return images;
        }
    }

    [Serializable]
    public sealed class TestImage
    {
        public string path;
        public Texture2D texture;
    }
}
#endif