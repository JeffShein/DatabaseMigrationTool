using System;
using System.Collections.Generic;
using System.Windows;

namespace DatabaseMigrationTool.Helpers
{
    /// <summary>
    /// Helper class for building connection strings for different database providers
    /// </summary>
    public static class ConnectionStringBuilder
    {
        /// <summary>
        /// Builds a connection string for SQL Server
        /// </summary>
        public static string BuildSqlServerConnectionString(
            string server, 
            string database, 
            bool integratedSecurity, 
            string username = "", 
            string password = "", 
            bool trustServerCertificate = true,
            string additionalParameters = "")
        {
            var connectionStringParts = new List<string>();
            
            // Add server
            connectionStringParts.Add($"Server={server}");
            
            // Add database if specified
            if (!string.IsNullOrWhiteSpace(database))
            {
                connectionStringParts.Add($"Database={database}");
            }
            
            // Add authentication
            if (integratedSecurity)
            {
                connectionStringParts.Add("Integrated Security=True");
            }
            else if (!string.IsNullOrWhiteSpace(username))
            {
                connectionStringParts.Add($"User Id={username}");
                
                if (!string.IsNullOrWhiteSpace(password))
                {
                    connectionStringParts.Add($"Password={password}");
                }
            }
            
            // Add trust server certificate for SQL Server
            connectionStringParts.Add($"TrustServerCertificate={trustServerCertificate}");
            
            // Disable encryption by default to avoid SSL/TLS certificate issues in development
            connectionStringParts.Add("Encrypt=false");
            
            // Add additional parameters if provided
            if (!string.IsNullOrWhiteSpace(additionalParameters))
            {
                // Remove any leading semicolons
                additionalParameters = additionalParameters.TrimStart(';');
                
                if (!string.IsNullOrWhiteSpace(additionalParameters))
                {
                    connectionStringParts.Add(additionalParameters);
                }
            }
            
            return string.Join(";", connectionStringParts);
        }
        
        /// <summary>
        /// Builds a connection string for MySQL
        /// </summary>
        public static string BuildMySqlConnectionString(
            string server,
            string database,
            int port = 3306,
            string username = "",
            string password = "",
            bool useSsl = false,
            string additionalParameters = "")
        {
            var connectionStringParts = new List<string>();
            
            // Add server
            connectionStringParts.Add($"Server={server}");
            
            // Add port if not default
            if (port != 3306)
            {
                connectionStringParts.Add($"Port={port}");
            }
            
            // Add database if specified
            if (!string.IsNullOrWhiteSpace(database))
            {
                connectionStringParts.Add($"Database={database}");
            }
            
            // Add user credentials if provided
            if (!string.IsNullOrWhiteSpace(username))
            {
                connectionStringParts.Add($"User Id={username}");
                
                if (!string.IsNullOrWhiteSpace(password))
                {
                    connectionStringParts.Add($"Password={password}");
                }
            }
            
            // Add SSL if enabled
            if (useSsl)
            {
                connectionStringParts.Add("SslMode=Required");
            }
            else
            {
                connectionStringParts.Add("SslMode=None");
            }
            
            // Add additional parameters if provided
            if (!string.IsNullOrWhiteSpace(additionalParameters))
            {
                // Remove any leading semicolons
                additionalParameters = additionalParameters.TrimStart(';');
                
                if (!string.IsNullOrWhiteSpace(additionalParameters))
                {
                    connectionStringParts.Add(additionalParameters);
                }
            }
            
            return string.Join(";", connectionStringParts);
        }
        
        /// <summary>
        /// Builds a connection string for PostgreSQL
        /// </summary>
        public static string BuildPostgreSqlConnectionString(
            string host,
            string database,
            int port = 5432,
            string username = "",
            string password = "",
            bool useSsl = true,
            string additionalParameters = "")
        {
            var connectionStringParts = new List<string>();
            
            // Add host
            connectionStringParts.Add($"Host={host}");
            
            // Add port if not default
            if (port != 5432)
            {
                connectionStringParts.Add($"Port={port}");
            }
            
            // Add database if specified
            if (!string.IsNullOrWhiteSpace(database))
            {
                connectionStringParts.Add($"Database={database}");
            }
            
            // Add user credentials if provided
            if (!string.IsNullOrWhiteSpace(username))
            {
                connectionStringParts.Add($"Username={username}");
                
                if (!string.IsNullOrWhiteSpace(password))
                {
                    connectionStringParts.Add($"Password={password}");
                }
            }
            
            // Add SSL if enabled
            connectionStringParts.Add(useSsl ? "SSL Mode=Require" : "SSL Mode=Disable");
            
            // Add additional parameters if provided
            if (!string.IsNullOrWhiteSpace(additionalParameters))
            {
                // Remove any leading semicolons
                additionalParameters = additionalParameters.TrimStart(';');
                
                if (!string.IsNullOrWhiteSpace(additionalParameters))
                {
                    connectionStringParts.Add(additionalParameters);
                }
            }
            
            return string.Join(";", connectionStringParts);
        }
        
