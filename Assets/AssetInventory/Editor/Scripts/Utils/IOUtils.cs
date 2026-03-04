using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_2021_2_OR_NEWER
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using CompressionType = SharpCompress.Common.CompressionType;
#endif
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace AssetInventory
{
    public static class IOUtils
    {
        private const string LONG_PATH_PREFIX = @"\\?\";
        private const string LONG_PATH_UNC_PREFIX = @"\\?\UNC\";

        public static DriveInfo GetDriveInfoForPath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return null;
            folderPath = ToShortPath(folderPath);

            folderPath = Path.GetFullPath(folderPath);
            DriveInfo bestMatch = null;

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                string root = drive.RootDirectory.FullName;
                if (folderPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    if (bestMatch == null || root.Length > bestMatch.RootDirectory.FullName.Length)
                    {
                        bestMatch = drive;
                    }
                }
            }
            if (bestMatch == null) Debug.LogError($"No drive found for the given path: {folderPath}");

            return bestMatch;
        }

        public static bool IsSameDrive(string path1, string path2)
        {
            DriveInfo drive1 = GetDriveInfoForPath(path1);
            DriveInfo drive2 = GetDriveInfoForPath(path2);
            return string.Equals(drive1.Name, drive2.Name, StringComparison.OrdinalIgnoreCase);
        }

        public static long GetFreeSpace(string folderPath)
        {
            try
            {
                DriveInfo drive = GetDriveInfoForPath(folderPath);
                if (drive == null) return -1;
                return drive.AvailableFreeSpace;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static string NormalizeRelative(string path)
        {
            string[] parts = path.Split(new[] {'/', '\\'}, StringSplitOptions.RemoveEmptyEntries);
            Stack<string> stack = new Stack<string>();
            foreach (string part in parts)
            {
                if (part == "..")
                {
                    stack.Pop();
                }
                else if (part != ".")
                {
                    stack.Push(part);
                }
            }
            return string.Join("/", stack.Reverse());
        }

        public static string ToLongPath(string path)
        {
            if (path == null) return null;

#if UNITY_EDITOR_WIN && UNITY_2020_2_OR_NEWER // support was only added in that Mono version
            // see https://learn.microsoft.com/en-us/answers/questions/240603/c-app-long-path-support-on-windows-10-post-1607-ne
            path = path.Replace("/", "\\"); // in case later concatenations added /
            if (path.StartsWith(LONG_PATH_PREFIX, StringComparison.Ordinal)) return path;
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string withoutSlashes = path.Substring(2);
                return $"{LONG_PATH_UNC_PREFIX}{withoutSlashes}";
            }
            return $"{LONG_PATH_PREFIX}{path}";
#else
            return path;
#endif
        }

        public static string ToShortPath(string path)
        {
#if UNITY_EDITOR_WIN && UNITY_2020_2_OR_NEWER
            if (path == null) return null;

            // handle UNC long-path prefix \\?\UNC\server\share\…
            if (path.StartsWith(LONG_PATH_UNC_PREFIX, StringComparison.Ordinal))
            {
                string withoutUncPrefix = path.Substring(LONG_PATH_UNC_PREFIX.Length);
                string uncPath = @"\\" + withoutUncPrefix;
                return uncPath; // UNC paths must use backslashes
            }
            return path.Replace(LONG_PATH_PREFIX, string.Empty).Replace("\\", "/");
#else
            return path;
#endif
        }

        public static bool PathContainsInvalidChars(string path)
        {
            return !string.IsNullOrEmpty(path) && path.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
        }

        public static string RemoveInvalidChars(string path)
        {
            return string.Concat(path.Split(Path.GetInvalidFileNameChars()));
        }

        public static string MakeProjectRelative(string path)
        {
            if (path.Replace("\\", "/").StartsWith(Application.dataPath.Replace("\\", "/")))
            {
                return "Assets" + path.Substring(Application.dataPath.Length);
            }
            return path;
        }

        public static string CreateTempFolder()
        {
            string tempDirectoryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectoryPath);

            return tempDirectoryPath;
        }

        public static string CreateTempFolder(string name, bool deleteIfExists = false)
        {
            string tempDirectoryPath = Path.Combine(Path.GetTempPath(), name);
            if (deleteIfExists && Directory.Exists(tempDirectoryPath)) Directory.Delete(tempDirectoryPath, true);
            if (!Directory.Exists(tempDirectoryPath)) Directory.CreateDirectory(tempDirectoryPath);

            return tempDirectoryPath;
        }

        public static async Task<List<string>> FindMatchesInBinaryFile(string filePath, IList<string> searchStrings, int bufferSize = 1 << 20)
        {
            int count = searchStrings.Count;
            byte[][] patterns = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                patterns[i] = Encoding.UTF8.GetBytes(searchStrings[i]);
            }

            AhoCorasick automaton = new AhoCorasick(patterns);
            HashSet<int> foundIds = new HashSet<int>();
            AhoCorasick.Node state = automaton.Root;

            byte[] buffer = new byte[bufferSize];

            using (FileStream fs = new FileStream(
                       filePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize,
                       useAsync: true))
            {
                int bytesRead;
                while (foundIds.Count < count && (bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    automaton.Scan(buffer, bytesRead, foundIds, ref state);
                }

                List<string> result = new List<string>(foundIds.Count);
                foreach (int id in foundIds)
                {
                    result.Add(searchStrings[id]);
                }
                return result;
            }
        }

        private static bool ContainsPattern(byte[] data, int dataLen, byte[] pattern)
        {
            int patLen = pattern.Length;
            int end = dataLen - patLen;

            for (int i = 0; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < patLen; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }

            return false;
        }

        public static string GetExtensionWithoutDot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string ext = Path.GetExtension(path);
            return string.IsNullOrEmpty(ext) ? string.Empty : ext.TrimStart('.');
        }

        public static string GetFileName(string path, bool returnOriginalOnError = true, bool quiet = true)
        {
            try
            {
                return Path.GetFileName(path);
            }
            catch (Exception e)
            {
                if (!quiet) Debug.LogError($"Illegal characters in path '{path}': {e}");
                return returnOriginalOnError ? path : null;
            }
        }

        public static string ReadFirstLine(string path)
        {
            string result = null;
            try
            {
                using (StreamReader reader = new StreamReader(ToLongPath(path)))
                {
                    result = reader.ReadLine();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error reading file '{path}': {e.Message}");
            }

            return result;
        }

        public static async Task<bool> TryCopyFile(string sourceFileName, string destFileName, bool overwrite, int retries = 5)
        {
            while (retries >= 0)
            {
                try
                {
                    File.Copy(sourceFileName, destFileName, overwrite);
                    return true;
                }
                catch (Exception e)
                {
                    string directoryName = Path.GetDirectoryName(destFileName);
                    if (!Directory.Exists(directoryName)) Directory.CreateDirectory(directoryName);

                    retries--;
                    if (retries >= 0)
                    {
                        await Task.Delay(500);
                    }
                    else
                    {
                        Debug.LogError($"Could not copy file '{sourceFileName}' to '{destFileName}': {e.Message}");
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Reads all text from a file using FileShare.Read to avoid locking the file.
        /// This is critical for Unity cache files that may be accessed by multiple editors.
        /// </summary>
        public static string ReadAllTextWithShare(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Reads all bytes from a file using FileShare.Read to avoid locking the file.
        /// This is critical for Unity cache files that may be accessed by multiple editors.
        /// </summary>
        public static byte[] ReadAllBytesWithShare(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                return buffer;
            }
        }

        /// <summary>
        /// Copies a file using FileShare.Read to avoid locking the source file.
        /// This is critical for Unity cache files that may be accessed by multiple editors.
        /// </summary>
        public static async Task<bool> CopyFileWithShare(string sourceFileName, string destFileName, bool overwrite, int retries = 5)
        {
            while (retries >= 0)
            {
                try
                {
                    // Ensure destination directory exists
                    string directoryName = Path.GetDirectoryName(destFileName);
                    if (!Directory.Exists(directoryName)) Directory.CreateDirectory(directoryName);

                    // Check if destination exists and we're not overwriting
                    if (File.Exists(destFileName) && !overwrite)
                    {
                        throw new IOException($"File already exists: {destFileName}");
                    }

                    // Copy file using FileStreams with FileShare.Read to avoid locking
                    using (FileStream sourceStream = new FileStream(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                    using (FileStream destStream = new FileStream(destFileName, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        await sourceStream.CopyToAsync(destStream);
                    }

                    // Copy file attributes
                    File.SetAttributes(destFileName, File.GetAttributes(sourceFileName));

                    return true;
                }
                catch (Exception e)
                {
                    retries--;
                    if (retries >= 0)
                    {
                        await Task.Delay(500);
                    }
                    else
                    {
                        Debug.LogError($"Could not copy file '{sourceFileName}' to '{destFileName}': {e.Message}");
                    }
                }
            }

            return false;
        }

        public static bool TryDeleteFile(string path)
        {
            // adjust attributes to ensure deletion
            try { File.SetAttributes(path, FileAttributes.Normal); }
            catch
            { /* ignore */
            }

            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<bool> DeleteFileOrDirectory(string path, int retries = 3)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;

            string targetPath = ToLongPath(path);

            while (retries >= 0)
            {
                try
                {
                    // Delete file
                    if (File.Exists(targetPath))
                    {
                        try { File.SetAttributes(targetPath, FileAttributes.Normal); }
                        catch
                        { /* ignore */
                        }
                        File.Delete(targetPath);
                        return true;
                    }

                    // Delete directory (recursive)
                    if (Directory.Exists(targetPath))
                    {
                        ClearReadOnlyAttributes(targetPath);
                        Directory.Delete(targetPath, true);
                        return true;
                    }

                    // Path already gone
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    // Clear attributes and retry
                    try
                    {
                        if (Directory.Exists(targetPath)) ClearReadOnlyAttributes(targetPath);
                        if (File.Exists(targetPath)) File.SetAttributes(targetPath, FileAttributes.Normal);
                    }
                    catch
                    { /* best effort */
                    }

                    retries--;
                    if (retries >= 0) await Task.Delay(500);
                }
                catch (IOException)
                {
                    // Often due to file locks; wait and retry
                    retries--;
                    if (retries >= 0) await Task.Delay(500);
                }
                catch
                {
                    // Do not swallow unexpected exceptions endlessly
                    break;
                }
            }

            return !File.Exists(targetPath) && !Directory.Exists(targetPath);
        }

        private static void ClearReadOnlyAttributes(string directoryPath)
        {
            // Clear file attributes first
            try
            {
                foreach (string file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); }
                    catch
                    { /* ignore */
                    }
                }
            }
            catch
            { /* ignore */
            }

            // Clear directory attributes (including the root)
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(dir, FileAttributes.Normal); }
                    catch
                    { /* ignore */
                    }
                }
            }
            catch
            { /* ignore */
            }

            try { File.SetAttributes(directoryPath, FileAttributes.Normal); }
            catch
            { /* ignore */
            }
        }

        // Regex version
        public static IEnumerable<string> GetFiles(string path, string searchPatternExpression = "", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            Regex reSearchPattern = new Regex(searchPatternExpression, RegexOptions.IgnoreCase);
            return Directory.EnumerateFiles(path, "*", searchOption)
                .Where(file => reSearchPattern.IsMatch(Path.GetExtension(file)));
        }

        // Takes multiple patterns and executes in parallel
        public static IEnumerable<string> GetFiles(string path, IEnumerable<string> searchPatterns, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            if (path == null) return Enumerable.Empty<string>();

            return searchPatterns.AsParallel()
                .SelectMany(searchPattern => Directory.EnumerateFiles(path, searchPattern, searchOption));
        }

        public static IEnumerable<string> GetFilesSafe(string rootPath, string searchPattern, SearchOption searchOption = SearchOption.AllDirectories)
        {
            Queue<string> dirs = new Queue<string>();
            dirs.Enqueue(rootPath);

            while (dirs.Count > 0)
            {
                string currentDir = dirs.Dequeue();
                string[] subDirs;
                string[] files;

                // Try to get files in the current directory
                try
                {
                    files = Directory.GetFiles(currentDir, searchPattern);
                }
                catch (Exception)
                {
                    // Skip this directory if access is denied
                    // Skip if the directory is not found
                    // Skip if timeout happens
                    continue;
                }

                foreach (string file in files)
                {
                    yield return file;
                }

                if (searchOption == SearchOption.TopDirectoryOnly) continue;

                // Try to get subdirectories
                try
                {
                    subDirs = Directory.GetDirectories(currentDir);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (string subDir in subDirs)
                {
                    dirs.Enqueue(subDir);
                }
            }
        }

        public static bool IsDirectoryEmpty(string path)
        {
            if (path == null) return true;
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }

        public static bool IsSameDirectory(string path1, string path2)
        {
            DirectoryInfo di1 = new DirectoryInfo(path1);
            DirectoryInfo di2 = new DirectoryInfo(path2);

            return string.Equals(di1.FullName, di2.FullName, StringComparison.OrdinalIgnoreCase);
        }

        public static void CopyDirectory(string sourceDir, string destDir, bool includeSubDirs = true)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDir);
            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destDir);

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDir, file.Name);
                file.CopyTo(tempPath, true);
            }

            if (includeSubDirs)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string tempPath = Path.Combine(destDir, subDir.Name);
                    CopyDirectory(subDir.FullName, tempPath, includeSubDirs);
                }
            }
        }

        public static async Task<long> GetFolderSize(string folder, bool async = true)
        {
            if (!Directory.Exists(folder)) return 0;
            DirectoryInfo dirInfo = new DirectoryInfo(folder);
            try
            {
                if (async)
                {
                    // FIXME: this can crash Unity
                    return await Task.Run(() => dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length));
                }
                return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Returns a combined path with unified slashes
        /// </summary>
        /// <returns></returns>
        public static string PathCombine(params string[] path)
        {
            return Path.GetFullPath(Path.Combine(path));
        }

        /// <summary>
        /// Determines whether the provided path is a filesystem root.
        /// Handles Windows drive roots (e.g., C:\, D:\), bare drive letters (e.g., E:),
        /// and Unix-style roots (/). UNC roots are treated as roots as well.
        /// </summary>
        public static bool IsRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                // Normalize to a full path when possible
                string fullPath = Path.GetFullPath(path);
                string rootPath = Path.GetPathRoot(fullPath);

                bool isAtRoot = !string.IsNullOrEmpty(rootPath)
                    && string.Equals(fullPath.TrimEnd('\\', '/'), rootPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

                // Also catch bare drive letters like "E:" (without trailing slash)
                string normalizedRaw = path.Replace('/', '\\').TrimEnd('\\', '/');
                bool isDriveLetterOnly = normalizedRaw.Length == 2 && normalizedRaw[1] == ':'
                    && char.IsLetter(normalizedRaw[0]);

                return isAtRoot || isDriveLetterOnly;
            }
            catch
            {
                // If Path.GetFullPath throws (illegal chars), treat as not root
                return false;
            }
        }

        /// <summary>
        /// Gets the relative path from one path to another.
        /// For Unity 2021.2+, uses the built-in Path.GetRelativePath.
        /// For older versions, provides a custom implementation.
        /// </summary>
        /// <param name="relativeTo">The source path the result should be relative to</param>
        /// <param name="path">The destination path</param>
        /// <returns>The relative path, or the original path if it can't be made relative</returns>
        public static string GetRelativePath(string relativeTo, string path)
        {
#if UNITY_2021_2_OR_NEWER
            return Path.GetRelativePath(relativeTo, path);
#else
            if (string.IsNullOrEmpty(relativeTo)) return path;
            if (string.IsNullOrEmpty(path)) return path;

            try
            {
                Uri fromUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(relativeTo)));
                Uri toUri = new Uri(Path.GetFullPath(path));

                if (fromUri.Scheme != toUri.Scheme)
                {
                    return path; // Cannot make relative path between different schemes
                }

                Uri relativeUri = fromUri.MakeRelativeUri(toUri);
                string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

                if (string.Equals(toUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }

                return relativePath;
            }
            catch
            {
                return path;
            }
