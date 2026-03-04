using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class MaintenanceUI : EditorWindow
    {
        public static event Action OnMaintenanceDone;

        private readonly List<Validator> _validators = new List<Validator>();
        private Vector2 _checksScrollPos;
        private int _fixeableItems;

        public MaintenanceUI()
        {
            Init();
        }

        public static MaintenanceUI ShowWindow()
        {
            MaintenanceUI window = GetWindow<MaintenanceUI>("Maintenance Wizard");
            window.minSize = new Vector2(550, 300);

            return window;
        }

        private void Init()
        {
            _validators.Clear();
            _validators.Add(new ScheduledPreviewRecreationValidator());
            _validators.Add(new SubPackageRenderPipelineValidator());
            _validators.Add(new OutdatedPackagesValidator());
            _validators.Add(new UseOriginalPreviewValidator());
            _validators.Add(new EmbedOriginalPreviewValidator());
            _validators.Add(new IncorrectPreviewsValidator());
            _validators.Add(new MissingPreviewFilesValidator());
            _validators.Add(new OrphanedTagAssignmentsValidator());
            _validators.Add(new DeletedAssetFilesValidator());
            _validators.Add(new OrphanedAssetFilesValidator());
            _validators.Add(new OrphanedPackagesValidator());
            _validators.Add(new OrphanedCacheFoldersValidator());
            _validators.Add(new OrphanedPreviewFoldersValidator());
            _validators.Add(new OrphanedPreviewFilesValidator());
            _validators.Add(new WrongDimensionPreviewFilesValidator());
            _validators.Add(new MissingAudioLengthValidator());
            _validators.Add(new MissingParentPackagesValidator());
            _validators.Add(new UnindexedSubPackagesValidator());
            _validators.Add(new ReassignedMediaIndexValidator());
            _validators.Add(new DuplicateMediaIndexValidator());
            _validators.Add(new SuspiciousBackupsValidator());
            _validators.Add(new CorruptDatabaseValidator());
        }

        private void ScanAll(bool fastOnly)
        {
            _validators.Where(v => v.IsVisible()).ForEach(v =>
            {
                if (fastOnly && v.Speed != Validator.ValidatorSpeed.Fast) return;
                if (v.CurrentState == Validator.State.Idle || v.CurrentState == Validator.State.Completed)
                {
                    v.CancellationRequested = false;
                    v.Validate();
                }
            });
        }

        private void FixAll()
        {
            _validators.Where(v => v.IsVisible()).ForEach(v =>
            {
                if (v.CurrentState == Validator.State.Completed && v.IssueCount > 0 && v.Fixable)
                {
                    v.CancellationRequested = false;
                    v.Fix();
                    OnMaintenanceDone?.Invoke();
                }
            });
        }

        public void OnGUI()
        {
            EditorGUI.BeginDisabledGroup(false);

            EditorGUILayout.LabelField("This wizard will scan your database, previews and files for issues and provide means to repair or clean these up.", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Run All", UIStyles.mainButton, GUILayout.ExpandWidth(false), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                ScanAll(false);
            }
            if (GUILayout.Button("Run Only Fast Scans*", GUILayout.ExpandWidth(false), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                ScanAll(true);
            }
            EditorGUI.BeginDisabledGroup(_fixeableItems == 0);
            if (GUILayout.Button("Fix All", GUILayout.ExpandWidth(false), GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                FixAll();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space();
            _fixeableItems = 0;
            _checksScrollPos = GUILayout.BeginScrollView(_checksScrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
            foreach (Validator validator in _validators.Where(v => v.IsVisible()))
            {
                GUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(validator.Name + (validator.Speed == Validator.ValidatorSpeed.Fast ? "*" : ""), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(validator.Description, EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.BeginHorizontal();
                if (validator.CurrentState == Validator.State.Idle || validator.CurrentState == Validator.State.Completed)
                {
                    if (GUILayout.Button("Scan", GUILayout.ExpandWidth(false)))
                    {
                        validator.CancellationRequested = false;
                        validator.Validate();
                    }
                }
                else
                {
                    if (GUILayout.Button("Cancel", GUILayout.ExpandWidth(false)))
                    {
                        validator.CancellationRequested = true;
                    }
                }
                EditorGUILayout.LabelField("Result:", GUILayout.Width(40));
                Color oldColor = GUI.color;
                GUI.color = Color.yellow;
                switch (validator.CurrentState)
                {
                    case Validator.State.Idle:
                        EditorGUILayout.LabelField("-not scanned yet-");
                        break;

                    case Validator.State.Scanning:
                        EditorGUILayout.LabelField("scanning...");
                        break;

                    case Validator.State.Completed:
                        GUI.color = validator.IssueCount == 0 ? Color.green : UIStyles.errorColor;
                        EditorGUILayout.LabelField($"{validator.IssueCount:N0} issues found" + (validator.IssueCount > 0 && !validator.Fixable ? " (not automatically fixable)" : ""));
                        GUI.color = oldColor;

                        if (validator.IssueCount > 0)
                        {
                            EditorGUI.BeginDisabledGroup(validator.CurrentState == Validator.State.Fixing);
                            if (GUILayout.Button("Show...", GUILayout.ExpandWidth(false)))
                            {
                                switch (validator.Type)
                                {
                                    case Validator.ValidatorType.DB:
                                        EditorUtility.DisplayDialog("Issue List (Top 50)", string.Join("\n", validator.DBIssues.Take(50).Select(i => $"{(string.IsNullOrWhiteSpace(i.Path) ? i.GetDisplayName() : i.Path)} ({i.Id})")), "OK");
                                        break;

                                    case Validator.ValidatorType.FileSystem:
                                        EditorUtility.DisplayDialog("Issue List (Top 50)", string.Join("\n", validator.FileIssues.Take(50)), "OK");
                                        break;

                                }
                            }
                            if (validator.Fixable)
                            {
                                _fixeableItems++;
                                if (GUILayout.Button(validator.FixCaption, GUILayout.ExpandWidth(false)))
                                {
                                    validator.CancellationRequested = false;
                                    validator.Fix();
                                    OnMaintenanceDone?.Invoke();
                                }
                            }
                            EditorGUI.EndDisabledGroup();
                        }
                        break;

                    case Validator.State.Fixing:
                        GUI.color = UIStyles.errorColor;
                        EditorGUILayout.LabelField("fixing...");
                        break;

                }
                GUI.color = oldColor;
                EditorGUILayout.EndHorizontal();
                GUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
            GUILayout.EndScrollView();

            EditorGUI.EndDisabledGroup();
        }
    }
}