using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class DeletedAssetFilesValidator : Validator
    {
        public DeletedAssetFilesValidator()
        {
            Type = ValidatorType.DB;
            Name = "Deleted Asset Files";
            Description = "Scans all indexed files that are in media folders but don't exist anymore on the file system.";
        }

        public override async Task Validate()
        {
            CurrentState = State.Scanning;
            DBIssues = await GatherDeletedFiles();
            CurrentState = State.Completed;
        }

        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            // delete all deleted asset files
            foreach (AssetInfo issue in DBIssues)
            {
                if (CancellationRequested) break;
                DBAdapter.DB.Delete<AssetFile>(issue.Id);
            }

            await Validate();
        }

        private static async Task<List<AssetInfo>> GatherDeletedFiles()
        {
            string query = "SELECT AF.* FROM AssetFile AF JOIN Asset A ON AF.AssetId = A.Id WHERE A.AssetSource = 2";
            List<AssetInfo> files = DBAdapter.DB.Query<AssetInfo>(query).ToList();

            ConcurrentBag<AssetInfo> missing = new ConcurrentBag<AssetInfo>();
            int total = files.Count;
            int processed = 0;
            int progressId = MetaProgress.Start("Checking for deleted files");

            await Task.Run(() =>
            {
                Parallel.ForEach(
                    files,
                    new ParallelOptions {MaxDegreeOfParallelism = Environment.ProcessorCount},
                    file =>
                    {
                        if (!File.Exists(AI.DeRel(file.Path))) missing.Add(file);

                        int current = Interlocked.Increment(ref processed);
                        if (current % 5000 == 0) MetaProgress.Report(progressId, current, total, file.FileName);
                    });
            });
            MetaProgress.Remove(progressId);

            return missing.ToList();
        }
    }
}