#endif
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (!Path.HasExtension(path) && !path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                return path + Path.DirectorySeparatorChar;
            }
            return path;
        }

        public static string ExecuteCommand(string command, string arguments, string workingDirectory = "", bool waitForExit = true, bool createWindow = false)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = !createWindow,
                UseShellExecute = createWindow,
                CreateNoWindow = !createWindow,
                WorkingDirectory = workingDirectory
            };

            try
            {
                using (Process process = new Process {StartInfo = processStartInfo})
                {
                    process.Start();
                    string result = null;
                    if (!createWindow) result = process.StandardOutput.ReadToEnd();
                    if (waitForExit) process.WaitForExit();
                    return result;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error executing command '{command}': {e.Message}");
                return null;
            }
        }

#if UNITY_2021_2_OR_NEWER
        public static async Task<bool> DownloadFile(Uri uri, string targetFile)
        {
            UnityWebRequest request = UnityWebRequest.Get(uri);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError(request.error);
                return false;
            }

            byte[] data = request.downloadHandler.data;
            await File.WriteAllBytesAsync(targetFile, data);

            return true;
        }
#endif

        public static bool IsFirstArchiveVolume(string file)
        {
            string fileName = Path.GetFileName(file).ToLowerInvariant();
            if (fileName.EndsWith(".rar"))
            {
                Match match = Regex.Match(fileName, @"\.part(\d+)\.rar$");
                if (match.Success)
                {
                    int partNumber = int.Parse(match.Groups[1].Value);
                    return partNumber == 1;
                }
                return true;
            }
            return true;
        }

