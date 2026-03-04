using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Debug = UnityEngine.Debug;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetInventory
{
    public sealed class RemovalUI : EditorWindow
    {
        public static event Action OnUninstallDone;

        private List<AssetInfo> _assets;
        private Vector2 _scrollPos;
        private bool _running;
        private bool _cancellationRequested;
        private RemoveRequest _removeRequest;
        private AssetInfo _curInfo;
        private int _queueCount;
        private Action _callback;

        public static RemovalUI ShowWindow()
        {
            RemovalUI window = GetWindow<RemovalUI>("Removal Wizard");
            window.minSize = new Vector2(450, 200);

            return window;
        }

        public void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        public void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
        }

        private void OnBeforeAssemblyReload()
        {
            // right now not any state to persist actually, Unity will serialize the whole view correctly
        }

        private void OnAfterAssemblyReload()
        {
            if (_running)
            {
                // means there was an interactive import active which triggered a recompile, so let's continue
                BulkRemoveAssets(false);
            }
        }

        public void Init(List<AssetInfo> assets, Action callback = null)
        {
            _callback = callback;
            _assets = assets.Where(a => a.ParentId == 0)
                .OrderByDescending(a => a.AssetSource).ThenBy(a => a.GetDisplayName())
                .ToArray().ToList(); // break direct reference so that package list refresh does not clear import state

            // check if only sub-packages were selected, this is a valid scenario
            if (_assets.Count == 0)
            {
                _assets = assets.Where(a => a.ParentId > 0)
                    .OrderByDescending(a => a.AssetSource).ThenBy(a => a.GetDisplayName())
                    .ToArray().ToList(); // break direct reference so that package list refresh does not clear import state
            }

            _queueCount = 0;
            foreach (AssetInfo info in _assets)
            {
                if (info.SafeName == Asset.NONE) continue;

                info.ImportState = AssetInfo.ImportStateOptions.Queued;
                _queueCount++;
            }
            BulkRemoveAssets(false);
        }

        public void OnGUI()
        {
            EditorGUILayout.Space();
            if (_assets == null || _assets.Count == 0)
            {
                EditorGUILayout.HelpBox("Select packages in the Asset Inventory for removal first.", MessageType.Info);
                return;
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel, GUILayout.Width(85));
            EditorGUILayout.LabelField(_assets.Count.ToString());
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
            foreach (AssetInfo info in _assets)
            {
                if (info.SafeName == Asset.NONE) continue;

                GUILayout.BeginHorizontal();
                if (info.AssetSource == Asset.Source.RegistryPackage)
                {
                    if (info.InstalledPackageVersion() != null)
                    {
                        EditorGUILayout.LabelField(new GUIContent($"{info.GetDisplayName()} - {info.InstalledPackageVersion()}", info.SafeName));
                    }
                    else
                    {
                        EditorGUILayout.LabelField(new GUIContent($"{info.GetDisplayName()} - checking", info.SafeName));
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(new GUIContent(info.GetDisplayName(), info.GetLocation(true)));
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(info.ImportState.ToString(), GUILayout.Width(80));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            EditorGUILayout.Space();

            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_running);
            if (GUILayout.Button(UIStyles.Content("Remove", "Start removal process"))) BulkRemoveAssets(true);
            EditorGUI.EndDisabledGroup();
            if (_running && GUILayout.Button("Cancel All"))
            {
                _cancellationRequested = true; // will not always work if there was a recompile in between
                _running = false;
            }
            GUILayout.EndHorizontal();
        }

        private async void BulkRemoveAssets(bool resetState)
        {
            if (resetState)
            {
                _assets
                    .Where(a => a.ImportState == AssetInfo.ImportStateOptions.Cancelled || a.ImportState == AssetInfo.ImportStateOptions.Failed)
                    .ForEach(a => a.ImportState = AssetInfo.ImportStateOptions.Queued);
            }

            // importing will be set if there was a recompile during an ongoing import
            IEnumerable<AssetInfo> removalQueue = _assets.Where(a => a.ImportState == AssetInfo.ImportStateOptions.Queued || a.ImportState == AssetInfo.ImportStateOptions.Uninstalling)
                .Where(a => a.SafeName != Asset.NONE)
                .ToList();
            if (removalQueue.Count() == 0) return;

            _running = true;
            _cancellationRequested = false;

            await DoBulkRemoval(removalQueue, true);
            bool allDone = removalQueue.All(a => a.ImportState == AssetInfo.ImportStateOptions.Uninstalled);
            _running = false;

            OnUninstallDone?.Invoke();

            // custom one-time callback handler
            _callback?.Invoke();
            _callback = null;

            if (allDone) Close();
        }

        private async Task DoBulkRemoval(IEnumerable<AssetInfo> queue, bool allAutomatic)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (AssetInfo info in queue)
                {
                    _curInfo = info;

                    if (info.ImportState != AssetInfo.ImportStateOptions.Uninstalling)
                    {
                        info.ImportState = AssetInfo.ImportStateOptions.Uninstalling;

                        if (info.AssetSource == Asset.Source.RegistryPackage)
                        {
                            _removeRequest = RemovePackage(info);
                            if (_removeRequest == null) continue;

                            EditorApplication.update += RemoveProgress;
                        }
                    }

                    // wait until done
                    while (!_cancellationRequested && info.ImportState == AssetInfo.ImportStateOptions.Uninstalling)
                    {
                        await Task.Delay(25);
                    }

                    if (info.ImportState == AssetInfo.ImportStateOptions.Uninstalling) info.ImportState = AssetInfo.ImportStateOptions.Queued;
                    if (_cancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error uninstalling packages: {e.Message}");
            }

            // handle potentially pending uninstalls and put them back in the queue
            _assets.ForEach(info =>
            {
                if (info.ImportState == AssetInfo.ImportStateOptions.Uninstalling) info.ImportState = AssetInfo.ImportStateOptions.Queued;
            });

            if (allAutomatic)
            {
                // set inactive since the next line will trigger a recompile and will otherwise continue the import
                _running = false;
            }

            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
#if UNITY_2020_3_OR_NEWER
            Client.Resolve();
#endif
        }

        private static RemoveRequest RemovePackage(AssetInfo info)
        {
            if (info.PackageSource == PackageSource.Embedded)
            {
                // embedded packages need to be deleted manually
                PackageInfo pInfo = AssetStore.GetPackageInfo(info);
                FileUtil.DeleteFileOrDirectory(pInfo.resolvedPath);
                AssetDatabase.Refresh();
                return null;
            }
            return Client.Remove(info.SafeName);
        }

        private void RemoveProgress()
        {
            if (!_removeRequest.IsCompleted) return;

            EditorApplication.update -= RemoveProgress;

            if (_removeRequest.Status == StatusCode.Success)
            {
                _curInfo.ImportState = AssetInfo.ImportStateOptions.Uninstalled;
            }
            else
            {
                _curInfo.ImportState = AssetInfo.ImportStateOptions.Failed;
                Debug.LogError($"Uninstalling {_curInfo} failed: {_removeRequest.Error.message}");
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}