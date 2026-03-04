using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class FTPAdminUI : BasicEditorUI
    {
        private Vector2 _scrollPos;
        private List<FTPConnection> _connections;
        private FTPConnection _selectedConnection;
        private int _selectedIndex = -1;
        private string _tempPassword = ""; // Temporary storage for password editing
        private bool _showPassword;
        private bool _isEditing;

        public static FTPAdminUI ShowWindow()
        {
            FTPAdminUI window = GetWindow<FTPAdminUI>("FTP/SFTP Administration");
            window.minSize = new Vector2(600, 500);
            return window;
        }

        private void OnEnable()
        {
            LoadConnections();
        }

        private void LoadConnections()
        {
            if (AI.Config.ftpConnections == null)
            {
                AI.Config.ftpConnections = new List<FTPConnection>();
            }
            _connections = AI.Config.ftpConnections;
            SortConnections();
        }

        private void SortConnections()
        {
            _connections.Sort((a, b) =>
            {
                string nameA = string.IsNullOrEmpty(a.name) ? "" : a.name;
                string nameB = string.IsNullOrEmpty(b.name) ? "" : b.name;
                return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void SaveConnections()
        {
            AI.SaveConfig();
            AI.Actions.Init(true); // FIXME: remove when actions support data init callback
        }

        public override void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Manage your FTP and SFTP connections for file upload actions.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            // Toolbar
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Add New Connection", EditorStyles.toolbarButton, GUILayout.Width(150)))
            {
                AddNewConnection();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Split view: List on the left, details on the right
            GUILayout.BeginHorizontal();

            // Left panel - Connection list
            GUILayout.BeginVertical(GUILayout.Width(250));
            DrawConnectionList();
            GUILayout.EndVertical();

            // Right panel - Connection details
            GUILayout.BeginVertical();
            DrawConnectionDetails();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawConnectionList()
        {
            EditorGUILayout.LabelField("Connections", EditorStyles.boldLabel);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, "box", GUILayout.ExpandHeight(true));

            for (int i = 0; i < _connections.Count; i++)
            {
                FTPConnection conn = _connections[i];

                string protocolBadge = conn.protocol == FTPConnection.FTPProtocol.SFTP ? "(SFTP)" : "(FTP)";
                string label = string.IsNullOrEmpty(conn.name) ? $"<Unnamed> {protocolBadge}" : $"{conn.name} {protocolBadge}";

                bool isSelected = i == _selectedIndex;
                if (GUILayout.Toggle(isSelected, label, GUI.skin.button))
                {
                    if (_selectedIndex != i)
                    {
                        SelectConnection(i);
                    }
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawConnectionDetails()
        {
            EditorGUILayout.LabelField("Connection Details", EditorStyles.boldLabel);

            if (_selectedConnection == null || _selectedIndex < 0)
            {
                EditorGUILayout.HelpBox("Select a connection from the list or add a new one.", MessageType.Info);
                return;
            }

            GUILayout.BeginVertical("box");

            EditorGUI.BeginChangeCheck();

            // Connection Name
            _selectedConnection.name = EditorGUILayout.TextField("Connection Name", _selectedConnection.name);

            EditorGUILayout.Space();

            // Protocol Selection
            FTPConnection.FTPProtocol oldProtocol = _selectedConnection.protocol;
            _selectedConnection.protocol = (FTPConnection.FTPProtocol)EditorGUILayout.EnumPopup("Protocol", _selectedConnection.protocol);

            // Update default port if protocol changed
            if (oldProtocol != _selectedConnection.protocol)
            {
                _selectedConnection.port = _selectedConnection.GetDefaultPort();
            }

            // Host
            _selectedConnection.host = EditorGUILayout.TextField("Host/Server", _selectedConnection.host);

            // Port
            _selectedConnection.port = EditorGUILayout.IntField("Port", _selectedConnection.port);
            if (_selectedConnection.port <= 0) _selectedConnection.port = _selectedConnection.GetDefaultPort();

            EditorGUILayout.Space();

            // Username
            _selectedConnection.username = EditorGUILayout.TextField("Username", _selectedConnection.username);

            // Password
            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Password");
            if (_showPassword)
            {
                _tempPassword = EditorGUILayout.TextField(_tempPassword);
            }
            else
            {
                _tempPassword = EditorGUILayout.PasswordField(_tempPassword);
            }
            _showPassword = GUILayout.Toggle(_showPassword, _showPassword ? "Hide" : "Show", GUI.skin.button, GUILayout.Width(50));
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_tempPassword))
            {
                EditorGUILayout.HelpBox("Password will be encrypted when saved.", MessageType.Info);
            }

            EditorGUILayout.Space();

            if (ShowAdvanced())
            {
                // SSL/TLS options (only for FTP, not SFTP)
                if (_selectedConnection.protocol == FTPConnection.FTPProtocol.FTP)
                {
                    _selectedConnection.useSsl = EditorGUILayout.Toggle("Use SSL/TLS (FTPS)", _selectedConnection.useSsl);
                    _selectedConnection.validateCertificate = EditorGUILayout.Toggle("Validate Certificate", _selectedConnection.validateCertificate);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                _isEditing = true;
            }

            EditorGUILayout.Space();

            // Action buttons
            GUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(!_isEditing && string.IsNullOrEmpty(_tempPassword));
            if (GUILayout.Button("Save Changes", GUILayout.Height(30)))
            {
                SaveCurrentConnection();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Test Connection", GUILayout.Height(30)))
            {
                TestConnection();
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Delete Connection", GUILayout.Height(30)))
            {
                DeleteConnection();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void SelectConnection(int index)
        {
            if (index < 0 || index >= _connections.Count) return;

            _selectedIndex = index;
            _selectedConnection = _connections[index].Clone();
            _tempPassword = "";
            _showPassword = false;
            _isEditing = false;

            // Try to decrypt password if it exists
            if (!string.IsNullOrEmpty(_selectedConnection.encryptedPassword))
            {
                _tempPassword = EncryptionUtil.Decrypt(_selectedConnection.encryptedPassword);
                if (string.IsNullOrEmpty(_tempPassword))
                {
                    _tempPassword = "";
                    Debug.LogWarning("Could not decrypt password for connection: " + _selectedConnection.name);
                }
            }
        }

        private void AddNewConnection()
        {
            FTPConnection newConnection = new FTPConnection
            {
                name = "New Connection",
                port = 21
            };

            _connections.Add(newConnection);
            _selectedIndex = _connections.Count - 1;
            SelectConnection(_selectedIndex);
            _isEditing = true;
        }

        private void SaveCurrentConnection()
        {
            if (_selectedConnection == null || _selectedIndex < 0) return;

            // Validate
            if (string.IsNullOrEmpty(_selectedConnection.name))
            {
                EditorUtility.DisplayDialog("Validation Error", "Connection name cannot be empty.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_selectedConnection.host))
            {
                EditorUtility.DisplayDialog("Validation Error", "Host cannot be empty.", "OK");
                return;
            }

            // Encrypt password if changed
            if (!string.IsNullOrEmpty(_tempPassword))
            {
                _selectedConnection.encryptedPassword = EncryptionUtil.Encrypt(_tempPassword);
                if (string.IsNullOrEmpty(_selectedConnection.encryptedPassword))
                {
                    EditorUtility.DisplayDialog("Error", "Failed to encrypt password.", "OK");
                    return;
                }
            }

            // Save the edited clone back to the original list
            _connections[_selectedIndex] = _selectedConnection;

            // Re-sort connections alphabetically
            string savedConnectionId = _selectedConnection.key;
            SortConnections();

            // Update selection index to match new position after sorting
            _selectedIndex = _connections.FindIndex(c => c.key == savedConnectionId);

            SaveConnections();
            _isEditing = false;
        }

        private void DeleteConnection()
        {
            if (_selectedConnection == null || _selectedIndex < 0) return;

            if (EditorUtility.DisplayDialog("Delete Connection",
                    $"Are you sure you want to delete the connection '{_selectedConnection.name}'?",
                    "Delete", "Cancel"))
            {
                _connections.RemoveAt(_selectedIndex);

                // Clear selection first
                _selectedConnection = null;
                _selectedIndex = -1;
                _tempPassword = "";
                _isEditing = false;
                _showPassword = false;

                SaveConnections();
            }
        }

        private async void TestConnection()
        {
            if (_selectedConnection == null) return;

            if (string.IsNullOrEmpty(_selectedConnection.host))
            {
                EditorUtility.DisplayDialog("Error", "Host cannot be empty.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_selectedConnection.username))
            {
                EditorUtility.DisplayDialog("Error", "Username cannot be empty.", "OK");
                return;
            }

            // Validate password
            if (string.IsNullOrEmpty(_tempPassword))
            {
                EditorUtility.DisplayDialog("Error", "Password cannot be empty.", "OK");
                return;
            }

            // Show progress
            string protocolName = _selectedConnection.protocol == FTPConnection.FTPProtocol.SFTP ? "SFTP" : "FTP";
            EditorUtility.DisplayProgressBar("Testing Connection", $"Connecting to {protocolName} server at {_selectedConnection.host}...", 0.5f);

            try
            {
                bool success = false;
                string errorMessage = "";

                if (_selectedConnection.protocol == FTPConnection.FTPProtocol.SFTP)
                {
#if UNITY_2021_2_OR_NEWER
                    // Test SFTP connection
                    await Task.Run(() =>
                    {
                        success = SFTPUtil.TestConnection(_selectedConnection, _tempPassword, out errorMessage);
                    });
#endif
                }
                else
                {
                    // Test FTP connection
                    await Task.Run(() =>
                    {
                        try
                        {
                            // Always use ftp:// scheme; SSL is controlled by EnableSsl property
                            string uri = $"ftp://{_selectedConnection.host}:{_selectedConnection.port}/";

                            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uri);
                            request.Method = WebRequestMethods.Ftp.ListDirectory;
                            request.Credentials = new NetworkCredential(_selectedConnection.username, _tempPassword);
                            request.UsePassive = true;
                            request.KeepAlive = false;
                            request.Timeout = 10000;

                            if (_selectedConnection.useSsl)
                            {
                                request.EnableSsl = true;
                                if (!_selectedConnection.validateCertificate)
                                {
                                    ServicePointManager.ServerCertificateValidationCallback = (s, cert, chain, sslPolicyErrors) => true;
                                }
                            }

                            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                            {
                                if (response.StatusCode == FtpStatusCode.OpeningData ||
                                    response.StatusCode == FtpStatusCode.DataAlreadyOpen ||
                                    response.StatusCode == FtpStatusCode.PathnameCreated)
                                {
                                    success = true;
                                }
                                response.Close();
                            }

                            ServicePointManager.ServerCertificateValidationCallback = null;
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);

                            errorMessage = e.Message;
                            ServicePointManager.ServerCertificateValidationCallback = null;
                        }
                    });
                }

                EditorUtility.ClearProgressBar();

                if (success)
                {
                    EditorUtility.DisplayDialog("Connection Successful",
                        $"Successfully connected to {protocolName} server. The connection is ready to use.",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Connection Failed",
                        $"Could not connect to {protocolName} server.\n\n" +
                        $"Error: {errorMessage}\n\n" +
                        "Please check your connection and certificate validation settings and credentials.",
                        "OK");
                }
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Connection Test Error", $"An error occurred while testing the connection:\n\n{e.Message}", "OK");
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}