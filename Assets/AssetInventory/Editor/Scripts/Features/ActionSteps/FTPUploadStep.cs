using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    [Serializable]
    public sealed class FTPUploadStep : FTPActionStep
    {
        public FTPUploadStep()
        {
            Key = "FTPUpload";
            Name = "FTP Upload";
            Description = "Upload a folder to an FTP or SFTP server.";
            Category = ActionCategory.FilesAndFolders;

            // Add FTP/SFTP server connection parameter
            AddServerParameter();

            // Source folder parameter
            Parameters.Add(new StepParameter
            {
                Name = "Source",
                Description = "Local folder to upload.",
                Type = StepParameter.ParamType.String,
                ValueList = StepParameter.ValueType.Folder,
                DefaultValue = new ParameterValue(AI.GetStorageFolder())
            });

            // Target directory parameter
            Parameters.Add(new StepParameter
            {
                Name = "Target",
                Description = "Remote directory path on the FTP/SFTP server (e.g., /public_html/files or /uploads).",
                Type = StepParameter.ParamType.String,
                ValueList = StepParameter.ValueType.None,
                DefaultValue = new ParameterValue("/")
            });
        }

        public override async Task Run(List<ParameterValue> parameters)
        {
            // Get parameters
            string connectionId = parameters[0].stringValue;
            string sourceFolder = parameters[1].stringValue;
            string targetDirectory = parameters[2].stringValue;

            // Get and validate connection
            if (!TryGetConnection(connectionId, out FTPConnection connection, out string password))
            {
                throw new Exception("Failed to get FTP connection. Check that the connection is properly configured.");
            }

            // Validate source folder
            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException($"Source folder does not exist: {sourceFolder}");
            }

            string protocolName = GetProtocolName(connection);
            Debug.Log($"Starting upload from '{sourceFolder}' to '{protocolName}://{connection.host}:{connection.port}{targetDirectory}'");

            // Perform upload based on protocol
            if (connection.protocol == FTPConnection.FTPProtocol.SFTP)
            {
                await UploadViaSFTP(connection, sourceFolder, targetDirectory, true, password);
            }
            else
            {
                await UploadViaFTP(connection, sourceFolder, targetDirectory, true, password);
            }

            Debug.Log($"{protocolName.ToUpper()} upload completed successfully.");
        }

        private async Task UploadViaFTP(FTPConnection connection, string sourceFolder, string targetDirectory, bool includeSubdirectories, string password)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Get all files to upload
                    string[] files = Directory.GetFiles(sourceFolder, "*.*", includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                    int uploadedCount = 0;
                    int totalFiles = files.Length;

                    foreach (string filePath in files)
                    {
                        if (AI.Actions.CancellationRequested) break;
                        try
                        {
                            // Calculate relative path
                            string relativePath = IOUtils.GetRelativePath(sourceFolder, filePath);
                            string remoteDirectory = targetDirectory;
                            string remoteFileName = Path.GetFileName(filePath);

                            if (includeSubdirectories)
                            {
                                string subDir = Path.GetDirectoryName(relativePath);
                                if (!string.IsNullOrEmpty(subDir))
                                {
                                    remoteDirectory = targetDirectory.TrimEnd('/') + "/" + subDir.Replace("\\", "/");
                                }
                            }

                            string remotePath = remoteDirectory.TrimEnd('/') + "/" + remoteFileName;

                            // Create directories if needed
                            if (includeSubdirectories && remoteDirectory != targetDirectory)
                            {
                                CreateFTPDirectory(connection, remoteDirectory, password);
                            }

                            // Create FTP request
                            FtpWebRequest request = CreateFtpRequest(connection, remotePath, password);
                            request.Method = WebRequestMethods.Ftp.UploadFile;
                            request.UseBinary = true;

                            // Upload file
                            byte[] fileContents = File.ReadAllBytes(filePath);
                            request.ContentLength = fileContents.Length;

                            using (Stream requestStream = request.GetRequestStream())
                            {
                                requestStream.Write(fileContents, 0, fileContents.Length);
                            }

                            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                            {
                                uploadedCount++;
                                Debug.Log($"Uploaded ({uploadedCount}/{totalFiles}): {relativePath} -> {remotePath} [{response.StatusDescription.Trim()}]");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Failed to upload file '{filePath}': {ex.Message}");
                        }
                    }

                    Debug.Log($"FTP upload completed: {uploadedCount}/{totalFiles} files uploaded.");
                }
                finally
                {
                    ResetSslValidation();
                }
            });
        }

        private void CreateFTPDirectory(FTPConnection connection, string remotePath, string password)
        {
            try
            {
                FtpWebRequest request = CreateFtpRequest(connection, remotePath, password);
                request.Method = WebRequestMethods.Ftp.MakeDirectory;

                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    // Directory created
                }
            }
            catch (WebException ex)
            {
                // Directory might already exist, which is fine
                if (ex.Response is FtpWebResponse response)
                {
                    if (response.StatusCode != FtpStatusCode.ActionNotTakenFileUnavailable)
                    {
                        // Only log if it's not a "directory already exists" error
                        Debug.LogWarning($"Could not create directory '{remotePath}': {response.StatusDescription}");
                    }
                }
            }
        }

        private async Task UploadViaSFTP(FTPConnection connection, string sourceFolder, string targetDirectory, bool includeSubdirectories, string password)
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

                    // Get all files to upload
                    string[] files = Directory.GetFiles(sourceFolder, "*.*", includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                    int uploadedCount = 0;
                    int totalFiles = files.Length;

                    // Upload files
                    SFTPUtil.UploadDirectory(client, sourceFolder, targetDirectory, includeSubdirectories, (localFile, remoteFile) =>
                    {
                        uploadedCount++;
                        string relativePath = IOUtils.GetRelativePath(sourceFolder, localFile);
                        Debug.Log($"Uploaded ({uploadedCount}/{totalFiles}): {relativePath} -> {remoteFile}");
                    }, () => AI.Actions.CancellationRequested);

                    Debug.Log($"SFTP upload completed: {uploadedCount}/{totalFiles} files uploaded.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"SFTP upload error: {e.Message}\n{e.StackTrace}");
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
    }
}
