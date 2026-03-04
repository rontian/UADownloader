using System.IO;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class WelcomeWindow : BasicEditorUI
    {
        private const int WINDOW_WIDTH = 420;
        private const int WINDOW_HEIGHT = 300;

        public static void ShowWindow()
        {
            WelcomeWindow window = GetWindow<WelcomeWindow>("Welcome");
            window.minSize = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
            window.maxSize = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
            window.position = new Rect(
                (Screen.currentResolution.width - WINDOW_WIDTH) / 2f,
                (Screen.currentResolution.height - WINDOW_HEIGHT) / 3f,
                WINDOW_WIDTH,
                WINDOW_HEIGHT);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            // Ensure window is non-resizable by locking min/max sizes
            minSize = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
            maxSize = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
        }

        public override void OnGUI()
        {
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Box(Logo, EditorStyles.centeredGreyMiniLabel, GUILayout.MaxWidth(150), GUILayout.MaxHeight(150), GUILayout.MinHeight(50), GUILayout.MinWidth(50));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Welcome to Asset Inventory", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            EditorGUILayout.HelpBox("Get started by launching the tool, reading the docs, or joining the community. You can open it via Assets/Asset Inventory.", MessageType.Info);

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Launch Tool", UIStyles.mainButton, GUILayout.Width(120), GUILayout.Height(30)))
            {
                MenuIntegration.ShowWindow();
            }
            GUILayout.Space(6);
            if (GUILayout.Button("Read Docs", GUILayout.Width(120), GUILayout.Height(30)))
            {
                OpenLocalDocsPdf();
            }
            GUILayout.Space(6);
            if (GUILayout.Button("Join Community", GUILayout.Width(120), GUILayout.Height(30)))
            {
                Application.OpenURL(AI.DISCORD_LINK);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }

        private static void OpenLocalDocsPdf()
        {
            // Open Documentation/Documentation.pdf relative to the installed tool folder; fallback to online docs
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            // Resolve this script's asset path
            string scriptAssetPath = null;
            try
            {
                WelcomeWindow temp = CreateInstance<WelcomeWindow>();
                MonoScript ms = MonoScript.FromScriptableObject(temp);
                scriptAssetPath = AssetDatabase.GetAssetPath(ms);
                DestroyImmediate(temp);
            }
            catch
            {
                // ignored
            }

            if (!string.IsNullOrEmpty(scriptAssetPath))
            {
                // Convert to full filesystem path
                string full = Path.GetFullPath(Path.Combine(projectRoot, scriptAssetPath));
                string dir = Path.GetDirectoryName(full);

                // Walk up a few levels to find a Documentation/Documentation.pdf next to the tool root
                for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                {
                    string candidate = Path.Combine(dir, "Documentation", "Documentation.pdf");
                    if (File.Exists(candidate))
                    {
                        EditorUtility.OpenWithDefaultApp(candidate);
                        return;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            Application.OpenURL(AI.HOME_LINK);
        }
    }
}