        /// <summary>
        /// Tries to parse a SQL Server connection string into its components
        /// </summary>
        public static bool TryParseSqlServerConnectionString(
            string connectionString,
            out string server,
            out string database,
            out bool integratedSecurity,
            out string username,
            out string password)
        {
            server = "";
            database = "";
            integratedSecurity = false;
            username = "";
            password = "";
            
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;
            
            try
            {
                var parts = SplitConnectionString(connectionString);
                
                foreach (var part in parts)
                {
                    var keyValue = part.Split('=');
                    if (keyValue.Length != 2)
                        continue;
                    
                    string key = keyValue[0].Trim();
                    string value = keyValue[1].Trim();
                    
                    switch (key.ToLowerInvariant())
                    {
                        case "server":
                        case "data source":
                            server = value;
                            break;
                        case "database":
                        case "initial catalog":
                            database = value;
                            break;
                        case "integrated security":
                        case "trusted_connection":
                            integratedSecurity = value.ToLowerInvariant() == "true" || 
                                               value.ToLowerInvariant() == "yes" || 
                                               value.ToLowerInvariant() == "sspi";
                            break;
                        case "user id":
                        case "uid":
                        case "user":
                            username = value;
                            break;
                        case "password":
                        case "pwd":
                            password = value;
                            break;
                    }
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Tries to parse a MySQL connection string into its components
        /// </summary>
        public static bool TryParseMySqlConnectionString(
            string connectionString,
            out string server,
            out string database,
            out int port,
            out string username,
            out string password,
            out bool useSsl)
        {
            server = "";
            database = "";
            port = 3306;
            username = "";
            password = "";
            useSsl = false;
            
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;
            
            try
            {
                var parts = SplitConnectionString(connectionString);
                
                foreach (var part in parts)
                {
                    var keyValue = part.Split('=');
                    if (keyValue.Length != 2)
                        continue;
                    
                    string key = keyValue[0].Trim();
                    string value = keyValue[1].Trim();
                    
                    switch (key.ToLowerInvariant())
                    {
                        case "server":
                        case "host":
                            server = value;
                            break;
                        case "database":
                            database = value;
                            break;
                        case "port":
                            if (int.TryParse(value, out int parsedPort))
                            {
                                port = parsedPort;
                            }
                            break;
                        case "user id":
                        case "uid":
                        case "user":
                        case "username":
                            username = value;
                            break;
                        case "password":
                        case "pwd":
                            password = value;
                            break;
                        case "sslmode":
                            useSsl = value.ToLowerInvariant() != "none" && 
                                   value.ToLowerInvariant() != "disabled";
                            break;
                    }
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Tries to parse a PostgreSQL connection string into its components
        /// </summary>
        public static bool TryParsePostgreSqlConnectionString(
            string connectionString,
            out string host,
            out string database,
            out int port,
            out string username,
            out string password,
            out bool useSsl)
        {
            host = "";
            database = "";
            port = 5432;
            username = "";
            password = "";
            useSsl = true;
            
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;
            
            try
            {
                var parts = SplitConnectionString(connectionString);
                
                foreach (var part in parts)
                {
                    var keyValue = part.Split('=');
                    if (keyValue.Length != 2)
                        continue;
                    
                    string key = keyValue[0].Trim();
                    string value = keyValue[1].Trim();
                    
                    switch (key.ToLowerInvariant())
                    {
                        case "server":
                        case "host":
                            host = value;
                            break;
                        case "database":
                            database = value;
                            break;
                        case "port":
                            if (int.TryParse(value, out int parsedPort))
                            {
                                port = parsedPort;
                            }
                            break;
                        case "user id":
                        case "uid":
                        case "user":
                        case "username":
                            username = value;
                            break;
                        case "password":
                        case "pwd":
                            password = value;
                            break;
                        case "ssl mode":
                        case "sslmode":
                            useSsl = value.ToLowerInvariant() != "disable" && 
                                   value.ToLowerInvariant() != "disabled";
                            break;
                    }
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Helper function to split connection string parts
        /// </summary>
        private static IEnumerable<string> SplitConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return Array.Empty<string>();
                
            return connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        }
        
        /// <summary>
        /// Builds a connection string for Firebird database
        /// </summary>
        public static string BuildFirebirdConnectionString(
            string databaseFile, 
            string username = "",
            string password = "",
            string version = "",
            bool readOnly = true,
            string additionalOptions = "")
        {
            var connectionStringParts = new List<string>();
            
            // Add database file
            connectionStringParts.Add($"Database={databaseFile}");
            
            // Add credentials
            connectionStringParts.Add($"User={username}");
            connectionStringParts.Add($"Password={password}");
            
            // Add version if specified
            if (!string.IsNullOrEmpty(version))
            {
                connectionStringParts.Add($"Version={version}");
            }
            
            // Add additional options if provided
            if (!string.IsNullOrWhiteSpace(additionalOptions))
            {
                // Remove any leading semicolons
                additionalOptions = additionalOptions.TrimStart(';');
                
                if (!string.IsNullOrWhiteSpace(additionalOptions))
                {
                    connectionStringParts.Add(additionalOptions);
                }
            }
            
            return string.Join(";", connectionStringParts);
        }
        
        /// <summary>
        /// Tries to parse a Firebird connection string into its components
        /// </summary>
        public static bool TryParseFirebirdConnectionString(
            string connectionString,
            out string databaseFile,
            out string username,
            out string password,
            out string version)
        {
            databaseFile = string.Empty;
            username = "SYSDBA";
            password = "masterkey";
            version = string.Empty;
            
            try
            {
                var parts = SplitConnectionString(connectionString);
                
                foreach (var part in parts)
                {
                    var keyValue = part.Split('=');
                    if (keyValue.Length != 2)
                        continue;
                    
                    string key = keyValue[0].Trim();
                    string value = keyValue[1].Trim();
                    
                    switch (key.ToLowerInvariant())
                    {
                        case "file":
                        case "database":
                            databaseFile = value;
                            break;
                        case "user":
                        case "username":
                        case "userid":
                            username = value;
                            break;
                        case "password":
                        case "pwd":
                            password = value;
                            break;
                        case "version":
                            version = value;
                            break;
                    }
                }
                
                return !string.IsNullOrWhiteSpace(databaseFile);
            }
            catch
            {
                return false;
            }
        }
    }
}