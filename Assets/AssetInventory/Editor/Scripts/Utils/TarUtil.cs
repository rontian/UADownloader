using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
#if UNITY_2021_2_OR_NEWER
using SharpCompress.Common;
using SharpCompress.Readers;
#endif
using Unity.SharpZipLib.GZip;
using Unity.SharpZipLib.Tar;
using UnityEngine;

namespace AssetInventory
{
    public static class TarUtil
    {
#if UNITY_2021_2_OR_NEWER
        // more performant implementation using SharpCompress, especially on Linux
        public static void ExtractGz(string archive, string targetFolder, CancellationToken ct)
        {
            Directory.CreateDirectory(targetFolder);

            try
            {
                using FileStream stream = File.OpenRead(archive);
                ReaderOptions readerOptions = new ReaderOptions {LeaveStreamOpen = false};
                using IReader reader = ReaderFactory.Open(stream, readerOptions);
                while (reader.MoveToNextEntry())
                {
                    if (ct.IsCancellationRequested)
                    {
                        _ = IOUtils.DeleteFileOrDirectory(targetFolder);
                        break;
                    }
                    if (!reader.Entry.IsDirectory)
                    {
                        reader.WriteEntryToDirectory(targetFolder, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                Debug.LogError($"Permission denied extracting ‘{archive}’: {uaEx.Message}");
            }
            catch (ArchiveException archEx)
            {
                Debug.LogError($"Archive format error for ‘{archive}’: {archEx.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not extract archive ‘{archive}’. It may be corrupted or the process was interrupted: {e.Message}");
            }
        }
#else
        public static void ExtractGz(string archive, string targetFolder, CancellationToken ct)
        {
            Stream rawStream = File.OpenRead(archive);
            GZipInputStream gzipStream = new GZipInputStream(rawStream);

            try
            {
                TarArchive tarArchive = TarArchive.CreateInputTarArchive(IsZipped(archive) ? gzipStream : rawStream, Encoding.Default);
                tarArchive.ExtractContents(targetFolder, true);
                tarArchive.Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not extract archive '{archive}'. The process was either interrupted or the file is corrupted: {e.Message}");
            }

            gzipStream.Close();
            rawStream.Close();
        }
#endif

        public static string ExtractGzFile(string archive, string fileName, string targetFolder, CancellationToken ct)
        {
            Stream rawStream = File.OpenRead(archive);
            GZipInputStream gzipStream = new GZipInputStream(rawStream);

            string destFile = null;

            // fileName will be ID/asset, whole folder is needed though
            string folderName = fileName.Split(new[] {'/', '\\'}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            try
            {
                Stream inputStream = IsZipped(archive) ? gzipStream : rawStream;

                using (TarInputStream tarStream = new TarInputStream(inputStream, Encoding.Default))
                {
                    TarEntry entry;
                    bool found = false;
                    while ((entry = tarStream.GetNextEntry()) != null)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (entry.IsDirectory) continue;
                        if (entry.Name.Contains(folderName))
                        {
                            destFile = Path.Combine(targetFolder, entry.Name);
                            string directoryName = Path.GetDirectoryName(destFile);
                            Directory.CreateDirectory(directoryName);

                            using (FileStream fileStream = File.Create(destFile))
                            {
                                tarStream.CopyEntryContents(fileStream);
                            }
                            found = true;
                        }
                        else if (found)
                        {
                            // leave the loop if the files were found and the next entry is not in the same folder
                            // assumption is the files appear consecutively
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not extract file from archive '{archive}'. The process was either interrupted or the file is corrupted: {e.Message}");
            }

            gzipStream.Close();
            rawStream.Close();

            return destFile;
        }

        private static bool IsZipped(string fileName)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buffer = new byte[2];
                fs.Read(buffer, 0, buffer.Length);
                return buffer[0] == 0x1F && buffer[1] == 0x8B;
            }
        }
    }
}