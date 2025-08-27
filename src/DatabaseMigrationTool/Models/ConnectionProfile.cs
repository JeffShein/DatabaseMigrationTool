using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DatabaseMigrationTool.Models
{
    public class ConnectionProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        
        [JsonIgnore]
        public string Password { get; set; } = string.Empty; // Don't serialize password
        
        public int Port { get; set; }
        public bool UseWindowsAuth { get; set; }
        public bool UseSsl { get; set; }
        public bool TrustServerCertificate { get; set; }
        public bool ReadOnlyConnection { get; set; }
        public string FirebirdVersion { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime LastUsed { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
        
        // Additional properties for advanced configurations
        public Dictionary<string, string> AdvancedProperties { get; set; } = new();
        
        public string GetConnectionString()
        {
            return Provider.ToLower() switch
            {
                "sqlserver" => BuildSqlServerConnectionString(),
                "mysql" => BuildMySqlConnectionString(),
                "postgresql" => BuildPostgreSqlConnectionString(),
                "firebird" => BuildFirebirdConnectionString(),
                _ => throw new NotSupportedException($"Provider {Provider} not supported")
            };
        }
        
        private string BuildSqlServerConnectionString()
        {
            var parts = new List<string>
            {
                $"Server={Server}"
            };
            
            if (!string.IsNullOrEmpty(Database))
                parts.Add($"Database={Database}");
                
            if (UseWindowsAuth)
            {
                parts.Add("Integrated Security=true");
            }
            else
            {
                if (!string.IsNullOrEmpty(Username))
                    parts.Add($"User Id={Username}");
                if (!string.IsNullOrEmpty(Password))
                    parts.Add($"Password={Password}");
            }
            
            if (TrustServerCertificate)
                parts.Add("TrustServerCertificate=True");
                
            return string.Join(";", parts) + ";";
        }
        
        private string BuildMySqlConnectionString()
        {
            var parts = new List<string>
            {
                $"Server={Server}",
                $"Port={Port}"
            };
            
            if (!string.IsNullOrEmpty(Database))
                parts.Add($"Database={Database}");
            if (!string.IsNullOrEmpty(Username))
                parts.Add($"User={Username}");
            if (!string.IsNullOrEmpty(Password))
                parts.Add($"Password={Password}");
            if (UseSsl)
                parts.Add("SslMode=Required");
                
            return string.Join(";", parts) + ";";
        }
        
        private string BuildPostgreSqlConnectionString()
        {
            var parts = new List<string>
            {
                $"Host={Server}",
                $"Port={Port}"
            };
            
            if (!string.IsNullOrEmpty(Database))
                parts.Add($"Database={Database}");
            if (!string.IsNullOrEmpty(Username))
                parts.Add($"Username={Username}");
            if (!string.IsNullOrEmpty(Password))
                parts.Add($"Password={Password}");
            if (UseSsl)
                parts.Add("SSL Mode=Require");
                
            return string.Join(";", parts) + ";";
        }
        
        private string BuildFirebirdConnectionString()
        {
            var parts = new List<string>
            {
                $"DataSource={Server}",
                $"Database={DatabasePath}"
            };
            
            if (!string.IsNullOrEmpty(Username))
                parts.Add($"User={Username}");
            if (!string.IsNullOrEmpty(Password))
                parts.Add($"Password={Password}");
            if (!string.IsNullOrEmpty(FirebirdVersion))
                parts.Add($"Version={FirebirdVersion}");
            if (ReadOnlyConnection)
                parts.Add("ReadOnly=true");
                
            return string.Join(";", parts) + ";";
        }
        
        public ConnectionProfile Clone()
        {
            return new ConnectionProfile
            {
                Name = Name,
                Provider = Provider,
                Server = Server,
                Database = Database,
                Username = Username,
                Password = Password,
                Port = Port,
                UseWindowsAuth = UseWindowsAuth,
                UseSsl = UseSsl,
                TrustServerCertificate = TrustServerCertificate,
                ReadOnlyConnection = ReadOnlyConnection,
                FirebirdVersion = FirebirdVersion,
                DatabasePath = DatabasePath,
                CreatedDate = CreatedDate,
                LastUsed = DateTime.Now,
                Description = Description,
                AdvancedProperties = new Dictionary<string, string>(AdvancedProperties)
            };
        }
        
        public override string ToString()
        {
            return $"{Name} ({Provider} - {Server})";
        }
    }
}