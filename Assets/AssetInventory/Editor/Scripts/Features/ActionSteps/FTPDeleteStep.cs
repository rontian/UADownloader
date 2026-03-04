using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
#if UNITY_2021_2_OR_NEWER
using Renci.SshNet.Sftp;
#endif
using UnityEngine;

namespace AssetInventory
{
    [Serializable]
    public sealed class FTPDeleteStep : FTPActionStep
    {
        public FTPDeleteStep()
        {
            Key = "FTPDelete";
            Name = "FTP Delete";
            Description = "Delete a folder from an FTP or SFTP server.";
            Category = ActionCategory.FilesAndFolders;

            // Add FTP/SFTP server connection parameter
            AddServerParameter();

            // Target directory parameter
            Parameters.Add(new StepParameter
            {
                Name = "Target",
                Description = "Remote directory path on the FTP/SFTP server to delete (e.g., /public_html/files or /uploads).",
                Type = StepParameter.ParamType.String,
                ValueList = StepParameter.ValueType.None,
                DefaultValue = new ParameterValue("/folder_name")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            // Get parameters
            string connectionId = parameters[0].stringValue;
            string targetDirectory = parameters[1].stringValue;

            // Get and validate connection
            if (!TryGetConnection(connectionId, out FTPConnection connection, out string password))
            {
                throw new Exception("Failed to get FTP connection. Check that the connection is properly configured.");
            }

            // Validate target directory
            if (string.IsNullOrEmpty(targetDirectory))
            {
                throw new ArgumentException("Target directory cannot be empty.");
            }

            // Prevent deleting root directory
            if (targetDirectory == "/" || targetDirectory == "\\")
            {
                throw new InvalidOperationException("Cannot delete root directory for safety reasons.");
            }

            string protocolName = GetProtocolName(connection);
            Debug.Log($"Starting deletion of '{protocolName}://{connection.host}:{connection.port}{targetDirectory}'");

            // Perform deletion based on protocol
            if (connection.protocol == FTPConnection.FTPProtocol.SFTP)
            {
                await DeleteViaSFTP(connection, targetDirectory, password);
            }
            else
            {
                await DeleteViaFTP(connection, targetDirectory, password);
            }

            Debug.Log($"{protocolName.ToUpper()} deletion completed successfully.");
        }

        private async Task DeleteViaFTP(FTPConnection connection, string targetDirectory, string password)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Recursively delete directory contents
                    DeleteFTPDirectoryRecursive(connection, targetDirectory, password);
                    Debug.Log($"FTP deletion completed for: {targetDirectory}");
                }
                finally
                {
                    ResetSslValidation();
                }
            });
        }

        private void DeleteFTPDirectoryRecursive(FTPConnection connection, string remotePath, string password)
        {
            try
            {
                if (AI.Actions.CancellationRequested) return;

                // List directory contents
                FtpWebRequest listRequest = CreateFtpRequest(connection, remotePath, password);
                listRequest.Method = WebRequestMethods.Ftp.ListDirectoryDetails;

                List<string> directories = new List<string>();
                List<string> files = new List<string>();

                using (FtpWebResponse listResponse = (FtpWebResponse)listRequest.GetResponse())
                using (StreamReader reader = new StreamReader(listResponse.GetResponseStream()))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        // Parse directory listing (Unix-style)
                        // Format: drwxr-xr-x 2 user group 4096 Jan 1 12:00 dirname
                        string[] parts = line.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 9) continue;

                        string name = string.Join(" ", parts.Skip(8));
                        if (name == "." || name == "..") continue;

                        string itemPath = remotePath.TrimEnd('/') + "/" + name;

                        // Check if it's a directory (starts with 'd')
                        if (line.StartsWith("d"))
                        {
                            directories.Add(itemPath);
                        }
                        else
                        {
                            files.Add(itemPath);
                        }
                    }
                }

                // Delete files first
                foreach (string filePath in files)
                {
                    if (AI.Actions.CancellationRequested) return;
                    DeleteFTPFile(connection, filePath, password);
                }

                // Recursively delete subdirectories
                foreach (string dirPath in directories)
                {
                    if (AI.Actions.CancellationRequested) return;
                    DeleteFTPDirectoryRecursive(connection, dirPath, password);
                }

                // Delete the directory itself
                DeleteFTPDirectory(connection, remotePath, password);
            }
            catch (WebException ex)
            {
                if (ex.Response is FtpWebResponse response)
                {
                    Debug.LogError($"Failed to delete directory '{remotePath}': {response.StatusDescription}");
                }
                else
                {
                    Debug.LogError($"Failed to delete directory '{remotePath}': {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to delete directory '{remotePath}': {ex.Message}");
            }
        }

        private void DeleteFTPFile(FTPConnection connection, string remotePath, string password)
        {
            try
            {
                FtpWebRequest request = CreateFtpRequest(connection, remotePath, password);
                request.Method = WebRequestMethods.Ftp.DeleteFile;

                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    Debug.Log($"Deleted file: {remotePath} [{response.StatusDescription.Trim()}]");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to delete file '{remotePath}': {ex.Message}");
            }
        }

        private void DeleteFTPDirectory(FTPConnection connection, string remotePath, string password)
        {
            try
            {
                FtpWebRequest request = CreateFtpRequest(connection, remotePath, password);
                request.Method = WebRequestMethods.Ftp.RemoveDirectory;

                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    Debug.Log($"Deleted directory: {remotePath} [{response.StatusDescription.Trim()}]");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to delete directory '{remotePath}': {ex.Message}");
            }
        }

        private async Task DeleteViaSFTP(FTPConnection connection, string targetDirectory, string password)
        {
#if UNITY_2021_2_OR_NEWER
            await Task.Run(() =>
            {
                Renci.SshNet.SftpClient client = null;

                try
                {
                    // Connect to SFTP server
                    client = SFTPUtil.ConnectSFTP(connection, password);

                    if (!client.IsConnected)
                    {
                        Debug.LogError("Failed to connect to SFTP server");
                        return;
                    }

                    Debug.Log($"Connected to SFTP server: {connection.host}:{connection.port}");

                    // Delete directory recursively
                    DeleteSFTPDirectoryRecursive(client, targetDirectory);

                    Debug.Log($"SFTP deletion completed for: {targetDirectory}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"SFTP deletion error: {e.Message}\n{e.StackTrace}");
                    throw;
                }
                finally
                {
                    // Disconnect and cleanup
                    if (client != null)
                    {
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                        }
                        client.Dispose();
                    }
                }
            });
#else
            Debug.LogError("SFTP is only supported in Unity 2021.2 and higher.");
            await Task.Yield();
#endif
        }

#if UNITY_2021_2_OR_NEWER
        private void DeleteSFTPDirectoryRecursive(Renci.SshNet.SftpClient client, string remotePath)
        {
            try
            {
                if (AI.Actions.CancellationRequested) return;

                // Check if path exists
                if (!client.Exists(remotePath))
                {
                    Debug.LogWarning($"Path does not exist: {remotePath}");
                    return;
                }

                // List directory contents
                IEnumerable<ISftpFile> items = client.ListDirectory(remotePath);

                foreach (ISftpFile item in items)
                {
                    if (AI.Actions.CancellationRequested) return;

                    if (item.Name == "." || item.Name == "..") continue;

                    if (item.IsDirectory)
                    {
                        // Recursively delete subdirectory
                        DeleteSFTPDirectoryRecursive(client, item.FullName);
                    }
                    else
                    {
                        // Delete file
                        client.DeleteFile(item.FullName);
                        Debug.Log($"Deleted file: {item.FullName}");
                    }
                }

                // Delete the directory itself
                client.DeleteDirectory(remotePath);
                Debug.Log($"Deleted directory: {remotePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to delete '{remotePath}': {e.Message}");
                throw;
            }
        }
#endif
    }
}