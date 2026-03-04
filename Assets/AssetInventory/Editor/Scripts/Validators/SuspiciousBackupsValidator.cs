using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    public sealed class SuspiciousBackupsValidator : Validator
    {
        public SuspiciousBackupsValidator()
        {
            Type = ValidatorType.FileSystem;
            Speed = ValidatorSpeed.Fast;
            Name = "Suspicious Backups";
            Description = "Checks for backups that have a newer version but an older date. This in most cases indicates incorrect versioning by asset authors, e.g. 1.1.412 released before 1.1.52.";
            FixCaption = "Remove Older Backups";
        }

        public override async Task Validate()
        {
            CurrentState = State.Scanning;
            FileIssues = new List<string>();

            Dictionary<int, List<BackupInfo>> state = AssetBackup.GatherState();
            foreach (KeyValuePair<int, List<BackupInfo>> pair in state)
            {
                for (int i = pair.Value.Count - 1; i >= 1; i--)
                {
                    try
                    {
                        FileInfo fOld = new FileInfo(pair.Value[i].location);
                        FileInfo fNew = new FileInfo(pair.Value[i - 1].location);
                        if (fNew.LastWriteTime < fOld.LastWriteTime)
                        {
                            FileIssues.Add(pair.Value[i - 1].location);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error checking backup file {pair.Value[i].location}: {e.Message}");
                    }
                }
            }

            await Task.Yield();
            CurrentState = State.Completed;
        }

        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            foreach (string file in FileIssues)
            {
                IOUtils.TryDeleteFile(file);
            }

            await Task.Yield();
            CurrentState = State.Idle;
        }
    }
}