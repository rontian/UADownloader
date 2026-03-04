using System;
using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;

namespace AssetInventory
{
    public sealed class GitHandler
    {
        private readonly string _path;
        public bool IsValid = false;
        public string LastError;

        public List<Reference> Refs;
        public string[] Branches;
        public string[] ShortBranches;
        public string[] Tags;
        public string[] ShortTags;
        public string[] PRs;
        public string[] ShortPRs;

        public GitHandler(string path)
        {
            _path = path;
        }

        public void GatherRemoteInfo()
        {
            try
            {
                Refs = LibGit2Sharp.Repository.ListRemoteReferences(_path).ToList();
                IsValid = true;
            }
            catch (Exception e)
            {
                IsValid = false;
                LastError = e.Message;
                return;
            }

            Branches = Refs
                .Where(r => r.IsLocalBranch)
                .Select(r => r.CanonicalName)
                .ToArray();
            ShortBranches = Branches
                .Select(b => b.Replace("refs/heads/", "").Replace("/", "-"))
                .ToArray();
            Tags = Refs
                .Where(r => r.IsTag)
                .Select(r => r.CanonicalName)
                .ToArray();
            ShortTags = Tags
                .Select(t => t.Replace("refs/tags/", "").Replace("/", "-"))
                .ToArray();
            PRs = Refs
                .Where(r => r.CanonicalName.StartsWith("refs/pull/"))
                .Select(r => r.CanonicalName)
                .ToArray();
            ShortPRs = PRs
                .Select(pr => pr.Replace("refs/pull/", "").Replace("/head", "").Replace("/", "-"))
                .ToArray();
        }
    }
}