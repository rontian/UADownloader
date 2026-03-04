using System.IO;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    public class DirectorySizeManager
    {
        private const int MIN_ALIVE_TIME = 20; // minutes

        public bool Enabled = true;
        public bool IsRunning;
        public long CurrentSize;
        public DateTime LastCheckTime;

        private string _path;
        private long _byteLimit;
        private bool _isMonitoring;
        private readonly Func<string, bool> _validator;

        public DirectorySizeManager(string path, int gbLimit, Func<string, bool> validator)
        {
            _path = path;
            _validator = validator;

            SetLimit(gbLimit);
        }

        public void SetLimit(int gbLimit)
        {
            _byteLimit = gbLimit * 1024L * 1024L * 1024L;
        }

        public long GetLimit()
        {
            return _byteLimit;
        }

        public async Task StartMonitoring(int scanPeriod)
        {
            _isMonitoring = true;
            while (_isMonitoring)
            {
                await Task.Delay(scanPeriod);
                if (!_isMonitoring) break;

                _ = CheckAndClean();
            }
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
        }

        public async Task CheckAndClean()
        {
            if (IsRunning || !Enabled) return;
            IsRunning = true;
            try
            {
                CurrentSize = await IOUtils.GetFolderSize(_path);
                if (CurrentSize > _byteLimit)
                {
                    // Create DirectoryInfo objects once and sort by creation time
                    DirectoryInfo[] subDirs = Directory.GetDirectories(_path)
                        .Select(d => new DirectoryInfo(d))
                        .OrderBy(d => d.CreationTime)
                        .ToArray();

                    foreach (DirectoryInfo dirInfo in subDirs)
                    {
                        if (CurrentSize <= _byteLimit) break;

                        // check if folder is older than 10 minutes to ensure just created folders which might still be in use are not deleted
                        if (DateTime.Now - dirInfo.CreationTime < TimeSpan.FromMinutes(MIN_ALIVE_TIME))
                        {
                            continue;
                        }

                        if (!Enabled) break;
                        if (!_validator(dirInfo.FullName))
                        {
                            continue;
                        }

                        long subDirSize = await IOUtils.GetFolderSize(dirInfo.FullName);

                        // run non-blocking, no need to wait for deletion
                        string pathToDelete = dirInfo.FullName;
                        _ = Task.Run(() => IOUtils.DeleteFileOrDirectory(pathToDelete));

                        CurrentSize -= subDirSize;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not run cache limiter successfully: {e.Message}");
            }
            finally
            {
                IsRunning = false;
            }
            LastCheckTime = DateTime.Now;
        }
    }
}