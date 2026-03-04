#if UNITY_2021_2_OR_NEWER
using System;
using System.IO;
using Renci.SshNet;
using Renci.SshNet.Common;
using UnityEngine;

namespace AssetInventory
{
    public static class SFTPUtil
    {
        /// <summary>
        /// Creates and connects an SFTP client based on the connection configuration
        /// </summary>
        public static SftpClient ConnectSFTP(FTPConnection connection, string password)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof (connection));
            }

            if (string.IsNullOrEmpty(connection.host))
            {
                throw new ArgumentException("Host cannot be empty", nameof (connection));
            }

            if (string.IsNullOrEmpty(connection.username))
            {
                throw new ArgumentException("Username cannot be empty", nameof (connection));
            }

            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password is required for SFTP authentication");
            }

            // Create connection info with password authentication
            ConnectionInfo connectionInfo = new ConnectionInfo(
                connection.host,
                connection.port,
                connection.username,
                new PasswordAuthenticationMethod(connection.username, password)
            );

            // Create and connect the SFTP client
            SftpClient client = new SftpClient(connectionInfo);

            try
            {
                client.Connect();
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Uploads a directory recursively to the SFTP server
        /// </summary>
        public static void UploadDirectory(SftpClient client, string localPath, string remotePath, bool recursive, Action<string, string> onFileUploaded = null, Func<bool> cancellationRequested = null)
        {
            if (client == null || !client.IsConnected)
            {
                throw new InvalidOperationException("SFTP client is not connected");
            }

            if (!Directory.Exists(localPath))
            {
                throw new DirectoryNotFoundException($"Local directory not found: {localPath}");
            }

            // Normalize remote path
            remotePath = NormalizePath(remotePath);

            // Create remote directory if it doesn't exist
            CreateRemoteDirectory(client, remotePath);

            // Upload files in current directory
            string[] files = Directory.GetFiles(localPath);
            foreach (string filePath in files)
            {
                // Check for cancellation
                if (cancellationRequested != null && cancellationRequested()) return;

                string fileName = Path.GetFileName(filePath);
                string remoteFilePath = remotePath.TrimEnd('/') + "/" + fileName;

                try
                {
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        client.UploadFile(fileStream, remoteFilePath, true);
                    }

                    onFileUploaded?.Invoke(filePath, remoteFilePath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to upload file '{filePath}': {ex.Message}");
                    throw;
                }
            }

            // Upload subdirectories if recursive
            if (recursive)
            {
                string[] directories = Directory.GetDirectories(localPath);
                foreach (string dirPath in directories)
                {
                    // Check for cancellation
                    if (cancellationRequested != null && cancellationRequested())
                    {
                        Debug.Log("SFTP upload cancelled by user");
                        return;
                    }

                    string dirName = Path.GetFileName(dirPath);
                    string remoteSubDir = remotePath.TrimEnd('/') + "/" + dirName;

                    UploadDirectory(client, dirPath, remoteSubDir, true, onFileUploaded, cancellationRequested);
                }
            }
        }

        /// <summary>
        /// Creates a directory on the remote server, including parent directories
        /// </summary>
        public static void CreateRemoteDirectory(SftpClient client, string path)
        {
            if (client == null || !client.IsConnected)
            {
                throw new InvalidOperationException("SFTP client is not connected");
            }

            path = NormalizePath(path);

            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return; // Root directory always exists
            }

            // Check if directory already exists
            try
            {
                if (client.Exists(path))
                {
                    return;
                }
            }
            catch (SftpPathNotFoundException)
            {
                // Directory doesn't exist, we'll create it
            }

            // Create parent directories first
            string parentPath = GetParentPath(path);
            if (!string.IsNullOrEmpty(parentPath) && parentPath != "/")
            {
                CreateRemoteDirectory(client, parentPath);
            }

            // Create this directory
            try
            {
                client.CreateDirectory(path);
            }
            catch (SftpPermissionDeniedException ex)
            {
                Debug.LogError($"Permission denied creating directory '{path}': {ex.Message}");
                throw;
            }
            catch (SshException ex)
            {
                // Directory might already exist (race condition in some cases)
                if (!client.Exists(path))
                {
                    Debug.LogError($"Failed to create directory '{path}': {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Normalizes a path to use forward slashes
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "/";
            }

            return path.Replace("\\", "/");
        }

        /// <summary>
        /// Gets the parent path of a given path
        /// </summary>
        private static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return null;
            }

            path = path.TrimEnd('/');
            int lastSlash = path.LastIndexOf('/');

            if (lastSlash <= 0)
            {
                return "/";
            }

            return path.Substring(0, lastSlash);
        }

        /// <summary>
        /// Tests an SFTP connection
        /// </summary>
        public static bool TestConnection(FTPConnection connection, string password, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                using (SftpClient client = ConnectSFTP(connection, password))
                {
                    if (client.IsConnected)
                    {
                        // Try to list the home directory to verify connection works
                        client.ListDirectory(".");
                        client.Disconnect();
                        return true;
                    }

                    errorMessage = "Failed to connect";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
#endif