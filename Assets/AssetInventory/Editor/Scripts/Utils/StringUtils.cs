using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AssetInventory
{
    public static class StringUtils
    {
        private const long SEC = TimeSpan.TicksPerSecond;
        private const long MIN = TimeSpan.TicksPerMinute;
        private const long HOUR = TimeSpan.TicksPerHour;
        private const long DAY = TimeSpan.TicksPerDay;
        private static readonly Regex CAMEL_CASE_R1 = new Regex(@"(?<=[a-z])(?=[A-Z])|(?<=[0-9])(?=[A-Z])|(?<=[A-Z])(?=[0-9])|(?<=[0-9])(?=[a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CAMEL_CASE_R2 = new Regex(@"(?<= [A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CAMEL_CASE_R3 = new Regex(@"(?<=[^\s])(?=[(])|(?<=[)])(?=[^\s])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Precompiled regex patterns for performance
        private static readonly Regex ESCAPE_SQL_LIKE_PATTERN = new Regex(@"(like\s+'[^']*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ESCAPE_SQL_LIKE_ESCAPE_PATTERN = new Regex(@"(like\s+'[^']*')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex STRIP_TAGS_PATTERN = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Regex STRIP_TAGS_WITH_CONTENT_PATTERN = new Regex("<[^>]+?>.*?</[^>]+?>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex STRIP_UNICODE_PATTERN = new Regex("&#.*?;", RegexOptions.Compiled);
        private static readonly Regex NORMALIZE_LINE_BREAKS_PATTERN = new Regex(@"\r\n?|\n", RegexOptions.Compiled);
        private static readonly Regex WHITESPACE_BEFORE_NEWLINE_PATTERN = new Regex(@"[ \t]+\n", RegexOptions.Compiled);
        private static readonly Regex MULTIPLE_NEWLINES_PATTERN = new Regex(@"\n{3,}", RegexOptions.Compiled);
        private static readonly Regex MULTIPLE_WHITESPACE_PATTERN = new Regex(@"\s+", RegexOptions.Compiled);

        public static string ExtractTokens(string input, string tokenName, List<string> tokenValues)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(tokenName)) return input;

            // Pattern to match tokens with different value formats:
            // 1. Quoted strings (single or double quotes) - handles escaped tokens with spaces
            // 2. Non-whitespace sequences - handles simple tokens without spaces
            // 3. Empty tokens (just the token name followed by colon) - should be removed but not added to values
            string pattern = $@"\b{Regex.Escape(tokenName)}:((?:'[^']*'|""[^""]*""|\S+)?)";

            // Use a MatchEvaluator to both capture the token and remove it in one go. Make token name matching case-insensitive.
            string result = Regex.Replace(input, pattern, match =>
            {
                string value = match.Groups[1].Value;

                // Skip empty tokens (don't add them to the values list)
                if (string.IsNullOrEmpty(value))
                {
                    return string.Empty;
                }

                // Remove quotes if present (for escaped tokens)
                if ((value.StartsWith("'") && value.EndsWith("'")) ||
                    (value.StartsWith("\"") && value.EndsWith("\"")))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                tokenValues.Add(value);

                // Return an empty string to remove this token from the original text.
                return string.Empty;
            }, RegexOptions.IgnoreCase);

            // remove any excess whitespace created by token removal
            result = MULTIPLE_WHITESPACE_PATTERN.Replace(result, " ").Trim();

            return result;
        }

        public static string ExtractTokens(string input, IEnumerable<string> tokenNames, List<string> tokenValues)
        {
            if (string.IsNullOrEmpty(input) || tokenNames == null) return input;

            List<string> names = new List<string>();
            foreach (string n in tokenNames)
            {
                if (!string.IsNullOrEmpty(n)) names.Add(Regex.Escape(n));
            }
            if (names.Count == 0) return input;

            string pattern = $@"\b(?:{string.Join("|", names)}):((?:'[^']*'|""[^""]*""|\S+)?)";

            string result = Regex.Replace(input, pattern, match =>
            {
                string value = match.Groups[1].Value;
                if (string.IsNullOrEmpty(value)) return string.Empty;
                if ((value.StartsWith("'") && value.EndsWith("'")) || (value.StartsWith("\"") && value.EndsWith("\"")))
                {
                    value = value.Substring(1, value.Length - 2);
                }
                tokenValues.Add(value);
                return string.Empty;
            }, RegexOptions.IgnoreCase);

            result = MULTIPLE_WHITESPACE_PATTERN.Replace(result, " ").Trim();
            return result;
        }

        public static string GetRelativeTimeDifference(DateTime date)
        {
            return GetRelativeTimeDifference(date, DateTime.Now);
        }

        public static string GetRelativeTimeDifference(DateTime date1, DateTime date2)
        {
            long ticks = date2.Ticks - date1.Ticks;
            if (ticks < 0) ticks = -ticks;

            if (ticks >= DAY)
            {
                int v = (int)(ticks / DAY);
                return v == 1 ? "1 day ago" : v.ToString(CultureInfo.InvariantCulture) + " days ago";
            }
            if (ticks >= HOUR)
            {
                int v = (int)(ticks / HOUR);
                return v == 1 ? "1 hour ago" : v.ToString(CultureInfo.InvariantCulture) + " hours ago";
            }
            if (ticks >= MIN)
            {
                int v = (int)(ticks / MIN);
                return v == 1 ? "1 minute ago" : v.ToString(CultureInfo.InvariantCulture) + " minutes ago";
            }

            int s = (int)(ticks / SEC);
            return s == 1 ? "1 second ago" : s.ToString(CultureInfo.InvariantCulture) + " seconds ago";
        }

        /// <summary>
        /// Formats a duration given in seconds into a human-readable string.
        /// </summary>
        /// <param name="totalSeconds">Total duration in seconds (e.g., from AudioClip.length)</param>
        /// <param name="maxComponents">Maximum number of time components to display (e.g., 2 = "1 Hour 30 Min"). Default is 2, set to -1 for all.</param>
        /// <returns>Formatted time string like "1 Hour 30 Min", "5 Min 42 Sec", or "15 Sec"</returns>
        public static string FormatDuration(float totalSeconds, int maxComponents = 2)
        {
            if (totalSeconds < 0) totalSeconds = 0;

            // For durations < 10 seconds, show fractions
            bool showFractions = totalSeconds < 10;

            int days = (int)(totalSeconds / 86400);
            totalSeconds -= days * 86400;

            int hours = (int)(totalSeconds / 3600);
            totalSeconds -= hours * 3600;

            int minutes = (int)(totalSeconds / 60);
            totalSeconds -= minutes * 60;

            int seconds = (int)totalSeconds;
            float fractionalSeconds = totalSeconds;

            List<string> parts = new List<string>();

            if (days > 0)
            {
                parts.Add(days.ToString(CultureInfo.InvariantCulture) + " Day" + (days == 1 ? "" : "s"));
            }
            if (hours > 0)
            {
                parts.Add(hours.ToString(CultureInfo.InvariantCulture) + " Hour" + (hours == 1 ? "" : "s"));
            }
            if (minutes > 0)
            {
                parts.Add(minutes.ToString(CultureInfo.InvariantCulture) + " Min");
            }
            if (seconds > 0 || parts.Count == 0) // Always show seconds if it's the only component or if there's time left
            {
                if (showFractions && parts.Count == 0)
                {
                    // Show one decimal place for durations < 10 seconds
                    parts.Add(fractionalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " Sec");
                }
                else
                {
                    parts.Add(seconds.ToString(CultureInfo.InvariantCulture) + " Sec");
                }
            }

            // Limit to maxComponents if specified
            if (maxComponents > 0 && parts.Count > maxComponents)
            {
                parts = parts.GetRange(0, maxComponents);
            }

            return string.Join(" ", parts);
        }

        public static string EscapeSQL(string input)
        {
            // Replace underscores with escaped underscores inside 'like' clauses
            input = ESCAPE_SQL_LIKE_PATTERN.Replace(input, m =>
            {
                string likeClause = m.Groups[1].Value;
                likeClause = likeClause.Replace("_", "\\_");
                return likeClause;
            });

            // Add ESCAPE '\' behind each 'like' clause
            input = ESCAPE_SQL_LIKE_ESCAPE_PATTERN.Replace(input, "$1 ESCAPE '\\'");

            return input;
        }

        // drop-in for Unity 2019 where splitting is only possible by char and Contains does not support StringComparison
#if !UNITY_2021_2_OR_NEWER
        public static string[] Split(this string source, string separator, StringSplitOptions options = StringSplitOptions.None)
            => source.Split(new[] {separator}, options);

        public static bool Contains(this string source, string toCheck, StringComparison comparison)
        {
            if (source == null || toCheck == null) return false;
            return source.IndexOf(toCheck, comparison) >= 0;
        }
#endif

        public static string Truncate(this string value, int maxLength)
        {
            if (value == null) return null;

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }

        public static string[] Split(string input, char[] separators)
        {
            if (string.IsNullOrEmpty(input)) return Array.Empty<string>();

            string[] parts = input.Split(separators, StringSplitOptions.None);

            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }

            return parts;
        }

        public static List<string> FlattenCommaSeparated(IEnumerable<string> inputs)
        {
            List<string> result = new List<string>();
            if (inputs == null) return result;

            foreach (string v in inputs)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                foreach (string part in Split(v, new[] {','}))
                {
                    if (!string.IsNullOrEmpty(part)) result.Add(part);
                }
            }

            return result;
        }

        public static string CamelCaseToWords(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string result = CAMEL_CASE_R1.Replace(input, " ");
            result = CAMEL_CASE_R2.Replace(result, " ");
            result = CAMEL_CASE_R3.Replace(result, " ");

            string[] words = result.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                words[i] = CapitalizeFirstLetter(words[i]);
            }

            return string.Join(" ", words);
        }

        private static string CapitalizeFirstLetter(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;

            // Preserve the case of the rest of the word
            return char.ToUpper(word[0]) + word.Substring(1);
        }

        public static string GetShortHash(string input, int length = 6)
        {
            if (length < 1 || length > 10)
            {
                throw new ArgumentOutOfRangeException(nameof (length), "Length must be between 1 and 10.");
            }

            // Compute a simple hash from the input string.
            int hash = 0;
            foreach (char c in input)
            {
                hash = (hash * 31 + c); // Use a prime number multiplier
            }

            // Calculate the modulus based on the desired length
            int mod = (int)Math.Pow(10, length);

            // Reduce the hash to a number with the desired length
            int shortHash = Math.Abs(hash) % mod;

            // Return the hash as a string, padded with leading zeros if necessary
            return shortHash.ToString($"D{length}");
        }

        public static bool IsUrl(string url)
        {
            return Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }

        public static bool IsUnicode(this string input)
        {
            // Iterate directly over string without allocating char array
            foreach (char c in input)
            {
                if (c > 255) return true;
            }
            return false;
        }

        public static string StripTags(string input, bool removeContentBetweenTags = false)
        {
            if (removeContentBetweenTags)
            {
                return STRIP_TAGS_WITH_CONTENT_PATTERN.Replace(input, string.Empty);
            }
            return STRIP_TAGS_PATTERN.Replace(input, string.Empty);
        }

        public static string StripUnicode(string input)
        {
            return STRIP_UNICODE_PATTERN.Replace(input, string.Empty);
        }

        public static string RemoveTrailing(this string source, string text)
        {
            if (source == null)
            {
                Debug.LogError("This should not happen, source is null");
                return null;
            }

            // Handle empty text case - return source unchanged
            if (string.IsNullOrEmpty(text)) return source;

            // Calculate final length once to avoid multiple substring allocations
            int textLength = text.Length;
            int endIndex = source.Length;

            while (endIndex >= textLength && source.Substring(endIndex - textLength, textLength) == text)
            {
                endIndex -= textLength;
            }

            return endIndex == source.Length ? source : source.Substring(0, endIndex);
        }

        public static string ToLowercaseFirstLetter(this string input)
        {
            if (string.IsNullOrEmpty(input) || char.IsLower(input[0]))
            {
                return input;
            }

            return char.ToLower(input[0]) + input.Substring(1);
        }

        public static string ToLabel(string input)
        {
            string result = input;

            // Normalize line breaks to \n
            result = NORMALIZE_LINE_BREAKS_PATTERN.Replace(result, "\n");

            // Translate some HTML tags
            result = result.Replace("<br>", "\n");
            result = result.Replace("</br>", "\n");
            result = result.Replace("<p>", "\n\n");
            result = result.Replace("<p >", "\n\n");
            result = result.Replace("<li>", "\n* ");
            result = result.Replace("<li >", "\n* ");
            result = result.Replace("&nbsp;", " ");
            result = result.Replace("&amp;", "&");

            // Remove remaining tags and also unicode tags
            result = StripUnicode(StripTags(result));

            // Remove whitespace from empty lines
            result = WHITESPACE_BEFORE_NEWLINE_PATTERN.Replace(result, "\n");

            // Ensure at max two consecutive line breaks
            result = MULTIPLE_NEWLINES_PATTERN.Replace(result, "\n\n");

            return result.Trim();
        }

        public static string GetEnvVar(string key)
        {
            string value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(value)) value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(value)) value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine);

            return value;
        }

        /// <summary>
        /// Formats bytes into a human-readable string (e.g., "1.5 MB", "256 KB").
        /// Thread-safe alternative to EditorUtility.FormatBytes.
        /// </summary>
        /// <param name="bytes">Number of bytes to format</param>
        /// <returns>Formatted string with appropriate unit (B, KB, MB, GB, TB)</returns>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";

            string[] sizes = {"B", "KB", "MB", "GB", "TB"};
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            // Format with up to 1 decimal place, but drop .0
            string formatted = len.ToString(len % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
            return $"{formatted} {sizes[order]}";
        }
    }
}