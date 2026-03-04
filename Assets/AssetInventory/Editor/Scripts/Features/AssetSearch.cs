using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace AssetInventory
{
    public static class AssetSearch
    {
        private const string PACKAGE_TAG_JOIN_CLAUSE = "inner join TagAssignment as tap on (Asset.Id = tap.TargetId and tap.TagTarget = 0)";
        private const string FILE_TAG_JOIN_CLAUSE = "inner join TagAssignment as taf on (AssetFile.Id = taf.TargetId and taf.TagTarget = 1)";

        public class Options
        {
            // Inputs originating from UI/state
            public string SearchPhrase = string.Empty;
            public Dictionary<string, string> SearchVariables = null; // variable name → current value
            public int SelectedPackageSRPs = 0;
            public string SearchWidth = string.Empty;
            public bool CheckMaxWidth = false;
            public string SearchHeight = string.Empty;
            public bool CheckMaxHeight = false;
            public string SearchLength = string.Empty;
            public bool CheckMaxLength = false;
            public string SearchSize = string.Empty;
            public bool CheckMaxSize = false;
            public int SelectedPackageTag = 0;
            public int SelectedFileTag = 0;
            public string[] TagNames = Array.Empty<string>();
            public List<Tag> Tags = new List<Tag>();
            public int SelectedPackageTypes = 0;
            public int SelectedPublisher = 0;
            public string[] PublisherNames = Array.Empty<string>();
            public int SelectedAsset = 0;
            public string[] AssetNames = Array.Empty<string>();
            public int SelectedCategory = 0;
            public string[] CategoryNames = Array.Empty<string>();
            public int SelectedColorOption = 0;
            public UnityEngine.Color SelectedColor = UnityEngine.Color.white;
            public int SelectedImageType = 0;
            public string[] ImageTypeOptions = Array.Empty<string>();
            public int SelectedPreviewFilter = 0; // 0=both, 1=has preview, 2=no preview
            public string RawSearchType = null; // precomputed caller value or null
            public bool IgnoreExcludedExtensions = false;
            public int CurrentPage = 1;
            public int MaxResults = 0; // 0 disables limit
            public InMemoryMode InMemory = InMemoryMode.None;
            public List<AssetInfo> AllAssets = new List<AssetInfo>(); // required for ResolveParents
        }

        public enum InMemoryMode
        {
            None,
            Init,
            Active
        }

        public class Result
        {
            public List<AssetInfo> Files = new List<AssetInfo>();
            public int ResultCount;
            public int OriginalResultCount;
            public string Error;
            public InMemoryMode InMemory;
        }

        public static Result Execute(Options opt)
        {
            AI.Init();
            Result result = new Result {InMemory = opt.InMemory};

            List<string> wheres = new List<string>();
            List<object> args = new List<object>();
            string packageTagJoin = "";
            string fileTagJoin = "";
            string computedFields = "";
            string lastWhere = null;
            string phrase = opt.SearchPhrase ?? string.Empty;

            // Substitute search variables before processing
            if (opt.SearchVariables != null && opt.SearchVariables.Count > 0)
            {
                try
                {
                    phrase = VariableResolver.ReplaceVariables(phrase, opt.SearchVariables);
                }
                catch (Exception ex)
                {
                    result.Error = $"Variable substitution error: {ex.Message}";
                    return result;
                }
            }

            wheres.Add("Asset.Exclude=0");

            List<string> withAllPT = new List<string>();
            phrase = StringUtils.ExtractTokens(phrase, "withallpt", withAllPT);
            List<string> withAnyPT = new List<string>();
            phrase = StringUtils.ExtractTokens(phrase, new[] {"withanypt", "pt"}, withAnyPT);
            List<string> withNonePT = new List<string>();
            phrase = StringUtils.ExtractTokens(phrase, new[] {"withnonept", "withnopt"}, withNonePT);

            List<string> withAllFT = new List<string>();
            phrase = StringUtils.ExtractTokens(phrase, "withallft", withAllFT);
            List<string> withAnyFT = new List<string>();
            phrase = StringUtils.ExtractTokens(phrase, new[] {"withanyft", "ft"}, withAnyFT);
            List<string> withNoneFT = new List<string>();
            phrase = StringUtils.ExtractTokens(phrase, new[] {"withnoneft", "withnoft"}, withNoneFT);

            List<string> withAllPTTags = StringUtils.FlattenCommaSeparated(withAllPT);
            if (withAllPTTags.Count > 0)
            {
                foreach (string tag in withAllPTTags)
                {
                    wheres.Add("exists (select tap2.Id from TagAssignment as tap2 where Asset.Id = tap2.TargetId and tap2.TagTarget = 0 and tap2.TagId = ?)");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name.ToLowerInvariant() == tag.ToLowerInvariant())?.Id);
                }
            }
            List<string> withAnyPTTags = StringUtils.FlattenCommaSeparated(withAnyPT);
            if (withAnyPTTags.Count > 0)
            {
                List<string> conditions = new List<string>();
                foreach (string tag in withAnyPTTags)
                {
                    conditions.Add("tap.TagId = ?");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name.ToLowerInvariant() == tag.ToLowerInvariant())?.Id);
                }
                wheres.Add("(" + string.Join(" OR ", conditions) + ")");
                packageTagJoin = PACKAGE_TAG_JOIN_CLAUSE;
            }
            List<string> withNonePTTags = StringUtils.FlattenCommaSeparated(withNonePT);
            if (withNonePTTags.Count > 0)
            {
                List<string> paramCount = new List<string>();
                foreach (string tag in withNonePTTags)
                {
                    paramCount.Add("?");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name.ToLowerInvariant() == tag.ToLowerInvariant())?.Id);
                }
                wheres.Add("not exists (select tap2.Id from TagAssignment as tap2 where Asset.Id = tap2.TargetId and tap2.TagTarget = 0 and tap2.TagId in (" + string.Join(",", paramCount) + "))");
            }

            List<string> withAllFTTags = StringUtils.FlattenCommaSeparated(withAllFT);
            if (withAllFTTags.Count > 0)
            {
                foreach (string tag in withAllFTTags)
                {
                    wheres.Add("exists (select taf2.Id from TagAssignment as taf2 where AssetFile.Id = taf2.TargetId and taf2.TagTarget = 1 and taf2.TagId = ?)");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name.ToLowerInvariant() == tag.ToLowerInvariant())?.Id);
                }
            }
            List<string> withAnyFTTags = StringUtils.FlattenCommaSeparated(withAnyFT);
            if (withAnyFTTags.Count > 0)
            {
                List<string> conditions = new List<string>();
                foreach (string tag in withAnyFTTags)
                {
                    conditions.Add("taf.TagId = ?");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name.ToLowerInvariant() == tag.ToLowerInvariant())?.Id);
                }
                wheres.Add("(" + string.Join(" OR ", conditions) + ")");
                fileTagJoin = FILE_TAG_JOIN_CLAUSE;
            }
            List<string> withNoneFTTags = StringUtils.FlattenCommaSeparated(withNoneFT);
            if (withNoneFTTags.Count > 0)
            {
                List<string> paramCount = new List<string>();
                foreach (string tag in withNoneFTTags)
                {
                    paramCount.Add("?");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name.ToLowerInvariant() == tag.ToLowerInvariant())?.Id);
                }
                wheres.Add("not exists (select taf2.Id from TagAssignment as taf2 where AssetFile.Id = taf2.TargetId and taf2.TagTarget = 1 and taf2.TagId in (" + string.Join(",", paramCount) + "))");
            }

            // inline tags: packages
            List<string> parsedPackageTags = new List<string>();
            phrase = StringUtils.ExtractTokens(phrase, "pt", parsedPackageTags);
            if (parsedPackageTags.Count > 0)
            {
                List<string> conditions = new List<string>();
                foreach (string tag in parsedPackageTags)
                {
                    conditions.Add("tap.TagId = ?");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name.ToLowerInvariant() == tag.ToLowerInvariant())?.Id);
                }
                wheres.Add("(" + string.Join(" OR ", conditions) + ")");
                packageTagJoin = PACKAGE_TAG_JOIN_CLAUSE;
            }

            // inline tags: files
            List<string> parsedFileTags = new List<string>();
            phrase = StringUtils.ExtractTokens(phrase, "ft", parsedFileTags);
            if (parsedFileTags.Count > 0)
            {
                List<string> conditions = new List<string>();
                foreach (string tag in parsedFileTags)
                {
                    conditions.Add("taf.TagId = ?");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name.ToLowerInvariant() == tag.ToLowerInvariant())?.Id);
                }
                wheres.Add("(" + string.Join(" OR ", conditions) + ")");
                fileTagJoin = FILE_TAG_JOIN_CLAUSE;
            }

            // SRP filters
            switch (opt.SelectedPackageSRPs)
            {
                case 0:
                    break;
                
                case 1: // auto-detect
                    if (AI.Config.excludeIncompatibleSRPs)
                    {
                        bool isURP = AssetUtils.IsOnURP();
                        bool isHDRP = AssetUtils.IsOnHDRP();
                        bool isBIRP = !isURP && !isHDRP;

                        if (isBIRP)
                        {
                            wheres.Add("(Asset.BIRPCompatible = 1 OR (Asset.URPCompatible = 0 AND Asset.HDRPCompatible = 0))");
                        }
                        else if (isURP)
                        {
                            wheres.Add("(Asset.BIRPCompatible = 1 OR Asset.URPCompatible = 1 OR (Asset.URPCompatible = 0 AND Asset.HDRPCompatible = 0))");
                        }
                        else if (isHDRP)
                        {
                            wheres.Add("(Asset.BIRPCompatible = 1 OR Asset.HDRPCompatible = 1 OR (Asset.URPCompatible = 0 AND Asset.HDRPCompatible = 0))");
                        }
                    }
                    break;

                case 3:
                    wheres.Add("Asset.BIRPCompatible=1");
                    break;

                case 4:
                    wheres.Add("Asset.URPCompatible=1");
                    break;

                case 5:
                    wheres.Add("Asset.HDRPCompatible=1");
                    break;
            }

            // numeric filters first
            if (IsFilterApplicable("Width", opt.RawSearchType) && !string.IsNullOrWhiteSpace(opt.SearchWidth) && int.TryParse(opt.SearchWidth, out int width) && width > 0)
            {
                string comp = opt.CheckMaxWidth ? "<=" : ">=";
                wheres.Add($"AssetFile.Width > 0 and AssetFile.Width {comp} ?");
                args.Add(width);
            }
            if (IsFilterApplicable("Height", opt.RawSearchType) && !string.IsNullOrWhiteSpace(opt.SearchHeight) && int.TryParse(opt.SearchHeight, out int height) && height > 0)
            {
                string comp = opt.CheckMaxHeight ? "<=" : ">=";
                wheres.Add($"AssetFile.Height > 0 and AssetFile.Height {comp} ?");
                args.Add(height);
            }
            if (IsFilterApplicable("Length", opt.RawSearchType) && !string.IsNullOrWhiteSpace(opt.SearchLength) && float.TryParse(opt.SearchLength, out float length) && length > 0)
            {
                string comp = opt.CheckMaxLength ? "<=" : ">=";
                wheres.Add($"AssetFile.Length > 0 and AssetFile.Length {comp} ?");
                args.Add(length);
            }
            if (!string.IsNullOrWhiteSpace(opt.SearchSize) && int.TryParse(opt.SearchSize, out int size) && size > 0)
            {
                string comp = opt.CheckMaxSize ? "<=" : ">=";
                wheres.Add($"AssetFile.Size > 0 and AssetFile.Size {comp} ?");
                args.Add(size * 1024);
            }

            // dropdown tags (ignored if inline tags are present)
            bool anyInlinePT = parsedPackageTags.Count > 0 || withAllPTTags.Count > 0 || withAnyPTTags.Count > 0 || withNonePTTags.Count > 0;
            if (!anyInlinePT)
            {
                if (opt.SelectedPackageTag == 1)
                {
                    wheres.Add("not exists (select tap.Id from TagAssignment as tap where Asset.Id = tap.TargetId and tap.TagTarget = 0)");
                }
                else if (opt.SelectedPackageTag > 1 && opt.TagNames.Length > opt.SelectedPackageTag)
                {
                    string[] arr = opt.TagNames[opt.SelectedPackageTag].Split('/');
                    string tag = arr[arr.Length - 1];
                    wheres.Add("tap.TagId = ?");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name == tag)?.Id);
                    packageTagJoin = PACKAGE_TAG_JOIN_CLAUSE;
                }
            }
            bool anyInlineFT = parsedFileTags.Count > 0 || withAllFTTags.Count > 0 || withAnyFTTags.Count > 0 || withNoneFTTags.Count > 0;
            if (!anyInlineFT)
            {
                if (opt.SelectedFileTag == 1)
                {
                    wheres.Add("not exists (select taf.Id from TagAssignment as taf where AssetFile.Id = taf.TargetId and taf.TagTarget = 1)");
                }
                else if (opt.SelectedFileTag > 1 && opt.TagNames.Length > opt.SelectedFileTag)
                {
                    string[] arr = opt.TagNames[opt.SelectedFileTag].Split('/');
                    string tag = arr[arr.Length - 1];
                    wheres.Add("taf.TagId = ?");
                    args.Add(opt.Tags.FirstOrDefault(t => t.Name == tag)?.Id);
                    fileTagJoin = FILE_TAG_JOIN_CLAUSE;
                }
            }

            // package types
            switch (opt.SelectedPackageTypes)
            {
                case 1:
                    wheres.Add("Asset.AssetSource != ?");
                    args.Add(Asset.Source.RegistryPackage);
                    break;
                case 2:
                    wheres.Add("Asset.AssetSource = ?");
                    args.Add(Asset.Source.AssetStorePackage);
                    break;
                case 3:
                    wheres.Add("Asset.AssetSource = ?");
                    args.Add(Asset.Source.RegistryPackage);
                    break;
                case 4:
                    wheres.Add("Asset.AssetSource = ?");
                    args.Add(Asset.Source.CustomPackage);
                    break;
                case 5:
                    wheres.Add("Asset.AssetSource = ?");
                    args.Add(Asset.Source.Directory);
                    break;
                case 6:
                    wheres.Add("Asset.AssetSource = ?");
                    args.Add(Asset.Source.Archive);
                    break;
                case 7:
                    wheres.Add("Asset.AssetSource = ?");
                    args.Add(Asset.Source.AssetManager);
                    break;
            }

            // publisher filter
            if (opt.SelectedPublisher > 0 && opt.PublisherNames.Length > opt.SelectedPublisher)
            {
                string[] arr = opt.PublisherNames[opt.SelectedPublisher].Split('/');
                string publisher = arr[arr.Length - 1];
                wheres.Add("Asset.SafePublisher = ?");
                args.Add($"{publisher}");
            }

            // asset filter
            if (opt.SelectedAsset > 0 && opt.AssetNames.Length > opt.SelectedAsset)
            {
                string[] arr = opt.AssetNames[opt.SelectedAsset].Split('/');
                string asset = arr[arr.Length - 1];
                if (asset.LastIndexOf('[') > 0)
                {
                    string assetId = asset.Substring(asset.LastIndexOf('[') + 1);
                    assetId = assetId.Substring(0, assetId.Length - 1);
                    if (AI.Config.searchSubPackages)
                    {
                        wheres.Add("(Asset.Id = ? or Asset.ParentId = ?)");
                        args.Add(int.Parse(assetId));
                        args.Add(int.Parse(assetId));
                    }
                    else
                    {
                        wheres.Add("Asset.Id = ?");
                        args.Add(int.Parse(assetId));
                    }
                }
                else
                {
                    wheres.Add("Asset.SafeName = ?");
                    args.Add($"{asset}");
                }
            }

            // category filter
            if (opt.SelectedCategory > 0 && opt.CategoryNames.Length > opt.SelectedCategory)
            {
                wheres.Add("Asset.DisplayCategory = ?");
                args.Add(opt.CategoryNames[opt.SelectedCategory]);
            }

            // color range
            if (opt.SelectedColorOption > 0)
            {
                wheres.Add("AssetFile.Hue >= ?");
                wheres.Add("AssetFile.Hue <= ?");
                args.Add(opt.SelectedColor.ToHue() - AI.Config.hueRange / 2f);
                args.Add(opt.SelectedColor.ToHue() + AI.Config.hueRange / 2f);
            }

            // image type
            if (IsFilterApplicable("ImageType", opt.RawSearchType) && opt.SelectedImageType > 0)
            {
                computedFields = ", CASE WHEN INSTR(AssetFile.FileName, '.') > 0 THEN Lower(SUBSTR(AssetFile.FileName, 1, INSTR(AssetFile.FileName, '.') - 1)) ELSE Lower(AssetFile.FileName) END AS FileNameWithoutExtension";
                string[] patterns = TextureNameSuggester.suffixPatterns[opt.ImageTypeOptions[opt.SelectedImageType].ToLowerInvariant()];
                List<string> patternWheres = new List<string>();
                foreach (string pattern in patterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern)) continue;
                    patternWheres.Add("FileNameWithoutExtension like ? ESCAPE '\\\'");
                    args.Add("%" + pattern.Replace("_", "\\_"));
                }
                wheres.Add("(" + string.Join(" or ", patternWheres) + ")");
            }

            // text search
            if (!string.IsNullOrWhiteSpace(opt.SearchPhrase))
            {
                List<string> searchFields = new List<string>();
                switch (AI.Config.searchField)
                {
                    case 0: searchFields.Add("AssetFile.Path"); break;
                    case 1: searchFields.Add("AssetFile.FileName"); break;
                }
                if (AI.Config.searchAICaptions && AI.Actions.CreateAICaptions) searchFields.Add("AssetFile.AICaption");
                if (AI.Config.searchPackageNames) searchFields.Add("Asset.DisplayName");

                // check for sqlite escaping requirements
                string escape = "";
                if (phrase.Contains("_"))
                {
                    if (!phrase.StartsWith("=")) phrase = phrase.Replace("_", "\\_");
                    escape = "ESCAPE '\\\'";
                }

                if (phrase.StartsWith("=")) // expert mode
                {
                    if (phrase.Length > 1)
                    {
                        phrase = StringUtils.EscapeSQL(phrase);
                        lastWhere = phrase.Substring(1);
                    }
                }
                else if (phrase.StartsWith("~")) // exact mode
                {
                    string term = phrase.Substring(1);
                    List<string> conditions = new List<string>();
                    searchFields.ForEach(s =>
                    {
                        conditions.Add($"COALESCE({s}, '') like ? {escape}");
                        args.Add($"%{term}%");
                    });
                    wheres.Add("(" + string.Join(" OR ", conditions) + ")");
                }
                else
                {
                    string[] fuzzyWords = phrase
                        .Split(' ')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();
                    foreach (string fuzzyWord in fuzzyWords)
                    {
                        if (fuzzyWord.StartsWith("-"))
                        {
                            List<string> conditions = new List<string>();
                            searchFields.ForEach(s =>
                            {
                                conditions.Add($"COALESCE({s}, '') not like ? {escape}");
                                args.Add($"%{fuzzyWord.Substring(1)}%");
                            });
                            wheres.Add("(" + string.Join(" AND ", conditions) + ")");
                        }
                        else
                        {
                            string term = fuzzyWord;
                            if (term.StartsWith("+")) term = term.Substring(1);
                            List<string> conditions = new List<string>();
                            searchFields.ForEach(s =>
                            {
                                conditions.Add($"COALESCE({s}, '') like ? {escape}");
                                args.Add($"%{term}%");
                            });
                            wheres.Add("(" + string.Join(" OR ", conditions) + ")");
                        }
                    }
                }
            }

            // type filtering based on raw type
            string rawType = opt.RawSearchType;
            if (rawType != null)
            {
                string[] type = rawType.Split('/');
                if (type.Length > 1)
                {
                    wheres.Add("AssetFile.Type = ?");
                    args.Add(type.Last());
                }
                else if (Enum.TryParse(rawType, out AI.AssetGroup assetGroup))
                {
                    if (AI.TypeGroups.TryGetValue(assetGroup, out string[] group))
                    {
                        // optimize SQL slightly for cases where only one type is checked
                        if (group.Length == 1)
                        {
                            wheres.Add("AssetFile.Type = ?");
                            args.Add(group[0]);
                        }
                        else
                        {
                            // sqlite does not support binding lists, parameters must be spelled out
                            List<string> paramCount = new List<string>();
                            foreach (string t in group)
                            {
                                paramCount.Add("?");
                                args.Add(t);
                            }
                            wheres.Add("AssetFile.Type in (" + string.Join(",", paramCount) + ")");
                        }
                    }
                }
            }

            // excluded extensions
            if (!opt.IgnoreExcludedExtensions && AI.Config.excludeExtensions && AI.Config.searchType == 0 && !string.IsNullOrWhiteSpace(AI.Config.excludedExtensions))
            {
                string[] extensions = AI.Config.excludedExtensions.Split(';');
                List<string> paramCount = new List<string>();
                foreach (string ext in extensions)
                {
                    paramCount.Add("?");
                    args.Add(ext.Trim());
                }
                wheres.Add("AssetFile.Type not in (" + string.Join(",", paramCount) + ")");
            }

            // preview filter (has preview / no preview)
            switch (opt.SelectedPreviewFilter)
            {
                case 2: // has preview
                    wheres.Add("AssetFile.PreviewState in (1, 2, 3)"); // Provided, Redo, Custom
                    break;
                case 3: // no preview
                    wheres.Add("AssetFile.PreviewState not in (1, 2, 3)"); // not Provided, Redo, Custom
                    break;
            }

            // ordering, can only be done on DB side since post-processing results would only work on the paged results which is incorrect
            string orderBy = "order by ";
            switch (AI.Config.sortField)
            {
                case 0: orderBy += "AssetFile.Path"; break;
                case 1: orderBy += "AssetFile.FileName"; break;
                case 2: orderBy += "AssetFile.Size"; break;
                case 3: orderBy += "AssetFile.Type"; break;
                case 4: orderBy += "AssetFile.Length"; break;
                case 5: orderBy += "AssetFile.Width"; break;
                case 6: orderBy += "AssetFile.Height"; break;
                case 7:
                    orderBy += "AssetFile.Hue";
                    wheres.Add("AssetFile.Hue >=0");
                    break;
                case 8: orderBy += "Asset.DisplayCategory"; break;
                case 9: orderBy += "Asset.LastRelease"; break;
                case 10: orderBy += "Asset.AssetRating"; break;
                case 11: orderBy += "Asset.RatingCount"; break;
                default: orderBy = ""; break;
            }
            if (!string.IsNullOrEmpty(orderBy))
            {
                orderBy += " COLLATE NOCASE";
                if (AI.Config.sortDescending) orderBy += " desc";
                orderBy += ", AssetFile.Path"; // always sort by path in case of equality of first level sorting
            }
            if (!string.IsNullOrEmpty(lastWhere)) wheres.Add(lastWhere);

            string where = wheres.Count > 0 ? "where " + string.Join(" and ", wheres) : "";
            string baseQuery = $"from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId {packageTagJoin} {fileTagJoin} {where}";
            string countQuery = $"select count(*){computedFields} {baseQuery}";
            string dataQuery = $"select *, AssetFile.Id as Id{computedFields} {baseQuery} {orderBy}";
            if (opt.MaxResults > 0 && opt.InMemory == InMemoryMode.None) dataQuery += $" limit {opt.MaxResults} offset {(opt.CurrentPage - 1) * opt.MaxResults}";

            try
            {
                result.Error = null;
                result.ResultCount = DBAdapter.DB.ExecuteScalar<int>($"{countQuery}", args.ToArray());
                result.OriginalResultCount = result.ResultCount; // store original result count for later use

                if (opt.MaxResults > 0 && opt.InMemory != InMemoryMode.None && result.ResultCount > AI.Config.maxInMemoryResults)
                {
                    result.InMemory = InMemoryMode.None;
                    dataQuery += $" limit {opt.MaxResults} offset {(opt.CurrentPage - 1) * opt.MaxResults}";
                    EditorUtility.DisplayDialog("Search Result Limit Exceeded",
                        $"There are more than {AI.Config.maxInMemoryResults:N0} search results (configured in search settings). In-Memory mode was therefore disabled again.",
                        "OK");
                }

                result.Files = DBAdapter.DB.Query<AssetInfo>($"{dataQuery}", args.ToArray());
            }
            catch (SQLite.SQLiteException e)
            {
                result.Error = e.Message;
            }

            AI.ResolveParents(result.Files, opt.AllAssets);
            return result;
        }

        public static bool IsFilterApplicable(string filterName, string rawSearchType)
        {
            string searchType = rawSearchType;
            if (searchType == null) return true;
            if (AI.FilterRestriction.TryGetValue(filterName, out string[] restrictions))
            {
                return restrictions.Contains(searchType);
            }
            return true;
        }
    }
}