using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>
    /// Base class for FTP/SFTP action steps with common functionality.
    /// </summary>
    [Serializable]
    public abstract class FTPActionStep : ActionStep
    {
        /// <summary>
        /// Adds the FTP/SFTP server connection parameter to the step.
        /// </summary>
        protected void AddServerParameter()
        {
            // Load available FTP connections
            List<Tuple<string, ParameterValue>> connectionOptions = new List<Tuple<string, ParameterValue>>();

            if (AI.Config.ftpConnections != null && AI.Config.ftpConnections.Count > 0)
            {
                foreach (FTPConnection conn in AI.Config.ftpConnections)
                {
                    string displayName = string.IsNullOrEmpty(conn.name) ? conn.host : conn.name;
                    connectionOptions.Add(new Tuple<string, ParameterValue>(displayName, new ParameterValue(conn.key)));
                }
            }

            if (connectionOptions.Count == 0)
            {
                connectionOptions.Add(new Tuple<string, ParameterValue>("No FTP connections configured", new ParameterValue("")));
            }

            // FTP/SFTP Connection parameter
            Parameters.Add(new StepParameter
            {
                Name = "Server",
                Description = "FTP/SFTP connection to use (configure in Settings > Maintenance > FTP/SFTP Administration).",
                Type = StepParameter.ParamType.String,
                ValueList = StepParameter.ValueType.Custom,
                Options = connectionOptions,
                DefaultValue = connectionOptions[0].Item2
            });
        }

        /// <summary>
        /// Gets and validates an FTP connection from parameters.
        /// </summary>
        /// <param name="connectionId">The connection ID from parameters.</param>
        /// <param name="connection">The validated connection, or null if validation fails.</param>
        /// <param name="password">The decrypted password, or empty string if validation fails.</param>
        /// <returns>True if connection is valid, false otherwise.</returns>
        protected bool TryGetConnection(string connectionId, out FTPConnection connection, out string password)
        {
            connection = null;
            password = "";

            // Validate that connections exist
            if (AI.Config.ftpConnections == null || string.IsNullOrEmpty(connectionId))
            {
                Debug.LogError("No valid FTP/SFTP connection selected. Please configure connections in Settings > Locations.");
                return false;
            }

            // Find connection by ID
            connection = AI.Config.ftpConnections.FirstOrDefault(c => c.key == connectionId);
            if (connection == null)
            {
                Debug.LogError($"FTP/SFTP connection with ID '{connectionId}' not found. Please reconfigure this action step.");
                return false;
            }

            // Validate host
            if (string.IsNullOrEmpty(connection.host))
            {
                Debug.LogError($"Connection '{connection.name}' has no host configured.");
                return false;
            }

            // Decrypt password
            if (!string.IsNullOrEmpty(connection.encryptedPassword))
            {
                password = EncryptionUtil.Decrypt(connection.encryptedPassword);
                if (password == null)
                {
                    Debug.LogError($"Failed to decrypt password for connection '{connection.name}'.");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets the protocol name for logging purposes.
        /// </summary>
        protected string GetProtocolName(FTPConnection connection)
        {
            return connection.protocol == FTPConnection.FTPProtocol.SFTP ? "sftp" : "ftp";
        }

        /// <summary>
        /// Builds the FTP URI scheme (ftp, ftps, or sftp).
        /// </summary>
        protected string GetFtpScheme(FTPConnection connection)
        {
            if (connection.protocol == FTPConnection.FTPProtocol.SFTP)
            {
                return "sftp";
            }
            return connection.useSsl ? "ftps" : "ftp";
        }

        /// <summary>
        /// Configures SSL settings for an FTP request.
        /// </summary>
        protected void ConfigureFtpSsl(FtpWebRequest request, FTPConnection connection)
        {
            if (connection.useSsl)
            {
                request.EnableSsl = true;
                if (!connection.validateCertificate)
                {
                    ServicePointManager.ServerCertificateValidationCallback = (s, cert, chain, sslPolicyErrors) => true;
                }
            }
        }

        /// <summary>
        /// Creates a basic FTP request with common settings.
        /// </summary>
        protected FtpWebRequest CreateFtpRequest(FTPConnection connection, string remotePath, string password)
        {
            string ftpScheme = GetFtpScheme(connection);
            string uri = $"{ftpScheme}://{connection.host}:{connection.port}{remotePath}";

            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uri);
            request.Credentials = new NetworkCredential(connection.username, password);
            request.UsePassive = true;
            request.KeepAlive = false;

            ConfigureFtpSsl(request, connection);

            return request;
        }

        /// <summary>
        /// Resets the SSL certificate validation callback to default.
        /// Should be called in finally blocks after FTP operations.
        /// </summary>
        protected void ResetSslValidation()
        {
            ServicePointManager.ServerCertificateValidationCallback = null;
        }
    }
}