#if UNITY_2021_2_OR_NEWER
        public static void CompressFolder(string source, string target)
        {
            using FileStream zipStream = File.Create(target);
            WriterOptions options = new WriterOptions(CompressionType.Deflate);
            using IWriter writer = WriterFactory.Open(zipStream, ArchiveType.Zip, options);
            writer.WriteAll(source, "*", SearchOption.AllDirectories);
        }

        public static void CreateEmptyZip(string zipPath)
        {
            using FileStream zipStream = File.Create(zipPath);
            using IWriter writer = WriterFactory.Open(zipStream, ArchiveType.Zip, new WriterOptions(CompressionType.Deflate));
            // No entries added: creates an empty zip.
        }

        public static bool ExtractArchive(string archiveFile, string targetFolder, CancellationToken ct = default(CancellationToken))
        {
            Directory.CreateDirectory(targetFolder);

            try
            {
                // CRITICAL: Open archive file with FileShare.Read to allow other Unity editors to read it simultaneously
                // This prevents exclusive locking of Unity cache packages during extraction
                using (FileStream archiveStream = new FileStream(archiveFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (IArchive archive = ArchiveFactory.Open(archiveStream))
                {
                    foreach (IArchiveEntry entry in archive.Entries)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            _ = DeleteFileOrDirectory(targetFolder);
                            return false;
                        }
                        if (string.IsNullOrEmpty(entry.Key)) continue;

                        if (!entry.IsDirectory)
                        {
                            try
                            {
                                string fullOutputPath = Path.Combine(targetFolder, entry.Key);
                                string directoryName = Path.GetDirectoryName(fullOutputPath);
                                Directory.CreateDirectory(directoryName);

                                entry.WriteToDirectory(targetFolder, new ExtractionOptions
                                {
                                    Overwrite = true,
                                    ExtractFullPath = true
                                });
                            }
                            catch (Exception e)
                            {
                                if (e is ArgumentException || e is IOException)
                                {
                                    // can happen for paths containing : and other illegal characters
                                    Debug.LogWarning($"Could not extract file '{entry.Key}' from archive '{archiveFile}': {e.Message}");
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not extract archive '{archiveFile}'. The process was potentially interrupted, the file is corrupted or the path too long: {e.Message}");
                return false;
            }

            return true;
        }
#endif
    }
}