using System.Collections.Generic;
using UnityEditor;
#if !USE_VECTOR_GRAPHICS || (UNITY_2021_3_OR_NEWER && !USE_TUTORIALS)
using UnityEditor.PackageManager;
#endif
using UnityEngine;
using System.IO;
using System;
using System.Linq;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const float WIZARD_STEP_LIST_WIDTH = 200f;

        private List<IWizardPage> _wizardPages;
        private Vector2 _wizardScrollPosition;

        private void DrawSetupView()
        {
            InitializeWizardPages();
            DrawEmbeddedWizard();
        }

        private void InitializeWizardPages()
        {
            if (_wizardPages == null)
            {
                _wizardPages = new List<IWizardPage>
                {
                    new SetupWizardIntroPage(),
                    new SetupWizardDownloadPage(),
                    new SetupWizardLocationsPage(),
                    new SetupWizardPreviewPage(),
                    new SetupWizardAIPage(),
                    new SetupWizardUIPage(),
                    new SetupWizardCompletionPage()
                };

                // Load current page from config
                if (AI.Config.wizardCurrentPage >= _wizardPages.Count)
                {
                    AI.Config.wizardCurrentPage = 0;
                }

                NavigateToWizardPage(AI.Config.wizardCurrentPage);
            }
        }

        private void DrawEmbeddedWizard()
        {
            GUILayout.BeginHorizontal();

            // Step list panel
            DrawWizardStepList();

            // Main content area
            GUILayout.BeginVertical();
            DrawWizardContent();
            DrawWizardFooter();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawWizardStepList()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(WIZARD_STEP_LIST_WIDTH), GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField("Setup Steps", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            for (int i = 0; i < _wizardPages.Count; i++)
            {
                IWizardPage page = _wizardPages[i];
                bool isCurrentPage = i == AI.Config.wizardCurrentPage;
                bool isCompleted = page.IsCompleted;
                bool isClickable = i <= AI.Config.wizardCurrentPage || isCompleted;

                // Determine the style and icon
                GUIStyle stepStyle = isCurrentPage ? EditorStyles.boldLabel : EditorStyles.label;
                string stepIcon = isCompleted ? "☑" : "☐"; // Checked box vs empty box
                string stepText = $"{stepIcon} {page.Title}";

                // Draw step button
                EditorGUI.BeginDisabledGroup(!isClickable);
                if (GUILayout.Button(stepText, stepStyle, GUILayout.Height(25)))
                {
                    if (isClickable)
                    {
                        NavigateToWizardPage(i);
                    }
                }
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Box(Logo, EditorStyles.centeredGreyMiniLabel, GUILayout.MaxWidth(150), GUILayout.MaxHeight(150), GUILayout.MinHeight(50), GUILayout.MinWidth(50));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            EditorGUILayout.Space();

            GUILayout.EndVertical();
        }

        private void DrawWizardContent()
        {
            if (_wizardPages == null || _wizardPages.Count == 0 || AI.Config.wizardCurrentPage >= _wizardPages.Count) return;

            _wizardScrollPosition = GUILayout.BeginScrollView(_wizardScrollPosition);

            IWizardPage currentPage = _wizardPages[AI.Config.wizardCurrentPage];

            // Call OnEnter when page becomes active
            if (Event.current.type == EventType.Layout)
            {
                currentPage.OnEnter();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(currentPage.Title, EditorStyles.largeLabel);
            EditorGUILayout.Space(5);

            if (!string.IsNullOrEmpty(currentPage.Description))
            {
                EditorGUILayout.LabelField(currentPage.Description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(10);
            }

            // Draw the page content
            currentPage.Draw();

            GUILayout.EndScrollView();
        }

        private void DrawWizardFooter()
        {
            GUILayout.BeginHorizontal();

            // Back button
            EditorGUI.BeginDisabledGroup(AI.Config.wizardCurrentPage <= 0);
            if (GUILayout.Button("Back", GUILayout.Width(60), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                NavigateToWizardPage(AI.Config.wizardCurrentPage - 1);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            // Buttons
            if (AI.Config.wizardCurrentPage == 0)
            {
                if (GUILayout.Button("Skip", GUILayout.Width(60), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                {
                    CompleteWizard();
                }
            }
            if (AI.Config.wizardCurrentPage < _wizardPages.Count - 1)
            {
                EditorGUI.BeginDisabledGroup(!_wizardPages[AI.Config.wizardCurrentPage].CanProceed);
                if (GUILayout.Button("Next", UIStyles.mainButton, GUILayout.Width(60), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                {
                    NavigateToWizardPage(AI.Config.wizardCurrentPage + 1);
                }
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUI.BeginDisabledGroup(!_wizardPages[AI.Config.wizardCurrentPage].CanProceed);
                if (GUILayout.Button("Finish", UIStyles.mainButton, GUILayout.Width(60), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                {
                    CompleteWizard();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.Space();
            GUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void NavigateToWizardPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _wizardPages.Count) return;

            // Call OnExit for current page
            if (AI.Config.wizardCurrentPage < _wizardPages.Count)
            {
                _wizardPages[AI.Config.wizardCurrentPage].OnExit();
            }

            // set all pages completed up to selected one
            for (int i = 0; i < pageIndex; i++)
            {
                _wizardPages[i].IsCompleted = true;
            }

            AI.Config.wizardCurrentPage = pageIndex;
            SaveWizardState();
            Repaint();
        }

        private void SaveWizardState()
        {
            AI.SaveConfig();
        }

        private void CompleteWizard()
        {
            AI.Config.wizardCompleted = true;
            AI.SaveConfig();
            Repaint();
        }
    }

    public class SetupWizardIntroPage : WizardPage
    {
        public override string Title => "Welcome to Asset Inventory";
        public override string Description => "Asset Inventory is a powerful tool for managing and searching your Unity assets. This setup wizard will guide you through the essential configuration options. All settings can be changed later in the Settings tab.";

        private static readonly Texture2D _sample = UIStyles.LoadTexture("asset-inventory-sample");

        public override void Draw()
        {
            EditorGUILayout.LabelField("Once complete you will be able to search through all your assets as shown below", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Box(_sample, EditorStyles.label, GUILayout.MinWidth(250), GUILayout.MaxWidth(750), GUILayout.MinHeight(200), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }

    public class SetupWizardDownloadPage : WizardPage
    {
        public override string Title => "Download Settings";
        public override string Description => "Asset Inventory can automatically download your purchased assets from the Asset Store for indexing. This ensures all your assets are properly catalogued.";

        public override void Draw()
        {
            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UIStyles.Content("Download Assets for Indexing", "Automatically download uncached items from the Asset Store for indexing. Will delete them again afterwards if not selected otherwise below. Attention: downloading an item will revoke the right to easily return it through the Asset Store."), EditorStyles.boldLabel, GUILayout.Width(220));
            AI.Actions.DownloadAssets = EditorGUILayout.Toggle(AI.Actions.DownloadAssets, GUILayout.MaxWidth(20));
            GUILayout.EndHorizontal();

            if (AI.Actions.DownloadAssets)
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Keep Downloaded Assets", "Will not delete automatically downloaded assets after indexing but keep them in the cache instead."), EditorStyles.boldLabel, GUILayout.Width(220));
                AI.Config.keepAutoDownloads = EditorGUILayout.Toggle(AI.Config.keepAutoDownloads, GUILayout.MaxWidth(20));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Limit Package Size", "Will not automatically download packages larger than specified."), EditorStyles.boldLabel, GUILayout.Width(220));
                AI.Config.limitAutoDownloads = EditorGUILayout.Toggle(AI.Config.limitAutoDownloads, GUILayout.MaxWidth(20));
                if (AI.Config.limitAutoDownloads)
                {
                    GUILayout.Label("to", GUILayout.ExpandWidth(false));
                    AI.Config.downloadLimit = EditorGUILayout.DelayedIntField(AI.Config.downloadLimit, GUILayout.Width(40));
                    GUILayout.Label("Mb", GUILayout.ExpandWidth(false));
                }
                GUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox("Note: If you plan to return freshly purchased assets you should deactivate this option since downloading will disallow returns. Also, downloading assets will use bandwidth. Consider your internet connection when enabling this feature in case you are not on an unlimited plan.", MessageType.Warning);

            if (EditorGUI.EndChangeCheck())
            {
                AI.SaveConfig();
            }
        }
    }

    public class SetupWizardLocationsPage : WizardPage
    {
        public override string Title => "Storage Location";
        public override string Description => "Specify where to store the database, cache and backups. While the database will only be a couple of hundred megabytes usually, the cache and backups can grow significantly and easily beyond 50-100Gb. The more space you reserve the more performant the tool can operate.";

        private List<DriveInfo> _drives;
        private Vector2 _driveScrollPosition;

        public override void OnEnter()
        {
            base.OnEnter();
            RefreshDriveInfo();
        }

        private void RefreshDriveInfo()
        {
            _drives = new List<DriveInfo>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (IsDriveSuitableForStorage(drive))
                    {
                        _drives.Add(drive);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error getting drive information: {e.Message}");
            }
        }

        private bool IsDriveSuitableForStorage(DriveInfo drive)
        {
            if (!drive.IsReady) return false;

            // Filter out unsuitable drive types
            switch (drive.DriveType)
            {
                case DriveType.Fixed:
                    // Fixed drives are good
                    break;
                case DriveType.Removable:
                    // Removable drives are not suitable for database storage
                    return false;
                case DriveType.Network:
                    // Network drives can be slow and unreliable for database operations
                    return false;
                case DriveType.CDRom:
                case DriveType.Unknown:
                case DriveType.NoRootDirectory:
                    return false;
            }

            // Filter out drives with very little space (less than 1GB)
            if (drive.AvailableFreeSpace < 1024 * 1024 * 1024) return false;

            // On macOS, filter out system volumes and temporary mounts
#if UNITY_EDITOR_OSX
            string drivePath = drive.RootDirectory.FullName.ToLowerInvariant();

            // Skip system volumes
            if (drivePath.Contains("/system/") || drivePath.Contains("/volumes/system")) return false;

            // Skip temporary mounts and network volumes
            if (drivePath.Contains("/volumes/.timemachine") ||
                drivePath.Contains("/volumes/.spotlight") ||
                drivePath.Contains("/volumes/.fseventsd") ||
                drivePath.Contains("/volumes/.trashes") ||
                drivePath.Contains("/volumes/.mobilebackups"))
                return false;

            // Skip network drives (usually mounted under /volumes with network paths)
            if (drivePath.Contains("//") || drivePath.Contains("smb://") || drivePath.Contains("afp://"))
                return false;

            // Skip drives that are likely temporary or system-related
            if (drive.VolumeLabel.ToLowerInvariant().Contains("time machine") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("backup") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("system") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("recovery"))
                return false;
#endif

            // On Windows, filter out some system drives
#if UNITY_EDITOR_WIN
            string drivePath = drive.RootDirectory.FullName.ToLowerInvariant();

            // Skip drives that are likely system recovery or temporary
            if (drive.VolumeLabel.ToLowerInvariant().Contains("recovery") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("system") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("temp"))
                return false;
#endif

            return true;
        }

        public override void Draw()
        {
            // Current database location
            string currentDbPath = AI.GetStorageFolder();
            string currentDbFolder = Path.GetDirectoryName(currentDbPath);

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Current Location:", EditorStyles.boldLabel, GUILayout.Width(120));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(currentDbPath, GUILayout.ExpandWidth(true));
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Change...", GUILayout.ExpandWidth(false))) SetDatabaseLocation();
            GUILayout.EndHorizontal();

            // Drive space overview
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Free Disk Space", EditorStyles.boldLabel);

            if (_drives != null && _drives.Count > 0)
            {
                foreach (DriveInfo drive in _drives)
                {
                    DrawDriveInfo(drive, currentDbFolder);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No drives found or accessible.", MessageType.Warning);
            }
        }

        private void SetDatabaseLocation()
        {
            string targetFolder = EditorUtility.OpenFolderPanel("Select folder for database and cache", AI.GetStorageFolder(), "");
            if (string.IsNullOrEmpty(targetFolder)) return;

            // check if same folder selected
            if (IOUtils.IsSameDirectory(targetFolder, AI.GetStorageFolder())) return;

            // disallow selecting a drive/root directory (e.g., C:\, D:\, E:, or /)
            if (IOUtils.IsRootPath(targetFolder))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Please select a subfolder, not a drive root.", "OK");
                return;
            }

            // check for existing database
            if (File.Exists(Path.Combine(targetFolder, DBAdapter.DB_NAME)))
            {
                if (EditorUtility.DisplayDialog("Use Existing?", "The target folder contains a database. Switch to this one? Otherwise please select an empty directory.", "Switch", "Cancel"))
                {
                    AI.SwitchDatabase(targetFolder);
                }

                return;
            }

            AI.SwitchDatabase(targetFolder);
            AssetStore.GatherAllMetadata();
            AssetStore.GatherProjectMetadata();
        }

        private void DrawDriveInfo(DriveInfo drive, string currentDbFolder)
        {
            bool isCurrentDrive = currentDbFolder.StartsWith(drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase);

            // Calculate free space percentage
            long freeSpace = drive.AvailableFreeSpace;

            // Draw drive info
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            string driveLabel = drive.VolumeLabel;
            if (string.IsNullOrEmpty(driveLabel)) driveLabel = drive.Name;

            if (isCurrentDrive)
            {
                driveLabel += " (Current)";
                EditorGUILayout.LabelField(driveLabel, EditorStyles.boldLabel);
            }
            else
            {
                EditorGUILayout.LabelField(driveLabel, EditorStyles.label);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(EditorUtility.FormatBytes(freeSpace), EditorStyles.boldLabel, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            // Simple progress bar
            DrawSimpleStorageBar(freeSpace, isCurrentDrive);

            GUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void DrawSimpleStorageBar(long freeSpace, bool isCurrentDrive)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 8);

            // Draw background
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f));

            // Calculate color based on free space in GB
            long freeSpaceGb = freeSpace / (1024 * 1024 * 1024);
            Color freeColor;
            if (freeSpaceGb > 200)
            {
                freeColor = Color.green;
            }
            else if (freeSpaceGb > 100)
            {
                freeColor = Color.yellow;
            }
            else
            {
                freeColor = Color.red;
            }

            // Draw free space bar (using a simple full-width bar for visual indication)
            EditorGUI.DrawRect(rect, freeColor);
        }

        public override bool CanProceed => true;
    }

    public class SetupWizardPreviewPage : WizardPage
    {
        public override string Title => "Display Settings";
        public override string Description => "Configure how Asset Inventory handles preview images and visual content.";

        public override void Draw()
        {
            EditorGUI.BeginChangeCheck();

            // Previews Section
            EditorGUILayout.LabelField("Previews", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.HelpBox("Previews are extracted from the packages. These are rather small (usually 128x128px). Activating the upscaling will provide larger previews for image files at the cost of additional storage space.", MessageType.Info);
            EditorGUILayout.Space();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UIStyles.Content("Upscale Preview Images", "Resize preview images to make them fill a bigger area of the tiles."), EditorStyles.boldLabel, GUILayout.Width(220));
            AI.Config.upscalePreviews = EditorGUILayout.Toggle(AI.Config.upscalePreviews, GUILayout.MaxWidth(20));
            GUILayout.EndHorizontal();

            if (AI.Config.upscalePreviews)
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content(AI.Config.upscaleLossless ? "Target Size" : "Minimum Size", "Minimum size the preview image should have. Bigger images are not changed."), EditorStyles.boldLabel, GUILayout.Width(220));
                AI.Config.upscaleSize = EditorGUILayout.DelayedIntField(AI.Config.upscaleSize, GUILayout.Width(100));
                EditorGUILayout.LabelField("px", EditorStyles.miniLabel, GUILayout.Width(30));
                GUILayout.EndHorizontal();
            }

#if !USE_VECTOR_GRAPHICS
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("In order to see previews for SVG graphics, the 'com.unity.vectorgraphics' package needs to be installed.", MessageType.Warning);
            if (GUILayout.Button("Install Vector Graphics Package"))
            {
                Client.Add("com.unity.vectorgraphics");
            }
#endif

            // Others Section
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Other Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UIStyles.Content("Preferred Currency", "Currency to show asset prices in."), EditorStyles.boldLabel, GUILayout.Width(220));
            AI.Config.currency = EditorGUILayout.Popup(AI.Config.currency, new[] {"EUR", "USD", "CNY"}, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                AI.SaveConfig();
            }
        }
    }

    public class SetupWizardAIPage : WizardPage
    {
        public override string Title => "AI Features (Optional)";
        public override string Description => "Asset Inventory includes AI-powered features that can automatically generate captions for many items. This allows you to search for an 'ambulance' even though the file might be called 'car1.prefab' but showing the ambulance.";

        public override void Draw()
        {
#if UNITY_2021_2_OR_NEWER
            EditorGUI.BeginChangeCheck();

            AI.Actions.CreateAICaptions = EditorGUILayout.ToggleLeft("Activate AI Captions", AI.Actions.CreateAICaptions);

            if (AI.Actions.CreateAICaptions)
            {
                EditorGUILayout.Space();

                if (AI.Config.aiBackend == 1) // Ollama
                {
                    if (!Intelligence.IsOllamaInstalled)
                    {
                        EditorGUILayout.HelpBox("AI captions require Ollama to be installed. This is a free tool you need to download and install yourself.", MessageType.Error);
                        if (GUILayout.Button("Ollama Website", UIStyles.wrappedLinkLabel, GUILayout.ExpandWidth(false)))
                        {
                            Application.OpenURL(Intelligence.OLLAMA_WEBSITE);
                        }
                    }
                    EditorGUILayout.Space();
                }

                EditorGUILayout.HelpBox("AI captions are created per package. Since this will take time, not all packages are enabled for AI captions per default. As a subsequent step go to the Packages tab and activate AI for those packages you want.", MessageType.Info);
            }

            if (EditorGUI.EndChangeCheck())
            {
                AI.SaveConfig();
            }
#else
            EditorGUILayout.HelpBox("Ollama support (a tool for local AI execution) is only available with Unity 2021.2+. You can setup Blip later in the settings as an alternative.", MessageType.Error);
#endif
        }
    }

    public class SetupWizardUIPage : WizardPage
    {
        public override string Title => "Advanced Features";
        public override string Description => "How to show advanced features.";

        private static readonly Texture2D _sample = UIStyles.LoadTexture("asset-inventory-sample2");


        public override void Draw()
        {
            EditorGUILayout.HelpBox("To simplify everyday usage, Asset Inventory will only show a subset of the features.", MessageType.Info);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("To switch between easy and advanced mode, use the eye icon in the top upper right corner or temporarily hold down the CTRL key.", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Box(_sample, EditorStyles.label, GUILayout.MinWidth(250), GUILayout.MaxWidth(650), GUILayout.MinHeight(200), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }
    }

    public class SetupWizardCompletionPage : WizardPage
    {
        public override string Title => "Setup Done";
        public override string Description => "All steps completed!";

        private int _assetsFileCount = -1;

        public override void OnEnter()
        {
            base.OnEnter();
            CountAssetsFiles();
            if (!AI.Actions.AnyActionsInProgress)
            {
                AI.Config.quickIndexingDone = false;
                AI.Actions.RunActions();
            }
        }

        private void CountAssetsFiles()
        {
            try
            {
                string assetsPath = Application.dataPath;
                if (Directory.Exists(assetsPath))
                {
                    _assetsFileCount = IOUtils.GetFilesSafe(assetsPath, "*.*", SearchOption.AllDirectories).Count();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not count files in Assets directory: {e.Message}");
                _assetsFileCount = -1;
            }
        }

        public override void Draw()
        {
            EditorGUILayout.HelpBox("You have successfully configured Asset Inventory. The tool is now ready to use in all your Unity projects.", MessageType.Info);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("What's Next:", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("1. Indexing of a part of your assets has already been triggered in the background", UIStyles.ColoredText(Color.green, true));
            EditorGUILayout.LabelField("2. Update your index regularly by clicking 'Run Actions' on the Settings tab", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("3. Check out the tutorials for advanced features", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("4. Have a great new asset management experience!", EditorStyles.wordWrappedLabel);
#if UNITY_2021_3_OR_NEWER && !USE_TUTORIALS
            EditorGUILayout.Space();
            if (GUILayout.Button("Install Getting-Started Tutorials", GUILayout.ExpandWidth(false)))
            {
                Client.Add($"com.unity.learn.iet-framework@{AI.TUTORIALS_VERSION}");
            }
#endif
            // Show warning if there are more than 100 files in Assets directory
            if (_assetsFileCount > 1500)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    $"Your project contains quite many files already which might slow down the initial indexing. " +
                    "For optimal performance, it is recommended to run Asset Inventory in an empty project first. " +
                    "This will significantly improve indexing speed." +
                    "The database is shared between projects.",
                    MessageType.Warning);
            }
        }

        public override bool CanProceed => true;
    }
}
