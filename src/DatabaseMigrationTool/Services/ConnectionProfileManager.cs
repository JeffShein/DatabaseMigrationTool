using DatabaseMigrationTool.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace DatabaseMigrationTool.Services
{
    public class ConnectionProfileManager
    {
        private readonly string _profilesPath;
        private readonly string _passwordKey;
        private List<ConnectionProfile> _profiles;
        
        public ConnectionProfileManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DatabaseMigrationTool"
            );
            Directory.CreateDirectory(appDataPath);
            
            _profilesPath = Path.Combine(appDataPath, "connection_profiles.json");
            _passwordKey = GetOrCreatePasswordKey(appDataPath);
            _profiles = new List<ConnectionProfile>();
            
            LoadProfiles();
        }
        
        public IReadOnlyList<ConnectionProfile> GetProfiles()
        {
            return _profiles.AsReadOnly();
        }
        
        public ConnectionProfile? GetProfile(string name)
        {
            return _profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        
        public void SaveProfile(ConnectionProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new ArgumentException("Profile name is required", nameof(profile));
                
            // Remove existing profile with same name
            _profiles.RemoveAll(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
            
            // Add the new/updated profile
            var profileToSave = profile.Clone();
            profileToSave.LastUsed = DateTime.Now;
            _profiles.Add(profileToSave);
            
            SaveProfiles();
        }
        
        public bool DeleteProfile(string name)
        {
            var removed = _profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SaveProfiles();
                return true;
            }
            return false;
        }
        
        public void UpdateLastUsed(string name)
        {
            var profile = GetProfile(name);
            if (profile != null)
            {
                profile.LastUsed = DateTime.Now;
                SaveProfiles();
            }
        }
        
        public IEnumerable<ConnectionProfile> GetRecentProfiles(int count = 5)
        {
            return _profiles
                .OrderByDescending(p => p.LastUsed)
                .Take(count);
        }
        
        public IEnumerable<ConnectionProfile> GetProfilesByProvider(string provider)
        {
            return _profiles
                .Where(p => p.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Name);
        }
        
        private void LoadProfiles()
        {
            try
            {
                if (!File.Exists(_profilesPath))
                    return;
                    
                var json = File.ReadAllText(_profilesPath);
                var savedProfiles = JsonSerializer.Deserialize<List<SavedConnectionProfile>>(json) ?? new List<SavedConnectionProfile>();
                
                _profiles = savedProfiles.Select(sp => new ConnectionProfile
                {
                    Name = sp.Name,
                    Provider = sp.Provider,
                    Server = sp.Server,
                    Database = sp.Database,
                    Username = sp.Username,
                    Password = DecryptPassword(sp.EncryptedPassword),
                    Port = sp.Port,
                    UseWindowsAuth = sp.UseWindowsAuth,
                    UseSsl = sp.UseSsl,
                    TrustServerCertificate = sp.TrustServerCertificate,
                    ReadOnlyConnection = sp.ReadOnlyConnection,
                    FirebirdVersion = sp.FirebirdVersion,
                    DatabasePath = sp.DatabasePath,
                    CreatedDate = sp.CreatedDate,
                    LastUsed = sp.LastUsed,
                    Description = sp.Description,
                    AdvancedProperties = sp.AdvancedProperties ?? new Dictionary<string, string>()
                }).ToList();
            }
            catch (Exception ex)
            {
                // Log error but continue with empty list
                System.Diagnostics.Debug.WriteLine($"Failed to load connection profiles: {ex.Message}");
                _profiles = new List<ConnectionProfile>();
            }
        }
        
        private void SaveProfiles()
        {
            try
            {
                var savedProfiles = _profiles.Select(p => new SavedConnectionProfile
                {
                    Name = p.Name,
                    Provider = p.Provider,
                    Server = p.Server,
                    Database = p.Database,
                    Username = p.Username,
                    EncryptedPassword = EncryptPassword(p.Password),
                    Port = p.Port,
                    UseWindowsAuth = p.UseWindowsAuth,
                    UseSsl = p.UseSsl,
                    TrustServerCertificate = p.TrustServerCertificate,
                    ReadOnlyConnection = p.ReadOnlyConnection,
                    FirebirdVersion = p.FirebirdVersion,
                    DatabasePath = p.DatabasePath,
                    CreatedDate = p.CreatedDate,
                    LastUsed = p.LastUsed,
                    Description = p.Description,
                    AdvancedProperties = p.AdvancedProperties
                }).ToList();
                
                var json = JsonSerializer.Serialize(savedProfiles, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(_profilesPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save connection profiles: {ex.Message}");
                throw;
            }
        }
        
        private string GetOrCreatePasswordKey(string appDataPath)
        {
            var keyPath = Path.Combine(appDataPath, "key.dat");
            
            if (File.Exists(keyPath))
            {
                try
                {
                    return File.ReadAllText(keyPath);
                }
                catch
                {
                    // If we can't read the key, create a new one
                }
            }
            
            // Create new key
            var key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            try
            {
                File.WriteAllText(keyPath, key);
            }
            catch
            {
                // If we can't save the key, use a session-only key
            }
            
            return key;
        }
        
        private string EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;
                
            try
            {
                var data = Encoding.UTF8.GetBytes(password);
                var key = Encoding.UTF8.GetBytes(_passwordKey.PadRight(32).Substring(0, 32));
                
                using var aes = Aes.Create();
                aes.Key = key;
                aes.GenerateIV();
                
                using var encryptor = aes.CreateEncryptor();
                var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
                
                var result = new byte[aes.IV.Length + encrypted.Length];
                Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
                Array.Copy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
                
                return Convert.ToBase64String(result);
            }
            catch
            {
                return string.Empty;
            }
        }
        
        private string DecryptPassword(string encryptedPassword)
        {
            if (string.IsNullOrEmpty(encryptedPassword))
                return string.Empty;
                
            try
            {
                var data = Convert.FromBase64String(encryptedPassword);
                var key = Encoding.UTF8.GetBytes(_passwordKey.PadRight(32).Substring(0, 32));
                
                using var aes = Aes.Create();
                aes.Key = key;
                
                var iv = new byte[aes.IV.Length];
                var encrypted = new byte[data.Length - iv.Length];
                
                Array.Copy(data, 0, iv, 0, iv.Length);
                Array.Copy(data, iv.Length, encrypted, 0, encrypted.Length);
                
                aes.IV = iv;
                
                using var decryptor = aes.CreateDecryptor();
                var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return string.Empty;
            }
        }
        
        // Internal class for serialization (excludes sensitive password field)
        private class SavedConnectionProfile
        {
            public string Name { get; set; } = string.Empty;
            public string Provider { get; set; } = string.Empty;
            public string Server { get; set; } = string.Empty;
            public string Database { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string EncryptedPassword { get; set; } = string.Empty;
            public int Port { get; set; }
            public bool UseWindowsAuth { get; set; }
            public bool UseSsl { get; set; }
            public bool TrustServerCertificate { get; set; }
            public bool ReadOnlyConnection { get; set; }
            public string FirebirdVersion { get; set; } = string.Empty;
            public string DatabasePath { get; set; } = string.Empty;
            public DateTime CreatedDate { get; set; }
            public DateTime LastUsed { get; set; }
            public string Description { get; set; } = string.Empty;
            public Dictionary<string, string> AdvancedProperties { get; set; } = new();
        }
    }
}