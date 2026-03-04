using System;

namespace AssetInventory
{
    [Serializable]
    public sealed class FTPConnection
    {
        public enum FTPProtocol
        {
            FTP,
            SFTP
        }

        public string key;
        public string name;
        public string host;
        public int port = 21;
        public string username;
        public string encryptedPassword; // Encrypted password
        public FTPProtocol protocol = FTPProtocol.FTP;
        public bool useSsl;
        public bool validateCertificate = true;

        public FTPConnection()
        {
            key = Guid.NewGuid().ToString();
        }

        public FTPConnection Clone()
        {
            return new FTPConnection
            {
                key = key,
                name = name,
                host = host,
                port = port,
                username = username,
                encryptedPassword = encryptedPassword,
                protocol = protocol,
                useSsl = useSsl,
                validateCertificate = validateCertificate
            };
        }
        
        public int GetDefaultPort()
        {
            return protocol == FTPProtocol.SFTP ? 22 : 21;
        }
    }
}