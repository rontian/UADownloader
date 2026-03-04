using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
using System.Threading.Tasks;
using OllamaSharp.Models;
#endif
using UnityEditor;
#if (UNITY_2021_3_OR_NEWER && !USE_TUTORIALS) || !USE_VECTOR_GRAPHICS
using UnityEditor.PackageManager;
#endif
using UnityEditorInternal;
using UnityEngine;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private Vector2 _folderScrollPos;
        private Vector2 _statsScrollPos;
        private Vector2 _settingsScrollPos;

        private bool _showMaintenance;
        private bool _showDiskSpace;
        private long _dbSize;
        private long _backupSize;
        private long _cacheSize;
        private long _persistedCacheSize;
        private long _previewSize;
        private string _captionTest = "-no caption created yet-";
        private bool _captionTestRunning;
        private bool _legacyCacheLocationFound;

        // additional folders
        private sealed class AdditionalFoldersWrapper : ScriptableObject
        {
            public List<FolderSpec> folders = new List<FolderSpec>();
        }

        private ReorderableList FolderListControl
        {
            get
            {
                if (_folderListControl == null) InitFolderControl();
                return _folderListControl;
            }
        }
        private ReorderableList _folderListControl;

        private SerializedObject SerializedFoldersObject
        {
            get
            {
                // reference can become null on reload
                if (_serializedFoldersObject == null || _serializedFoldersObject.targetObjects.FirstOrDefault() == null) InitFolderControl();
                return _serializedFoldersObject;
            }
        }
        private SerializedObject _serializedFoldersObject;
        private SerializedProperty _foldersProperty;
        private int _selectedFolderIndex = -1;
        private int _selectedUpdateActionIndex = -1;

        // update actions
        private sealed class UpdateActionsWrapper : ScriptableObject
        {
            public List<UpdateAction> actions = new List<UpdateAction>();
        }

        private ReorderableList UpdateActionsControl
        {
            get
            {
                if (_updateActionsControl == null) InitUpdateActions();
                return _updateActionsControl;
            }
        }
        private ReorderableList _updateActionsControl;

        private SerializedObject SerializedUpdateActionsObject
        {
            get
            {
                // reference can become null on reload
                if (_serializedUpdateActionsObject == null || _serializedUpdateActionsObject.targetObjects.FirstOrDefault() == null) InitUpdateActions();
                return _serializedUpdateActionsObject;
            }
        }
        private SerializedObject _serializedUpdateActionsObject;
        private SerializedProperty _updateActionsProperty;

        private bool _calculatingFolderSizes;
        private bool _cleanupInProgress;
        private DateTime _lastFolderSizeCalculation;
        private long _curOllamaProgress;
        private long _maxOllamaProgress;

        private void InitUpdateActions()
        {
            UpdateActionsWrapper obj = CreateInstance<UpdateActionsWrapper>();
            obj.actions = AI.Actions.Actions.Where(a => !a.hidden).ToList();

            _serializedUpdateActionsObject = new SerializedObject(obj);
            _updateActionsProperty = _serializedUpdateActionsObject.FindProperty("actions");
            _updateActionsControl = new ReorderableList(_serializedUpdateActionsObject, _updateActionsProperty, false, true, false, false);
            _updateActionsControl.drawElementCallback = DrawUpdateActionItem;
            _updateActionsControl.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "Actions to Perform" + (AI.Actions.AnyActionsInProgress ? $" (Started {StringUtils.GetRelativeTimeDifference(AI.Actions.GetFirstActionStart())})" : ""));
                if (GUI.Button(new Rect(rect.x + rect.width - 155, rect.y, 35, 20), "All", EditorStyles.miniButton))
                {
                    AI.Actions.SetAllActive(true);
                }
                if (GUI.Button(new Rect(rect.x + rect.width - 115, rect.y, 60, 20), "Default", EditorStyles.miniButton))
                {
                    AI.Actions.SetDefaultActive();
                }
                if (GUI.Button(new Rect(rect.x + rect.width - 50, rect.y, 50, 20), "None", EditorStyles.miniButton))
                {
                    AI.Actions.SetAllActive(false);
                }
            };
            _updateActionsControl.displayAdd = true;
            _updateActionsControl.displayRemove = true;
            _updateActionsControl.onAddCallback = _ =>
            {
                NameUI nameUI = new NameUI();
                nameUI.Init("My Action", CreateAction);
                PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 0, 0), nameUI);
            };
            _updateActionsControl.onCanRemoveCallback = AllowRemoveAction;
            _updateActionsControl.onRemoveCallback = RemoveAction;
        }

        private bool AllowRemoveAction(ReorderableList list)
        {
            if (_selectedUpdateActionIndex < 0 || _selectedUpdateActionIndex >= AI.Actions.Actions.Count) return false;

            return AI.Actions.Actions[_selectedUpdateActionIndex].key.StartsWith(ActionHandler.ACTION_USER);
        }

        private void RemoveAction(ReorderableList list)
        {
            string key = AI.Actions.Actions[_selectedUpdateActionIndex].key;
            int id = int.Parse(key.Split('-').Last());
            CustomAction ca = DBAdapter.DB.Find<CustomAction>(id);
            if (ca == null)
            {
                Debug.LogError($"Could not find action to delete: {key}. Restarting Unity might solve this.");
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm", $"Do you really want to delete the action '{ca.Name}'?", "Yes", "No")) return;

            DBAdapter.DB.Execute("delete from CustomActionStep where ActionId=?", ca.Id);
            DBAdapter.DB.Delete(ca);

            AI.Actions.Init(true);
            InitUpdateActions();
        }

        private void CreateAction(string actionName)
        {
            CustomAction action = new CustomAction(actionName.Trim());
            DBAdapter.DB.Insert(action);

            AI.Actions.Init(true);
            InitUpdateActions();
            EditAction(action.Id);
        }

        private void EditAction(string actionKey)
        {
            int id = int.Parse(actionKey.Split('-').Last());
            EditAction(id);
        }

        private void EditAction(int id)
        {
            CustomAction action = DBAdapter.DB.Find<CustomAction>(id);

            ActionUI actionUI = ActionUI.ShowWindow();
            actionUI.Init(action, InitUpdateActions);
        }

        private void OnActionsInitialized()
        {
            InitUpdateActions();
        }

        private void InitFolderControl()
        {
            AdditionalFoldersWrapper obj = CreateInstance<AdditionalFoldersWrapper>();
            obj.folders = AI.Config.folders;

            _serializedFoldersObject = new SerializedObject(obj);
            _foldersProperty = _serializedFoldersObject.FindProperty("folders");
            _folderListControl = new ReorderableList(_serializedFoldersObject, _foldersProperty, true, false, true, true);
            _folderListControl.drawElementCallback = DrawFoldersListItem;
            _folderListControl.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Folders to Index");
            _folderListControl.onAddCallback = OnAddCustomFolder;
            _folderListControl.onRemoveCallback = OnRemoveCustomFolder;
        }

        private void DrawUpdateActionItem(Rect rect, int index, bool isActive, bool isFocused)
        {
            // draw alternating-row background
            if (Event.current.type == EventType.Repaint && index % 2 == 1)
            {
                // choose a tiny overlay that will darken/lighten regardless of the exact theme colors
                Color overlay = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f) // on dark (Pro) skin, brighten a hair
                    : new Color(0f, 0f, 0f, 0.025f); // on light skin, darken a hair

                EditorGUI.DrawRect(rect, overlay);
            }

            if (index >= AI.Actions.Actions.Count) return;

            UpdateAction action = AI.Actions.Actions[index];

            if (isFocused) _selectedUpdateActionIndex = index;

            EditorGUI.BeginChangeCheck();
            AI.Actions.SetActive(action, GUI.Toggle(new Rect(rect.x, rect.y, 20, EditorGUIUtility.singleLineHeight), AI.Actions.IsActive(action), UIStyles.Content("", "Include action when updating everything"), UIStyles.toggleStyle));
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            GUI.Label(new Rect(rect.x + 20, rect.y, rect.width - 250, EditorGUIUtility.singleLineHeight), UIStyles.Content(action.name, action.description), UIStyles.entryStyle);
            Color oldCol = GUI.backgroundColor;
            if (action.IsRunning())
            {
                GUI.backgroundColor = Color.green;
            }
            EditorGUI.BeginDisabledGroup(action.IsRunning() || action.scheduled || AI.Actions.AnyActionsInProgress);
            if (action.key.StartsWith(ActionHandler.ACTION_USER))
            {
                if (GUI.Button(new Rect(rect.x + rect.width - 65, rect.y + 1, 30, 20), EditorGUIUtility.IconContent("editicon.sml", "|Edit Action")))
                {
                    EditAction(action.key);
                }
            }
            if (action.supportsForce && ShowAdvanced() && GUI.Button(new Rect(rect.x + rect.width - 65, rect.y + 1, 30, 20), EditorGUIUtility.IconContent("d_preAudioAutoPlayOff@2x", "|Force Run Action Now")))
            {
                _ = AI.Actions.RunAction(action, true);
            }
            if (GUI.Button(new Rect(rect.x + rect.width - 30, rect.y + 1, 30, 20), EditorGUIUtility.IconContent("d_PlayButton@2x", "|Run Action Now")))
            {
                _ = AI.Actions.RunAction(action);
            }
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = oldCol;
        }

        private void DrawFoldersListItem(Rect rect, int index, bool isActive, bool isFocused)
        {
            // draw alternating-row background
            if (Event.current.type == EventType.Repaint && index % 2 == 1)
            {
                // choose a tiny overlay that will darken/lighten regardless of the exact theme colors
                Color overlay = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f) // on dark (Pro) skin, brighten a hair
                    : new Color(0f, 0f, 0f, 0.025f); // on light skin, darken a hair

                EditorGUI.DrawRect(rect, overlay);
            }

            _legacyCacheLocationFound = false;
            if (index >= AI.Config.folders.Count) return;

            FolderSpec spec = AI.Config.folders[index];

            if (isFocused) _selectedFolderIndex = index;

            EditorGUI.BeginChangeCheck();
            spec.enabled = GUI.Toggle(new Rect(rect.x, rect.y, 20, EditorGUIUtility.singleLineHeight), spec.enabled, UIStyles.Content("", "Rescan and update folder when running the action."), UIStyles.toggleStyle);
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            GUI.Label(new Rect(rect.x + 20, rect.y, rect.width - 250, EditorGUIUtility.singleLineHeight), spec.location, UIStyles.entryStyle);
            GUI.Label(new Rect(rect.x + rect.width - 230, rect.y, 200, EditorGUIUtility.singleLineHeight), UIStyles.FolderTypes[spec.folderType] + (spec.folderType == 1 ? " (" + UIStyles.MediaTypes[spec.scanFor] + ")" : ""), UIStyles.entryStyle);
            if (GUI.Button(new Rect(rect.x + rect.width - 30, rect.y + 1, 30, 20), EditorGUIUtility.IconContent("Settings", "|Folder Settings")))
            {
                FolderSettingsUI folderSettingsUI = new FolderSettingsUI();
                folderSettingsUI.Init(spec);
                PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 0, 0), folderSettingsUI);
            }
            if (spec.location.Contains(AI.ASSET_STORE_FOLDER_NAME)) _legacyCacheLocationFound = true;
        }

        private void OnRemoveCustomFolder(ReorderableList list)
        {
            _legacyCacheLocationFound = false; // otherwise warning will not be cleared if last folder is removed
            if (_selectedFolderIndex < 0 || _selectedFolderIndex >= AI.Config.folders.Count) return;
            AI.Config.folders.RemoveAt(_selectedFolderIndex);
            AI.SaveConfig();
        }

        private void OnAddCustomFolder(ReorderableList list)
        {
            string folder = EditorUtility.OpenFolderPanel("Select folder to index", "", "");
            if (string.IsNullOrEmpty(folder)) return;

            // make absolute and conform to OS separators
            folder = Path.GetFullPath(folder);

            // special case: a relative key is already defined for the folder to be added, replace it immediately
            folder = AI.MakeRelative(folder);

            // don't allow adding Unity asset cache folders manually 
            if (folder.Contains(AI.ASSET_STORE_FOLDER_NAME))
            {
                EditorUtility.DisplayDialog("Attention", "You selected a custom Unity asset cache location. This should be done by setting the asset cache location above to custom.", "OK");
                return;
            }

            // ensure no trailing slash if root folder on Windows
            if (folder.Length > 1 && folder.EndsWith("/")) folder = folder.Substring(0, folder.Length - 1);

            FolderWizardUI wizardUI = FolderWizardUI.ShowWindow();
            wizardUI.Init(folder);
        }

        private void DrawSettingsTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            _folderScrollPos = GUILayout.BeginScrollView(_folderScrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));

            int labelWidth = 225;
            int cbWidth = 20;

            // invisible spacer to ensure settings are legible if all are collapsed
            EditorGUILayout.LabelField("", GUILayout.Width(labelWidth), GUILayout.Height(1));

            // actions
            BeginIndentBlock();
            if (SerializedUpdateActionsObject != null)
            {
                SerializedUpdateActionsObject.Update();
                UpdateActionsControl.DoLayoutList();
                SerializedUpdateActionsObject.ApplyModifiedProperties();

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            EndIndentBlock();
            EditorGUILayout.Space();

            // settings
            EditorGUI.BeginChangeCheck();
            AI.Config.showIndexingSettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showIndexingSettings, "Indexing");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showIndexingSettings)
            {
                BeginIndentBlock();
                UIBlock("settings.locationintro", () =>
                {
                    EditorGUILayout.HelpBox("Unity stores downloads in two cache folders: one for Assets and one for content from the Unity package registry. These Unity cache folders will be your main indexing locations. Specify custom locations in the Additional Folders list below.", MessageType.Info);
                });

                EditorGUI.BeginChangeCheck();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Asset Cache Location", "How to determine where Unity stores downloaded asset packages"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.assetCacheLocationType = EditorGUILayout.Popup(AI.Config.assetCacheLocationType, _assetCacheLocationOptions, GUILayout.Width(300));
                GUILayout.EndHorizontal();

                switch (AI.Config.assetCacheLocationType)
                {
                    case 0:
                        UIBlock("settings.actions.openassetcache", () =>
                        {
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("", GUILayout.Width(labelWidth));
                            if (GUILayout.Button("Open", GUILayout.ExpandWidth(false))) EditorUtility.RevealInFinder(AI.GetAssetCacheFolder());
                            EditorGUILayout.LabelField(AI.GetAssetCacheFolder());
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        });

#if UNITY_2022_1_OR_NEWER
                        // show hint if Unity is not self-reporting the cache location
                        if (string.IsNullOrWhiteSpace(AssetStore.GetAssetCacheFolder()))
                        {
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("", GUILayout.Width(labelWidth));
                            EditorGUILayout.HelpBox("If you defined a custom location for your cache folder different from the one above, either set the 'ASSETSTORE_CACHE_PATH' environment variable or select 'Custom' and enter the path there. Unity does not expose the location yet for other tools.", MessageType.Info);
                            GUILayout.EndHorizontal();
                        }
#endif
                        break;

                    case 1:
                        DrawFolder("", AI.Config.assetCacheLocation, AI.GetAssetCacheFolder(), newFolder =>
                        {
                            AI.Config.assetCacheLocation = newFolder;
                            AI.GetObserver().SetPath(AI.GetAssetCacheFolder());
                        }, labelWidth, "Select asset cache folder of Unity (ending with 'Asset Store-5.x')", validate =>
                        {
                            if (Path.GetFileName(validate).ToLowerInvariant() != AI.ASSET_STORE_FOLDER_NAME.ToLowerInvariant())
                            {
                                EditorUtility.DisplayDialog("Error", $"Not a valid Unity asset cache folder. It should point to a folder ending with '{AI.ASSET_STORE_FOLDER_NAME}'", "OK");
                                return false;
                            }
                            return true;
                        });
                        UIBlock("settings.customlocationwarning", () =>
                        {
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("", GUILayout.Width(labelWidth));
                            EditorGUILayout.HelpBox("Setting a custom location should only be done if the automatic detection did not work and Unity actually stores the packages it downloads in a different place. Otherwise this will lead to an inconsistent experience. Downloads will always happen to the folder that is managed by Unity, not the one selected here.", MessageType.Warning);
                            GUILayout.EndHorizontal();
                        });
                        break;
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Package Cache Location", "How to determine where Unity stores downloaded registry packages"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.packageCacheLocationType = EditorGUILayout.Popup(AI.Config.packageCacheLocationType, _assetCacheLocationOptions, GUILayout.Width(300));
                GUILayout.EndHorizontal();

                switch (AI.Config.packageCacheLocationType)
                {
                    case 0:
                        UIBlock("settings.actions.openpackagecache", () =>
                        {
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("", GUILayout.Width(labelWidth));
                            if (GUILayout.Button("Open", GUILayout.ExpandWidth(false))) EditorUtility.RevealInFinder(AI.GetPackageCacheFolder());
                            EditorGUILayout.LabelField(AI.GetPackageCacheFolder());
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        });
                        break;

                    case 1:
                        DrawFolder("", AI.Config.packageCacheLocation, AI.GetAssetCacheFolder(), newFolder => AI.Config.packageCacheLocation = newFolder, labelWidth);
                        break;
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Index Sub-Packages", "Will scan packages for other .unitypackage files and also index these. Recommended, as it is the basis for SRP support since SRP packages are sub-packages inside other packages."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.indexSubPackages = EditorGUILayout.Toggle(AI.Config.indexSubPackages, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                EditorGUILayout.LabelField(UIStyles.Content("Download Settings"), EditorStyles.boldLabel);

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Keep Downloaded Assets", "Will not delete automatically downloaded assets after indexing but keep them in the cache instead."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.keepAutoDownloads = EditorGUILayout.Toggle(AI.Config.keepAutoDownloads, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Limit Package Size", "Will not automatically download packages larger than specified."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.limitAutoDownloads = EditorGUILayout.Toggle(AI.Config.limitAutoDownloads, GUILayout.Width(15));

                if (AI.Config.limitAutoDownloads)
                {
                    GUILayout.Label("to", GUILayout.ExpandWidth(false));
                    AI.Config.downloadLimit = EditorGUILayout.DelayedIntField(AI.Config.downloadLimit, GUILayout.Width(40));
                    GUILayout.Label("Mb", GUILayout.ExpandWidth(false));
                }
                GUILayout.EndHorizontal();

                if (ShowAdvanced())
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Extract Full Metadata", "Will extract dimensions from images and length from audio files to make these searchable at the cost of a slower indexing process."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.gatherExtendedMetadata = EditorGUILayout.Toggle(AI.Config.gatherExtendedMetadata, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Index Asset Package Contents", "Will extract asset packages (.unitypackage) and make contents searchable. This is the foundation for the search. Deactivate only if you are solely interested in package metadata."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.indexAssetPackageContents = EditorGUILayout.Toggle(AI.Config.indexAssetPackageContents, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Exclude Hidden Packages", "Will activate the exclude flag for packages that have been hidden by the user on the Asset Store."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.excludeHidden = EditorGUILayout.Toggle(AI.Config.excludeHidden, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    EditorGUILayout.LabelField(UIStyles.Content("Defaults for New Packages"), EditorStyles.boldLabel);

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Keep Cached", "Will set the Keep Cached flag on newly discovered assets. This will cause them to remain in the cache after indexing making the next access fast."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.extractByDefault = EditorGUILayout.Toggle(AI.Config.extractByDefault, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Backup", "Will mark newly discovered packages to be backed up automatically. Otherwise you need to select packages manually which will save a lot of disk space potentially."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.backupByDefault = EditorGUILayout.Toggle(AI.Config.backupByDefault, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}AI Captions", "Will set the AI Caption flag on newly discovered assets. This will cause AI captions to be created for these when the caption action is run."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.captionByDefault = EditorGUILayout.Toggle(AI.Config.captionByDefault, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Exclude", "Will not cause automatic indexing of newly discovered assets. Instead this needs to be triggered manually per package."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.excludeByDefault = EditorGUILayout.Toggle(AI.Config.excludeByDefault, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Pause indexing regularly", "Will pause all hard disk activity regularly to allow the disk to cool down."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.useCooldown = EditorGUILayout.Toggle(AI.Config.useCooldown, GUILayout.Width(15));

                if (AI.Config.useCooldown)
                {
                    GUILayout.Label("every", GUILayout.ExpandWidth(false));
                    AI.Config.cooldownInterval = EditorGUILayout.DelayedIntField(AI.Config.cooldownInterval, GUILayout.Width(30));
                    GUILayout.Label("minutes for", GUILayout.ExpandWidth(false));
                    AI.Config.cooldownDuration = EditorGUILayout.DelayedIntField(AI.Config.cooldownDuration, GUILayout.Width(30));
                    GUILayout.Label("seconds", GUILayout.ExpandWidth(false));
                }
                GUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                {
                    AI.SaveConfig();
                    _requireLookupUpdate = ChangeImpact.Write;
                }
                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // additional folders
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            AI.Config.showFolderSettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showFolderSettings, "Additional Folders");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showFolderSettings)
            {
                BeginIndentBlock();

                UIBlock("settings.foldersintro", () =>
                {
                    EditorGUILayout.HelpBox("Use Additional Folders to scan for Unity Packages downloaded from somewhere else than the Asset Store or for any arbitrary media files like your model or sound library you want to access.", MessageType.Info);
                });

                if (SerializedFoldersObject != null)
                {
                    EditorGUILayout.Space();
                    SerializedFoldersObject.Update();
                    FolderListControl.DoLayoutList();
                    SerializedFoldersObject.ApplyModifiedProperties();
                }

                if (_legacyCacheLocationFound)
                {
                    EditorGUILayout.HelpBox("You have selected a custom asset cache location as an additional folder. This should be done using the Asset Cache Location UI above in this new version.", MessageType.Warning);
                }

                // relative locations
                if (AI.UserRelativeLocations.Count > 0)
                {
                    EditorGUILayout.LabelField("Relative Location Mappings", EditorStyles.boldLabel);
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Key", EditorStyles.boldLabel, GUILayout.Width(200));
                    EditorGUILayout.LabelField("Location", EditorStyles.boldLabel);
                    GUILayout.EndHorizontal();
                    foreach (RelativeLocation location in AI.UserRelativeLocations)
                    {
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(location.Key, GUILayout.Width(200));

                        string otherSystems = "Mappings on other systems:\n\n";
                        string otherLocs = string.Join("\n", location.otherLocations);
                        otherSystems += string.IsNullOrWhiteSpace(otherLocs) ? "-None-" : otherLocs;

                        if (string.IsNullOrWhiteSpace(location.Location))
                        {
                            EditorGUILayout.LabelField(UIStyles.Content("-Not yet connected-", otherSystems));

                            // TODO: add ability to force delete relative mapping in case it is not used in additional folders anymore
                        }
                        else
                        {
                            EditorGUILayout.LabelField(UIStyles.Content(location.Location, otherSystems));
                            if (string.IsNullOrWhiteSpace(otherLocs))
                            {
                                EditorGUI.BeginDisabledGroup(true);
                                GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Cannot delete only remaining mapping"), GUILayout.Width(30));
                                EditorGUI.EndDisabledGroup();
                            }
                            else
                            {
                                if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Delete mapping"), GUILayout.Width(30)))
                                {
                                    DBAdapter.DB.Delete(location);
                                    AI.LoadRelativeLocations();
                                }
                            }
                        }
                        if (GUILayout.Button(UIStyles.Content("...", "Select folder"), GUILayout.Width(30)))
                        {
                            SelectRelativeFolderMapping(location);
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();
                }
                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Asset Manager
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            AI.Config.showAMSettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showAMSettings, "Unity Asset Manager");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showAMSettings)
            {
                BeginIndentBlock();
                DrawAssetManager();
                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // importing
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            AI.Config.showImportSettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showImportSettings, "Import");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showImportSettings)
            {
                BeginIndentBlock();
                UIBlock("settings.srpintro", () =>
                {
                    EditorGUILayout.HelpBox("There is extensive support for SRPs (scriptable render pipelines). Two mechanisms exist: if a package brings it's own SRP support packages, dependencies will automatically be used from these which fit to the current project. If these do not exist, the tool can automatically trigger the Unity URP converter after an import when activated below.", MessageType.Info);
                });

                EditorGUI.BeginChangeCheck();
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Adapt to Render Pipeline", "Will automatically adapt materials to the current render pipeline upon import."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                if (AI.Config.convertToPipeline)
                {
                    if (GUILayout.Button("Deactivate", GUILayout.ExpandWidth(false))) AI.SetPipelineConversion(false);
                }
                else
                {
                    if (GUILayout.Button("Activate", GUILayout.ExpandWidth(false)))
                    {
                        if (EditorUtility.DisplayDialog("Confirmation", "This will adapt materials to the current render pipeline if it is not the built-in one. This will affect newly imported as well as already existing project files. It is the same as running the Unity Render Pipeline Converter manually for all project materials. Are you sure?", "Yes", "Cancel"))
                        {
                            AI.SetPipelineConversion(true);
                        }
                    }
                }
#if USE_URP_CONVERTER
                GUILayout.Label("(URP only, supported in current project)", EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(false));
#else
                GUILayout.Label("(URP only, unsupported in current project, requires URP version 14 or higher)", EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true));
#endif
                GUILayout.EndHorizontal();

                UIBlock("settings.importstructureintro", () =>
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("You can always drag & drop assets from the search into a folder of your choice in the project view. What can be configured is the behavior when using the Import button or double-clicking an asset.", MessageType.Info);
                });

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Structure", "Structure to materialize the imported files in"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.importStructure = EditorGUILayout.Popup(AI.Config.importStructure, _importStructureOptions, GUILayout.Width(300));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Destination", "Target folder for imported files"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.importDestination = EditorGUILayout.Popup(AI.Config.importDestination, _importDestinationOptions, GUILayout.Width(300));
                GUILayout.EndHorizontal();

                if (AI.Config.importDestination == 2)
                {
                    DrawFolder("Target Folder", AI.Config.importFolder, "/Assets", newFolder =>
                    {
                        // store only part relative to /Assets
                        AI.Config.importFolder = newFolder?.Substring(Path.GetDirectoryName(Application.dataPath).Length + 1);
                    }, labelWidth, "Select folder for imports", validate =>
                    {
                        if (!validate.Replace("\\", "/").ToLowerInvariant().StartsWith(Application.dataPath.Replace("\\", "/").ToLowerInvariant()))
                        {
                            EditorUtility.DisplayDialog("Error", "Folder must be inside current project", "OK");
                            return false;
                        }
                        return true;
                    });
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Calculate FBX Dependencies", "Will scan FBX files for embedded texture references. This is recommended for maximum compatibility but can reduce performance of dependency calculation and preview generation."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.scanFBXDependencies = EditorGUILayout.Toggle(AI.Config.scanFBXDependencies, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Cross-Package Dependencies", "If referenced GUIDs cannot be found in the current package, the tool will scan the whole database if a match can be found somewhere else. Some asset authors rely on having multiple packs installed, e.g. level assembly packs."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.allowCrossPackageDependencies = EditorGUILayout.Toggle(AI.Config.allowCrossPackageDependencies, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                if (ShowAdvanced())
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Remove LODs", "Will remove LOD groups from imported prefabs and only keep the first one."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.removeLODs = EditorGUILayout.Toggle(AI.Config.removeLODs, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Remove Unresolveable Files", "Will automatically clean-up the database if a file cannot be found in the materialized package anymore but is still in the database."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.removeUnresolveableDBFiles = EditorGUILayout.Toggle(AI.Config.removeUnresolveableDBFiles, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();
                }

                if (EditorGUI.EndChangeCheck()) AI.SaveConfig();
                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // preview images
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            AI.Config.showPreviewSettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showPreviewSettings, "Previews");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showPreviewSettings)
            {
                BeginIndentBlock();
                EditorGUI.BeginChangeCheck();

                if (ShowAdvanced())
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Extract Preview Images", "Keep a folder with preview images for each asset file. Will require a moderate amount of space if there are many files."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.extractPreviews = EditorGUILayout.Toggle(AI.Config.extractPreviews, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Use Small Image Files Directly", "Will not create a separate preview file in the Previews folder if an image file is in an additional folder with dimensions fitting to the preview size. Recommended to speed up the preview pipeline and reduce storage size."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.directMediaPreviews = EditorGUILayout.Toggle(AI.Config.directMediaPreviews, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Use Fallback-Icons as Previews", "Will show generic icons in case a file preview is missing instead of an empty tile."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.showIconsForMissingPreviews = EditorGUILayout.Toggle(AI.Config.showIconsForMissingPreviews, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Verify Previews", "Will check preview images if they indeed contain a preview or just Unity default icons. Highly recommended but will slow down indexing and preview recreation."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.verifyPreviews = EditorGUILayout.Toggle(AI.Config.verifyPreviews, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Recreate Previews After Indexing", "Will run the preview recreation automatically once a package is indexed in case previews are missing or erroneous. Recommended, especially in combination with preview verification."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.recreatePreviewsAfterIndexing = EditorGUILayout.Toggle(AI.Config.recreatePreviewsAfterIndexing, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Download Missing Packages", "Will automatically temporarily download packages for which previews are missing."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.downloadPackagesForPreviews = EditorGUILayout.Toggle(AI.Config.downloadPackagesForPreviews, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Upscale Preview Images", "Resize preview images to make them fill a bigger area of the tiles."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.upscalePreviews = EditorGUILayout.Toggle(AI.Config.upscalePreviews, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                if (AI.Config.upscalePreviews)
                {
                    if (ShowAdvanced())
                    {
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Lossless" + (Application.platform == RuntimePlatform.WindowsEditor ? " (Windows only)" : ""), "Only create upscaled versions if base resolution is bigger. This will then mostly only affect images which can be previewed at a higher scale but leave prefab previews at the resolution they have through Unity, avoiding scaling artifacts."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        AI.Config.upscaleLossless = EditorGUILayout.Toggle(AI.Config.upscaleLossless, GUILayout.MaxWidth(cbWidth));
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content(AI.Config.upscaleLossless ? $"{UIStyles.INDENT}Target Size" : $"{UIStyles.INDENT}Minimum Size", "Minimum size the preview image should have. Bigger images are not changed."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.upscaleSize = EditorGUILayout.DelayedIntField(AI.Config.upscaleSize, GUILayout.Width(50));
                    EditorGUILayout.LabelField("px", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                }

                if (ShowAdvanced())
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Parallel Processing", "Number of previews to process simultaneously. Higher values can speed up preview generation but may use more memory and CPU. Set to 1 for sequential processing."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.parallelPreviewBatchSize = EditorGUILayout.DelayedIntField(AI.Config.parallelPreviewBatchSize, GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Wait Time", "Minimum time in seconds to wait for Unity's preview generation before giving up. Lower values speed up indexing but may skip some previews. Unity's preview system can be slow and unreliable."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.minPreviewWait = EditorGUILayout.DelayedFloatField(AI.Config.minPreviewWait, GUILayout.Width(50));
                    EditorGUILayout.LabelField("seconds");
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Animation Frames", "Number of frames to create for the preview of animated objects (e.g. videos), evenly spread across the animation. Higher frames require more storage space. Recommended are 3 or 4."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.animationGrid = EditorGUILayout.DelayedIntField(AI.Config.animationGrid, GUILayout.Width(50));
                    EditorGUILayout.LabelField("(will be squared, e.g. 4 = 16 frames)", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Animation Speed", "Time interval until a new frame of the animation is shown in seconds."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.animationSpeed = EditorGUILayout.DelayedFloatField(AI.Config.animationSpeed, GUILayout.Width(50));
                    EditorGUILayout.LabelField("s", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Exclude Extensions", "File extensions that should be skipped when creating preview images during media and archive indexing (e.g. blend,fbx,wav)."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.excludePreviewExtensions = EditorGUILayout.Toggle(AI.Config.excludePreviewExtensions, GUILayout.Width(16));
                    if (AI.Config.excludePreviewExtensions) AI.Config.excludedPreviewExtensions = EditorGUILayout.DelayedTextField(AI.Config.excludedPreviewExtensions, GUILayout.Width(300));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Keep Cached on Audio Playback", "Will set the 'Keep Cached' flag on a package to true when previewing an audio clip from it to ensure audio plays back smoothly without waiting for extraction first in the future."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.keepExtractedOnAudio = EditorGUILayout.Toggle(AI.Config.keepExtractedOnAudio, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();
                }

#if UNITY_2021_2_OR_NEWER && UNITY_EDITOR_WIN && !NET_4_6
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Your 'Editor Assembly Compatibility Level' is set to '.NET Standard' in the Player Settings. This will cause the tool to use an alternative image processing library which is slower on Windows. If you do not have a specific need for .NET Standard it is recommended to switch to .NET Framework.", MessageType.Warning);
#endif
#if !USE_VECTOR_GRAPHICS
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("In order to see previews for SVG graphics, the 'com.unity.vectorgraphics' needs to be installed.", MessageType.Warning);
                if (GUILayout.Button("Install Package"))
                {
                    Client.Add("com.unity.vectorgraphics");
                }
#endif

                if (EditorGUI.EndChangeCheck())
                {
                    AI.SaveConfig();
                    _requireSearchUpdate = true;
                }
                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // backup
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            AI.Config.showBackupSettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showBackupSettings, "Backup");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showBackupSettings)
            {
                BeginIndentBlock();
                EditorGUILayout.HelpBox("Automatically create backups of your asset purchases. Unity does not store old versions and assets get regularly deprecated. Backups will allow you to go back to previous versions easily. Backups will be done at the end of each update cycle.", MessageType.Info);
                EditorGUI.BeginChangeCheck();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Activated Packages"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                EditorGUILayout.LabelField(UIStyles.Content($"{_backupPackageCount} (set per package in Packages view)"), EditorStyles.wordWrappedLabel);
                if (ShowAdvanced())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Show")) ShowPackageMaintenance(MaintenanceOption.MarkedForBackup);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Override Patch Versions", "Will remove all but the latest patch version of an asset inside the same minor version (e.g. 5.4.3 instead of 5.4.2)"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.onlyLatestPatchVersion = EditorGUILayout.Toggle(AI.Config.onlyLatestPatchVersion, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Backups per Asset", "Number of versions to keep per asset"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.backupsPerAsset = EditorGUILayout.IntField(AI.Config.backupsPerAsset, GUILayout.Width(50));
                GUILayout.EndHorizontal();

                DrawFolder("Storage Folder", AI.Config.backupFolder, AI.GetBackupFolder(false), newFolder => AI.Config.backupFolder = newFolder, labelWidth);

                if (EditorGUI.EndChangeCheck()) AI.SaveConfig();
                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // AI
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            AI.Config.showAISettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showAISettings, "Artificial Intelligence");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showAISettings)
            {
                BeginIndentBlock();
                EditorGUI.BeginChangeCheck();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Activated Packages"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                EditorGUILayout.LabelField(UIStyles.Content($"{_aiPackageCount} (set per package in Packages view)"), EditorStyles.wordWrappedLabel);
                if (ShowAdvanced())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Show")) ShowPackageMaintenance(MaintenanceOption.MarkedForAI);
                }
                GUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Create Captions for", EditorStyles.boldLabel, GUILayout.Width(labelWidth));

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Prefabs", "Will create captions for prefabs."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.aiForPrefabs = EditorGUILayout.Toggle(AI.Config.aiForPrefabs, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Images", "Will create captions for image files."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.aiForImages = EditorGUILayout.Toggle(AI.Config.aiForImages, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Models", "Will create captions for model files."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.aiForModels = EditorGUILayout.Toggle(AI.Config.aiForModels, GUILayout.MaxWidth(cbWidth));
                GUILayout.EndHorizontal();

                if (ShowAdvanced())
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Log Created Captions", "Will print finished captions to the console."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.logAICaptions = EditorGUILayout.Toggle(AI.Config.logAICaptions, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Max Caption Length", "Some models can generate extremely long captions in boundary conditions. This setting caps the max length to preserve memory and display quality."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.aiMaxCaptionLength = EditorGUILayout.DelayedIntField(AI.Config.aiMaxCaptionLength, GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Pause Between Calculations", "AI inference requires significant resources and will bring a system to full load. Running constantly can lead to system crashes. Feel free to experiment with lower pauses."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.aiPause = EditorGUILayout.DelayedFloatField(AI.Config.aiPause, GUILayout.Width(50));
                    EditorGUILayout.LabelField("seconds", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Backend", "The technology to use for AI."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.aiBackend = EditorGUILayout.Popup(AI.Config.aiBackend, _aiBackendOptions, GUILayout.Width(100));
#if UNITY_2021_2_OR_NEWER
                if (AI.Config.aiBackend == 1)
                {
                    if (!AssetUtils.HasDefine(AI.DEFINE_SYMBOL_OLLAMA))
                    {
                        if (GUILayout.Button("Enable", GUILayout.ExpandWidth(false))) AssetUtils.AddDefine(AI.DEFINE_SYMBOL_OLLAMA);
                    }
                    else
                    {
                        if (GUILayout.Button("Disable", GUILayout.ExpandWidth(false))) AssetUtils.RemoveDefine(AI.DEFINE_SYMBOL_OLLAMA);
                    }
                }
#endif
                GUILayout.EndHorizontal();

                bool showTestImage = true;
                BeginIndentBlock();
                switch (AI.Config.aiBackend)
                {
                    case 0:
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content("Installation", "The model to be used for captioning. Local models are free of charge, but require a potent computer and graphics card."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        GUILayout.BeginVertical();
                        EditorGUILayout.HelpBox("This backend requires installing the Blip-Caption tool. It is free of charge and the guide can be found under the GitHub link below (Python, pipx, blip).", MessageType.Info);
                        if (GUILayout.Button("Salesforce Blip through Blip-Caption tool (local, free)", UIStyles.wrappedLinkLabel, GUILayout.ExpandWidth(true)))
                        {
                            Application.OpenURL("https://github.com/simonw/blip-caption");
                        }
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();

                        if (ShowAdvanced())
                        {
                            DrawFolder("Blip Folder", AI.Config.blipPath, null, newFolder => AI.Config.blipPath = newFolder, labelWidth);
                        }

                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content("Model", "The variant of the model that should be used."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        AI.Config.blipType = EditorGUILayout.Popup(AI.Config.blipType, _blipOptions, GUILayout.Width(100));
                        GUILayout.EndHorizontal();

                        if (ShowAdvanced())
                        {
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(UIStyles.Content("Ignore empty results", "Will not stop the captioning process when encountering empty captions which typically means the tooling is not properly set up."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                            AI.Config.aiContinueOnEmpty = EditorGUILayout.Toggle(AI.Config.aiContinueOnEmpty, GUILayout.MaxWidth(cbWidth));
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(UIStyles.Content("Use GPU", "Activate GPU acceleration if your system supports it. Otherwise only the CPU will be used. GPU support requires a patched blip version supporting GPU usage, see pull request 8."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                            AI.Config.blipUseGPU = EditorGUILayout.Toggle(AI.Config.blipUseGPU, GUILayout.MaxWidth(cbWidth));
                            GUILayout.EndHorizontal();
                        }

                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content("Bulk Process Size", "Number of files that are captioned by the model at once."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        AI.Config.blipChunkSize = EditorGUILayout.IntField(AI.Config.blipChunkSize, GUILayout.Width(50));
                        GUILayout.EndHorizontal();
                        break;

                    case 1:
#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
                        if (Intelligence.IsOllamaInstalled)
                        {
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(UIStyles.Content("Model", "The model to use. Must be listed in the Ollama library and support vision input and analysis."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                            GUILayout.BeginVertical();
                            GUILayout.BeginHorizontal();
                            AI.Config.ollamaModel = EditorGUILayout.TextField(AI.Config.ollamaModel, GUILayout.Width(150)).Trim();
                            if (!string.IsNullOrWhiteSpace(AI.Config.ollamaModel) && !Intelligence.OllamaModelDownloaded(AI.Config.ollamaModel))
                            {
                                EditorGUI.BeginDisabledGroup(Intelligence.DownloadingModel);
                                if (GUILayout.Button("Download Model", GUILayout.ExpandWidth(false))) DownloadOllamaModel();
                                EditorGUI.EndDisabledGroup();
                            }
                            if (EditorGUILayout.DropdownButton(UIStyles.Content("Installed"), FocusType.Keyboard, UIStyles.centerPopup, GUILayout.ExpandWidth(false))) ShowInstalledOllamaModels();
                            if (EditorGUILayout.DropdownButton(UIStyles.Content("Suggested"), FocusType.Keyboard, UIStyles.centerPopup, GUILayout.ExpandWidth(false))) ShowSuggestedOllamaModels();
                            if (ShowAdvanced() && Intelligence.OllamaModelDownloaded(AI.Config.ollamaModel))
                            {
                                if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Delete model"), GUILayout.Width(30)))
                                {
                                    DeleteOllamaModel();
                                }
                            }
                            GUILayout.EndHorizontal();
                            if (Intelligence.DownloadingModel)
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Space(3);
                                UIStyles.DrawProgressBar(
                                    (float)_curOllamaProgress / _maxOllamaProgress,
                                    $"{EditorUtility.FormatBytes(_curOllamaProgress)}/{EditorUtility.FormatBytes(_maxOllamaProgress)}",
                                    GUILayout.MaxWidth(150));
                                if (GUILayout.Button("Cancel", GUILayout.ExpandWidth(false))) Intelligence.OllamaDownloadToken?.Cancel();
                                GUILayout.EndHorizontal();
                            }
                            Model model = Intelligence.OllamaModels?.FirstOrDefault(m => m.Name == AI.Config.ollamaModel);
                            if (model != null && (model.Size / 1024 / 1024) + 2000 > SystemInfo.graphicsMemorySize) // add some buffer for system usage
                            {
                                EditorGUILayout.HelpBox($"The model probably requires more VRAM than your system has ({model.Size / 1024 / 1024:N0}Mb vs {SystemInfo.graphicsMemorySize:N0}Mb). This will lead to much slower performance.", MessageType.Warning);
                            }
                            if (GUILayout.Button("Model Catalog", UIStyles.wrappedLinkLabel, GUILayout.ExpandWidth(false)))
                            {
                                Application.OpenURL(Intelligence.OLLAMA_LIBRARY);
                            }
                            GUILayout.EndVertical();
                            GUILayout.EndHorizontal();
                        }
                        else
                        {
                            GUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(UIStyles.Content("Installation", ""), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                            GUILayout.BeginVertical();
                            EditorGUILayout.HelpBox("Ollama is not installed or active. Start it first and retry.", MessageType.Error);
                            GUILayout.BeginHorizontal();
                            if (GUILayout.Button("Refresh", GUILayout.ExpandWidth(false))) Intelligence.RefreshOllama();
                            if (GUILayout.Button("Ollama Website", UIStyles.wrappedLinkLabel, GUILayout.ExpandWidth(false)))
                            {
                                Application.OpenURL(Intelligence.OLLAMA_WEBSITE);
                            }
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            GUILayout.EndHorizontal();
                        }
#elif UNITY_2021_2_OR_NEWER
                        showTestImage = false;
                        EditorGUILayout.Space();
                        EditorGUILayout.HelpBox($"Ollama support requires additional libraries from Microsoft which can potentially conflict with other libraries currently in your project. Because of this it can be activated separately. In case you have issues after activation, easily turn it off again here or by removing the {AI.DEFINE_SYMBOL_OLLAMA} define symbol and redo the setup in a fresh or different project.", MessageType.Info);
#else
                        showTestImage = false;
                        EditorGUILayout.Space();
                        EditorGUILayout.HelpBox("Ollama support is only available with Unity 2021.2+.", MessageType.Error);
#endif
                        break;
                }
                EndIndentBlock();

                if (showTestImage)
                {
                    EditorGUILayout.Space();
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Test Image", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    GUILayout.BeginVertical(GUILayout.Width(120));
                    GUILayout.Box(Logo, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(100), GUILayout.MaxHeight(100));
                    EditorGUI.BeginDisabledGroup(_captionTestRunning);
                    if (GUILayout.Button("Create Caption", GUILayout.ExpandWidth(false))) TestCaptioning();
#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
                    if (AI.Config.aiBackend == 1)
                    {
                        EditorGUI.BeginDisabledGroup(!Intelligence.IsOllamaInstalled || Intelligence.LoadingModels);
                        if (GUILayout.Button("Model Tester...", GUILayout.ExpandWidth(false)))
                        {
                            ModelTestUI.ShowWindow();
                        }
                        EditorGUI.EndDisabledGroup();
                    }
#endif
                    EditorGUI.EndDisabledGroup();
                    GUILayout.EndVertical();
                    EditorGUILayout.LabelField(_captionTest, EditorStyles.wordWrappedLabel);
                    GUILayout.EndHorizontal();
                }
                if (EditorGUI.EndChangeCheck()) AI.SaveConfig();
                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // UI
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            AI.Config.showUISettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showUISettings, "UI Integration");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showUISettings)
            {
                BeginIndentBlock();

                EditorGUILayout.LabelField("'Assets' Menu", EditorStyles.largeLabel);

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Show Asset Inventory"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                if (AssetUtils.HasDefine(AI.DEFINE_SYMBOL_HIDE_AI))
                {
                    if (GUILayout.Button("Enable", GUILayout.ExpandWidth(false))) AssetUtils.RemoveDefine(AI.DEFINE_SYMBOL_HIDE_AI);
                }
                else
                {
                    if (GUILayout.Button("Disable", GUILayout.ExpandWidth(false))) AssetUtils.AddDefine(AI.DEFINE_SYMBOL_HIDE_AI);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Show Asset Browser"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                if (AssetUtils.HasDefine(AI.DEFINE_SYMBOL_HIDE_BROWSER))
                {
                    if (GUILayout.Button("Enable", GUILayout.ExpandWidth(false))) AssetUtils.RemoveDefine(AI.DEFINE_SYMBOL_HIDE_BROWSER);
                }
                else
                {
                    if (GUILayout.Button("Disable", GUILayout.ExpandWidth(false))) AssetUtils.AddDefine(AI.DEFINE_SYMBOL_HIDE_BROWSER);
                }
                GUILayout.EndHorizontal();

                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // locations
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            AI.Config.showLocationSettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showLocationSettings, "Locations");
            if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

            if (AI.Config.showLocationSettings)
            {
                BeginIndentBlock();
                EditorGUILayout.HelpBox("Per default all folders reside at the database location. Especially the cache, backup and preview folders can become quite large. You can move those to a different location with more space if needed. The database itself should be on the fastest available drive. If you change the locations, make sure to move the former contents along in case you want to keep the data.", MessageType.Info);
                EditorGUI.BeginChangeCheck();

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Database", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(AI.GetStorageFolder(), GUILayout.ExpandWidth(true));
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginDisabledGroup(AI.Actions.AnyActionsInProgress);
                if (GUILayout.Button("Change...", GUILayout.ExpandWidth(false))) SetDatabaseLocation();
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();

                DrawFolder("Backups", AI.Config.backupFolder, AI.GetBackupFolder(false), newFolder => AI.Config.backupFolder = newFolder, labelWidth);
                DrawFolder("Previews", AI.Config.previewFolder, AI.GetPreviewFolder(null, true), newFolder =>
                {
                    AI.Config.previewFolder = newFolder;
                    AI.RefreshPreviewCache();
                }, labelWidth);
                DrawFolder("Cache", AI.Config.cacheFolder, AI.GetMaterializeFolder(), newFolder => AI.Config.cacheFolder = newFolder, labelWidth);

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Limit Cache Size", "Flag if to regularly scan the cache folder and remove old items until the size limit is reached again. Only items that are not marked as 'Keep Extracted' will be removed."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                AI.Config.limitCacheSize = EditorGUILayout.Toggle(AI.Config.limitCacheSize, GUILayout.MaxWidth(cbWidth));
                if (AI.Config.limitCacheSize)
                {
                    AI.Config.cacheLimit = EditorGUILayout.DelayedIntField(AI.Config.cacheLimit, GUILayout.Width(50));
                    EditorGUILayout.LabelField("Gb", EditorStyles.miniLabel, GUILayout.Width(20));
                    EditorGUI.BeginDisabledGroup(AI.CacheLimiter.IsRunning);
                    if (GUILayout.Button(AI.CacheLimiter.IsRunning ? "Calculating..." : "Run Check", GUILayout.ExpandWidth(false)))
                    {
                        _ = AI.CacheLimiter.CheckAndClean();
                    }
                    if (AI.CacheLimiter.IsRunning && AI.CacheLimiter.CurrentSize > 0)
                    {
                        EditorGUILayout.LabelField($"Current Size: {EditorUtility.FormatBytes(AI.CacheLimiter.CurrentSize)}", EditorStyles.miniLabel);
                    }
                    else if (AI.CacheLimiter.CurrentSize > AI.CacheLimiter.GetLimit())
                    {
                        EditorGUILayout.LabelField($"The current cache size with {EditorUtility.FormatBytes(AI.CacheLimiter.CurrentSize)} exceeds the limit due to persistent cache entries ('Keep Cached' setting per package) that will not be cleaned up.", EditorStyles.wordWrappedMiniLabel);
                    }
                    EditorGUI.EndDisabledGroup();
                    GUILayout.FlexibleSpace();
                }
                GUILayout.EndHorizontal();

                EditorGUILayout.Space();
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(AI.UsedConfigLocation, GUILayout.ExpandWidth(true));
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Open", GUILayout.ExpandWidth(false))) EditorUtility.RevealInFinder(AI.UsedConfigLocation);
                GUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("To change, either copy the json file into your project to use a project-specific configuration or use the 'ASSETINVENTORY_CONFIG_PATH' environment variable to define a new global location (see documentation).", MessageType.Info);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                {
                    AI.SaveConfig();
                    AI.CacheLimiter.Enabled = AI.Config.limitCacheSize;
                    AI.CacheLimiter.SetLimit(AI.Config.cacheLimit);
                }

                EditorGUILayout.Space();
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("FTP/SFTP Connections", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                if (GUILayout.Button("Configure...", GUILayout.ExpandWidth(false)))
                {
                    FTPAdminUI.ShowWindow();
                }
                GUILayout.EndHorizontal();

                EndIndentBlock();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // advanced
            if (AI.Config.showAdvancedSettings || ShowAdvanced())
            {
                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                AI.Config.showAdvancedSettings = EditorGUILayout.BeginFoldoutHeaderGroup(AI.Config.showAdvancedSettings, "Advanced");
                if (EditorGUI.EndChangeCheck()) AI.SaveConfig();

                if (AI.Config.showAdvancedSettings)
                {
                    BeginIndentBlock();

                    EditorGUI.BeginChangeCheck();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Hide Advanced behind CTRL", "Will show only the main features in the UI permanently and hide all the rest until CTRL is held down."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.hideAdvanced = EditorGUILayout.Toggle(AI.Config.hideAdvanced, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Use Affiliate Links", "Will support the further development of the tool by allowing the usage of affiliate links whenever opening Asset Store pages."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.useAffiliateLinks = EditorGUILayout.Toggle(AI.Config.useAffiliateLinks, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Fetch Original Price", "Per default the current, potentially discounted price will be shown. If active, only the non-discounted is considered."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.showOriginalPrice = EditorGUILayout.Toggle(AI.Config.showOriginalPrice, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Concurrent Requests to Unity API", "Max number of requests that should be send at the same time to the Unity backend."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.maxConcurrentUnityRequests = EditorGUILayout.DelayedIntField(AI.Config.maxConcurrentUnityRequests, GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Online Metadata Refresh Cycle", "Number of days after which all metadata from the Asset Store should be refreshed to gather update information, new descriptions etc."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.assetStoreRefreshCycle = EditorGUILayout.DelayedIntField(AI.Config.assetStoreRefreshCycle, GUILayout.Width(50));
                    EditorGUILayout.LabelField("days");
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Preview Image Load Chunk Size", "Number of preview images to load in parallel."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.previewChunkSize = EditorGUILayout.DelayedIntField(AI.Config.previewChunkSize, GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Package State Refresh Speed", "Number of packages to gather update information for in the background per cycle."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.observationSpeed = EditorGUILayout.DelayedIntField(AI.Config.observationSpeed, GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Reporting Batch Size", "Amount of GUIDs that will be processed in a single request. Balance between performance and UI responsiveness."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.reportingBatchSize = EditorGUILayout.DelayedIntField(AI.Config.reportingBatchSize, GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Extract Single Audio Files", "Will only extract single audio files for preview and not the full archive. Advantage is less space requirements for caching but each preview will potentially again need to go through the full archive to extract, leading to more waiting time."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.extractSingleFiles = EditorGUILayout.Toggle(AI.Config.extractSingleFiles, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Updates For Indirect Dependencies", "Will show updates for packages even if they are indirect dependencies."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.showIndirectPackageUpdates = EditorGUILayout.Toggle(AI.Config.showIndirectPackageUpdates, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Updates For Custom Packages", "Will show custom packages in the list of available updates even though they cannot be updated automatically."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.showCustomPackageUpdates = EditorGUILayout.Toggle(AI.Config.showCustomPackageUpdates, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Enlarge Grid Tiles", "Will make grid tiles use all the available space and only snap to a different size if the tile size allows it."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.enlargeTiles = EditorGUILayout.Toggle(AI.Config.enlargeTiles, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Auto-Refresh Purchases", "Will update Asset Store purchases automatically at first start of the tool."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.autoRefreshPurchases = EditorGUILayout.Toggle(AI.Config.autoRefreshPurchases, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    if (AI.Config.autoRefreshPurchases)
                    {
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Refresh Period", "Number of hours after which purchases from the Asset Store should be refreshed."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        AI.Config.purchasesRefreshPeriod = EditorGUILayout.DelayedIntField(AI.Config.purchasesRefreshPeriod, GUILayout.Width(50));
                        EditorGUILayout.LabelField("hours");
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Auto-Refresh Metadata", "Will update the package metadata in the background when selecting a package to ensure the displayed information is up-to-date."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.autoRefreshMetadata = EditorGUILayout.Toggle(AI.Config.autoRefreshMetadata, GUILayout.MaxWidth(cbWidth));
                    GUILayout.EndHorizontal();

                    if (AI.Config.autoRefreshMetadata)
                    {
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Max Age", "Maximum age in hours after which the metadata is loaded again."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        AI.Config.metadataTimeout = EditorGUILayout.DelayedIntField(AI.Config.metadataTimeout, GUILayout.Width(50));
                        EditorGUILayout.LabelField("hours");
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Auto-Stop Cache Observer", "Will stop the cache observer after no new events came in for the specified time. This will save around 10% CPU background consumption. The only drawback will be that downloads started from the package manager will not be immediately be picked up by the tool anymore but only upon reselection."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.autoStopObservation = EditorGUILayout.Toggle(AI.Config.autoStopObservation, GUILayout.MaxWidth(cbWidth));
                    EditorGUILayout.LabelField(AI.IsObserverActive() ? "currently active" : "currently inactive", AI.IsObserverActive() ? EditorStyles.miniLabel : UIStyles.greyMiniLabel);
                    GUILayout.EndHorizontal();

                    if (AI.Config.autoStopObservation)
                    {
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content($"{UIStyles.INDENT}Timeout", "Time in seconds of no incoming file events after which the observer will be shut down."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                        AI.Config.observationTimeout = EditorGUILayout.DelayedIntField(AI.Config.observationTimeout, GUILayout.Width(50));
                        EditorGUILayout.LabelField("seconds");
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Tag Selection Window Height", "Height of the tag list window when selecting 'Add Tag...'"), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.tagListHeight = EditorGUILayout.DelayedIntField(AI.Config.tagListHeight, GUILayout.Width(50));
                    EditorGUILayout.LabelField("px");
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("No Package Text Below", "Don't show text for packages in grid mode when the tile size is below the value."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.noPackageTileTextBelow = EditorGUILayout.DelayedIntField(AI.Config.noPackageTileTextBelow, GUILayout.Width(50));
                    EditorGUILayout.LabelField("tile size");
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(UIStyles.Content("Exception Logging", "Will specify which errors should be logged to the console."), EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    AI.Config.logAreas = EditorGUILayout.MaskField(AI.Config.logAreas, _logOptions, GUILayout.MaxWidth(200));
                    GUILayout.EndHorizontal();

                    if (EditorGUI.EndChangeCheck())
                    {
                        AI.SaveConfig();
                        _requireAssetTreeRebuild = true;
                        if (!AI.Config.autoStopObservation) AI.StartCacheObserver();
                    }
                    EndIndentBlock();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            EditorGUILayout.Space();

            GUILayout.BeginVertical();
            EditorGUILayout.Space();
            GUILayout.BeginVertical("Update", "window", GUILayout.Width(UIStyles.INSPECTOR_WIDTH), GUILayout.ExpandHeight(false));
            UIBlock("settings.updateintro", () =>
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Ensure to regularly update the index and to fetch the newest updates from the Asset Store.", EditorStyles.wordWrappedLabel);
            });
            EditorGUILayout.Space();

            if (_usageCalculationInProgress)
            {
                EditorGUILayout.LabelField("Usage calculation in progress...", EditorStyles.boldLabel);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(_usageCalculation.CurrentMain);
            }
            else
            {
                if (AI.Actions.AnyActionsInProgress)
                {
                    EditorGUI.BeginDisabledGroup(AI.Actions.CancellationRequested);
                    if (GUILayout.Button("Stop Actions"))
                    {
                        AI.Actions.CancelAll();
                    }
                    EditorGUI.EndDisabledGroup();

                    // status
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Currently Running", EditorStyles.largeLabel);

                    List<UpdateAction> actions = AI.Actions.GetRunningActions();
                    foreach (UpdateAction action in actions)
                    {
                        foreach (ActionProgress progress in action.progress)
                        {
                            if (!progress.IsRunning()) continue;

                            EditorGUILayout.Space();

                            EditorGUILayout.LabelField(action.name, EditorStyles.boldLabel);
                            if (progress == null) continue;

                            UIStyles.DrawProgressBar(progress.MainProgress / (float)progress.MainCount, $"{progress.MainProgress:N0}/{progress.MainCount:N0} - {progress.CurrentMain}");

                            if (!string.IsNullOrWhiteSpace(progress.CurrentSub))
                            {
                                UIStyles.DrawProgressBar(progress.SubProgress / (float)progress.SubCount, $"{progress.SubProgress:N0}/{progress.SubCount:N0} - {progress.CurrentSub}");
                            }
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button(UIStyles.Content("Run Actions", "Run all enabled actions in one go and perform all necessary updates."), UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
                    {
                        PerformFullUpdate();
                    }
                    if (AI.Actions.LastActionUpdate != DateTime.MinValue)
                    {
                        UIBlock("settings.lastupdate", () =>
                        {
                            EditorGUILayout.Space();
                            EditorGUILayout.LabelField($"Last updated {StringUtils.GetRelativeTimeDifference(AI.Actions.LastActionUpdate)}", EditorStyles.centeredGreyMiniLabel);
                        });
                    }
                }
            }
            GUILayout.EndVertical();

            EditorGUILayout.Space();
            GUILayout.BeginVertical("Statistics", "window", GUILayout.Width(UIStyles.INSPECTOR_WIDTH));
            EditorGUILayout.Space();
            int labelWidth2 = 130;
            _statsScrollPos = GUILayout.BeginScrollView(_statsScrollPos, false, false);
            UIBlock("settings.statistics", () =>
            {
                DrawPackageStats(false);
                GUILabelWithText("Database Size", EditorUtility.FormatBytes(_dbSize), labelWidth2);
            });

            if (_indexedPackageCount < _indexablePackageCount && !AI.Actions.AnyActionsInProgress) // && !AI.Config.downloadAssets)
            {
                UIBlock("settings.hints.indexremaining", () =>
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("To index the remaining assets, download them first. You can multi-select packages in the Packages view to start a bulk download.", MessageType.Info);
                });
            }

            UIBlock("settings.diskspace", () =>
            {
                EditorGUILayout.Space();
                _showDiskSpace = EditorGUILayout.Foldout(_showDiskSpace, "Used Disk Space");
                if (_showDiskSpace)
                {
                    if (_lastFolderSizeCalculation != DateTime.MinValue)
                    {
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content("Previews", "Size of folder containing asset preview images."), EditorStyles.boldLabel, GUILayout.Width(120));
                        EditorGUILayout.LabelField(EditorUtility.FormatBytes(_previewSize), GUILayout.Width(80));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content("Cache", "Size of folder containing temporary cache. Can be deleted at any time."), EditorStyles.boldLabel, GUILayout.Width(120));
                        EditorGUILayout.LabelField(EditorUtility.FormatBytes(_cacheSize), GUILayout.Width(80));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content("Persistent Cache", "Size of extracted packages in cache that are marked 'extracted' and not automatically removed."), EditorStyles.boldLabel, GUILayout.Width(120));
                        EditorGUILayout.LabelField(EditorUtility.FormatBytes(_persistedCacheSize), GUILayout.Width(80));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(UIStyles.Content("Backups", "Size of folder containing asset preview images."), EditorStyles.boldLabel, GUILayout.Width(120));
                        EditorGUILayout.LabelField(EditorUtility.FormatBytes(_backupSize), GUILayout.Width(80));
                        GUILayout.EndHorizontal();

                        EditorGUILayout.LabelField("last updated " + _lastFolderSizeCalculation.ToShortTimeString(), EditorStyles.centeredGreyMiniLabel);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Not calculated yet....", EditorStyles.centeredGreyMiniLabel);
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginDisabledGroup(_calculatingFolderSizes);
                    if (GUILayout.Button(_calculatingFolderSizes ? "Calculating..." : "Refresh", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        CalcFolderSizes();
                    }
                    EditorGUI.EndDisabledGroup();
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            });

            EditorGUILayout.Space();
            _showMaintenance = EditorGUILayout.Foldout(_showMaintenance, "Maintenance");
            if (_showMaintenance)
            {
                EditorGUI.BeginDisabledGroup(AI.Actions.AnyActionsInProgress);
                UIBlock("settings.actions.maintenance", () =>
                {
                    if (GUILayout.Button("Maintenance Wizard..."))
                    {
                        MaintenanceUI.ShowWindow();
                    }
                });
                UIBlock("settings.actions.recreatepreviews", () =>
                {
                    if (GUILayout.Button("Previews Wizard..."))
                    {
                        PreviewWizardUI previewsUI = PreviewWizardUI.ShowWindow();
                        previewsUI.Init(null, _assets);
                    }
                });
                UIBlock("settings.actions.clearcache", () =>
                {
                    EditorGUILayout.Space();
                    EditorGUI.BeginDisabledGroup(AI.ClearCacheInProgress);
                    if (GUILayout.Button(UIStyles.Content("Clear Cache", "Will delete the 'Extracted' folder used for speeding up asset access. It will be recreated automatically when needed.")))
                    {
                        AI.ClearCache(() => UpdateStatistics(true));
                    }
                    EditorGUI.EndDisabledGroup();
                });
                UIBlock("settings.actions.cleardb", () =>
                {
                    if (GUILayout.Button(UIStyles.Content("Clear Database", "Will reset the database to its initial empty state. ALL data in the index will be lost.")))
                    {
                        if (EditorUtility.DisplayDialog("Confirm", "This will reset the database to its initial empty state. ALL data in the index will be lost.", "Proceed", "Cancel"))
                        {
                            if (DBAdapter.DeleteDB())
                            {
                                AssetUtils.ClearCache();

                                // delete previews since they will be incompatible due to different Ids
                                if (Directory.Exists(AI.GetPreviewFolder())) Directory.Delete(AI.GetPreviewFolder(), true);
                            }
                            else
                            {
                                EditorUtility.DisplayDialog("Error", "Database seems to be in use by another program and could not be cleared.", "OK");
                            }
                            UpdateStatistics(true);
                            _assets = new List<AssetInfo>();
                            _requireAssetTreeRebuild = true;
                        }
                    }
                });

                GUILayout.BeginHorizontal();
                UIBlock("settings.actions.resetconfig", () =>
                {
                    if (GUILayout.Button(UIStyles.Content("Reset Configuration", "Will reset the configuration to default values, also deleting all Additional Folder configurations.")))
                    {
                        AI.ResetConfig();
                    }
                });
                UIBlock("settings.actions.resetuiconfig", () =>
                {
                    if (GUILayout.Button(UIStyles.Content("Reset UI Customization", "Will reset the visibility of UI elements to initial default values.")))
                    {
                        AI.ResetUICustomization();
                    }
                });
                GUILayout.EndHorizontal();
                EditorGUILayout.Space();

                EditorGUI.BeginDisabledGroup(_cleanupInProgress);
                UIBlock("settings.actions.optimizedb", () =>
                {
                    if (GUILayout.Button("Optimize Database")) OptimizeDatabase();
                });
                EditorGUI.EndDisabledGroup();
                if (DBAdapter.IsDBOpen())
                {
                    UIBlock("settings.actions.closedb", () =>
                    {
                        if (GUILayout.Button(UIStyles.Content("Close Database", "Will allow to safely copy the database in the file system. Database will be reopened automatically upon activity.")))
                        {
                            DBAdapter.Close();
                        }
                    });
                }

                EditorGUI.EndDisabledGroup();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

#if UNITY_2021_2_OR_NEWER && ASSET_INVENTORY_OLLAMA
        private void DeleteOllamaModel()
        {
            if (!EditorUtility.DisplayDialog("Confirm Delete", $"Are you sure you want to delete the Ollama model '{AI.Config.ollamaModel}'?", "Delete", "Cancel"))
            {
                return;
            }
            _ = Intelligence.DeleteOllamaModel(AI.Config.ollamaModel);
        }

        private void DownloadOllamaModel()
        {
            _curOllamaProgress = 0;
            Task.Run(() => Intelligence.PullOllamaModel(AI.Config.ollamaModel, response =>
            {
                _curOllamaProgress = response.Completed;
                _maxOllamaProgress = response.Total;
            }));
        }

        private void ShowInstalledOllamaModels()
        {
            IEnumerable<Model> models = Intelligence.OllamaModels;

            GenericMenu menu = new GenericMenu();
            if (models != null)
            {
                foreach (Model model in models.OrderBy(m => m.Name, StringComparer.InvariantCultureIgnoreCase))
                {
                    menu.AddItem(new GUIContent($"{model.Name} ({EditorUtility.FormatBytes(model.Size)}, {model.Details.ParameterSize})"), false, () =>
                    {
                        AI.Config.ollamaModel = model.Name.Split(' ')[0];
                    });
                }
                menu.AddItem(GUIContent.none, false, () => { });
                menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshOllama);
            }
            else
            {
                if (Intelligence.LoadingModels)
                {
                    menu.AddDisabledItem(new GUIContent("Loading models..."));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Models could not be loaded"));
                }
            }
            menu.ShowAsContext();
        }

        private void ShowSuggestedOllamaModels()
        {
            GenericMenu menu = new GenericMenu();
            foreach (string model in Intelligence.SuggestedOllamaModels)
            {
                menu.AddItem(new GUIContent(model), false, () =>
                {
                    AI.Config.ollamaModel = model.Split(' ')[0];
                });
            }
            menu.ShowAsContext();
        }
#endif

        private async void TestCaptioning()
        {
            _captionTestRunning = true;
            _captionTest = "Running...";
            string path = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:Texture2D asset-inventory-logo").FirstOrDefault());
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            List<CaptionResult> captionResult = await CaptionCreator.CaptionImage(new List<string> {absolutePath}, AI.Config.ollamaModel);
            _captionTest = captionResult?.FirstOrDefault()?.caption;
            if (string.IsNullOrWhiteSpace(_captionTest))
            {
                _captionTest = "-Failed to create caption. Check tooling.-";
            }
            else
            {
                _captionTest = $"\"{_captionTest}\"";
            }
            _captionTestRunning = false;
        }

        private void OptimizeDatabase(bool initOnly = false)
        {
            if (!initOnly)
            {
                long savings = DBAdapter.Optimize();
                UpdateStatistics(true);
                EditorUtility.DisplayDialog("Success", $"Database was optimized. Size reduction: {EditorUtility.FormatBytes(savings)}\n\nMake sure to also delete your Library folder every now and then, especially after long indexing runs, to ensure Unity's asset database only contains what you really need for maximum performance.", "OK");
            }

            AppProperty lastOpt = new AppProperty("LastOptimization", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            DBAdapter.DB.InsertOrReplace(lastOpt);
        }

        private void SelectRelativeFolderMapping(RelativeLocation location)
        {
            string folder = EditorUtility.OpenFolderPanel("Select folder to map to", location.Location, "");
            if (!string.IsNullOrEmpty(folder))
            {
                location.SetLocation(Path.GetFullPath(folder));
                if (location.Id > 0)
                {
                    DBAdapter.DB.Execute("UPDATE RelativeLocation SET Location = ? WHERE Id = ?", location.Location, location.Id);
                }
                else
                {
                    DBAdapter.DB.Insert(location);
                }
                AI.LoadRelativeLocations();
            }
        }

        private async void CalcFolderSizes()
        {
            if (_calculatingFolderSizes) return;
            _calculatingFolderSizes = true;
            _lastFolderSizeCalculation = DateTime.Now;

            _backupSize = await AI.GetBackupFolderSize();
            _cacheSize = await AI.GetCacheFolderSize();
            _persistedCacheSize = await AI.GetPersistedCacheSize();
            _previewSize = await AI.GetPreviewFolderSize();

            _calculatingFolderSizes = false;
        }

        private void PerformFullUpdate()
        {
            AI.Actions.RunActions();
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
                    ReloadLookups();
                    PerformSearch();
                }

                return;
            }

            if (EditorUtility.DisplayDialog("Keep Old Database", "Should a new database be created or the current one moved?", "New", "Move..."))
            {
                AI.SwitchDatabase(targetFolder);
                ReloadLookups();
                PerformSearch();
                AssetStore.GatherAllMetadata();
                AssetStore.GatherProjectMetadata();
                return;
            }

            // show dedicated UI since the process is more complex now
            DBLocationUI relocateUI = DBLocationUI.ShowWindow();
            relocateUI.Init(targetFolder);
        }

        private IEnumerator UpdateStatisticsDelayed()
        {
            yield return null;
            UpdateStatistics(false);
        }

        private void UpdateStatistics(bool force)
        {
            if (!force && _assets != null && _tags != null && _dbSize > 0)
            {
                // check if assets were already correctly initialized since this method is also used for initial bootstrapping
                if (_assets.Any(a => a.PackageDownloader == null || (a.ParentId > 0 && a.ParentInfo == null)))
                {
                    AI.InitAssets(_assets);
                }
                return;
            }

            if (AI.DEBUG_MODE) Debug.LogWarning("Update Statistics");
            if (Application.isPlaying) return;

            _assets = AI.LoadAssets();
            _tags = Tagging.LoadTags();
            _packageCount = _assets.Count;
            _indexedPackageCount = _assets.Count(a => a.FileCount > 0);
            _subPackageCount = _assets.Count(a => a.ParentId > 0);
            _backupPackageCount = _assets.Count(a => a.Backup);
            _aiPackageCount = _assets.Count(a => a.UseAI);
            _deprecatedAssetsCount = _assets.Count(a => a.IsDeprecated);
            _abandonedAssetsCount = _assets.Count(a => a.IsAbandoned);
            _excludedAssetsCount = _assets.Count(a => a.Exclude);
            _registryPackageCount = _assets.Count(a => a.AssetSource == Asset.Source.RegistryPackage);
            _customPackageCount = _assets.Count(a => a.AssetSource == Asset.Source.CustomPackage || a.SafeName == Asset.NONE);

            // registry packages are too unpredictable to be counted and cannot be force indexed
            _indexablePackageCount = _packageCount - _abandonedAssetsCount - _registryPackageCount - _excludedAssetsCount;
            if (_indexablePackageCount < _indexedPackageCount) _indexablePackageCount = _indexedPackageCount;

            _packageFileCount = DBAdapter.DB.Table<AssetFile>().Count();

            // only load slow statistics on Index tab when nothing else is running
            if (AI.Config.tab == 3)
            {
                _dbSize = DBAdapter.GetDBSize();
            }
        }
    }
}