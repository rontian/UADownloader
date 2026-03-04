using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace AssetInventory
{
    public static class EncryptionUtil
    {
        // Using a key derived from hardware and application identifiers
        // This is not the most secure solution, but provides basic obfuscation
        // For production use, consider using a more secure key management system
        private static readonly byte[] Salt =
        {
            0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64,
            0x76, 0x65, 0x64, 0x65, 0x76, 0x41, 0x73, 0x73,
            0x65, 0x74, 0x49, 0x6e, 0x76, 0x65, 0x6e, 0x74,
            0x6f, 0x72, 0x79, 0x53, 0x61, 0x6c, 0x74, 0x31
        };

        /// <summary>
        /// Encrypts a string using AES encryption
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>Base64 encoded encrypted string</returns>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            try
            {
                byte[] key = GetEncryptionKey();
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.GenerateIV();

                    ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                    using (MemoryStream msEncrypt = new MemoryStream())
                    {
                        // Prepend IV to the encrypted data
                        msEncrypt.Write(aes.IV, 0, aes.IV.Length);

                        using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plainText);
                        }

                        return Convert.ToBase64String(msEncrypt.ToArray());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Encryption error: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Decrypts a string that was encrypted using the Encrypt method
        /// </summary>
        /// <param name="cipherText">Base64 encoded encrypted string</param>
        /// <returns>Decrypted plaintext string</returns>
        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            try
            {
                byte[] key = GetEncryptionKey();
                byte[] buffer = Convert.FromBase64String(cipherText);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;

                    // Extract IV from the beginning of the buffer
                    byte[] iv = new byte[aes.IV.Length];
                    Array.Copy(buffer, 0, iv, 0, iv.Length);
                    aes.IV = iv;

                    ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                    using (MemoryStream msDecrypt = new MemoryStream(buffer, iv.Length, buffer.Length - iv.Length))
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Decryption error: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Generates a key based on system info and salt
        /// </summary>
        private static byte[] GetEncryptionKey()
        {
            // Create a unique key based on system identifiers
            string keySource = SystemInfo.deviceUniqueIdentifier + "AssetInventory";

            using (Rfc2898DeriveBytes derive = new Rfc2898DeriveBytes(keySource, Salt, 10000))
            {
                return derive.GetBytes(32); // 256-bit key for AES
            }
        }
    }
}