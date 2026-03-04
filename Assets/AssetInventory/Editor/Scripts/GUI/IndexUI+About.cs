using UnityEditor;
#if UNITY_2021_3_OR_NEWER && !USE_TUTORIALS
using UnityEditor.PackageManager;
#endif
using UnityEngine;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private void DrawAboutTab()
        {
            GUIStyle textColor = EditorGUIUtility.isProSkin ? UIStyles.whiteCenter : UIStyles.blackCenter;

            EditorGUILayout.Space(6);

            // Header with title
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.MaxWidth(520));
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("A tool by Impossible Robert", UIStyles.centerHeading, GUILayout.Width(350), GUILayout.Height(50));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Links row
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Online Resources", UIStyles.centerLinkLabel)) Application.OpenURL(AI.HOME_LINK);
            EditorGUILayout.LabelField(" | ", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(10));
            if (GUILayout.Button("Join Discord", UIStyles.centerLinkLabel)) Application.OpenURL(AI.DISCORD_LINK);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

#if UNITY_2021_3_OR_NEWER && !USE_TUTORIALS
            // Tutorials CTA
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxWidth(520));
            EditorGUILayout.LabelField("Tutorials", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Integrated tutorials require the Unity Tutorials package.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(UIStyles.Content("Install/Upgrade Tutorials Package...", "Integrated tutorials require the Unity Tutorials package installed."), GUILayout.Width(280)))
            {
                Client.Add($"com.unity.learn.iet-framework@{AI.TUTORIALS_VERSION}");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
#endif

            // Version/info
            EditorGUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Version {AI.VERSION}", textColor, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Review CTA
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxWidth(520));
            EditorGUILayout.LabelField("Enjoying Asset Inventory?", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("If you like this asset, please consider leaving a review on the Unity Asset Store.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Write Review", GUILayout.Width(160))) Application.OpenURL(AI.ASSET_STORE_LINK);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Advanced tools
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (ShowAdvanced())
            {
                GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxWidth(520));
                EditorGUILayout.LabelField("Maintenance", EditorStyles.boldLabel);
                EditorGUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Show Welcome Dialog", GUILayout.Width(180))) WelcomeWindow.ShowWindow();
                if (GUILayout.Button("Restart Setup Wizard", GUILayout.Width(180)))
                {
                    AI.Config.wizardCompleted = false;
                    AI.Config.wizardCurrentPage = 0;
                    AI.SaveConfig();
                }
                if (GUILayout.Button("Create Debug Support Report", GUILayout.Width(220))) CreateDebugReport();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            // Logo at the bottom
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Box(Logo, EditorStyles.centeredGreyMiniLabel, GUILayout.MaxWidth(200), GUILayout.MaxHeight(200));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();

            if (AI.DEBUG_MODE && GUILayout.Button("Reload Lookups")) ReloadLookups();
            if (AI.DEBUG_MODE && GUILayout.Button("Get Token", GUILayout.ExpandWidth(false))) Debug.Log(CloudProjectSettings.accessToken);
            if (AI.DEBUG_MODE && GUILayout.Button("Free Memory")) Resources.UnloadUnusedAssets();
        }
    }
}