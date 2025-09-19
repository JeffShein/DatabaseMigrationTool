using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Generic;

namespace DatabaseMigrationTool.Services
{
    /// <summary>
    /// Uses AssemblyLoadContext to isolate different Firebird client versions
    /// </summary>
    public class FirebirdContextConnector : IDisposable
    {
        private readonly FirebirdAssemblyContext _context;
        private readonly string _version;
        private Assembly? _firebirdAssembly;
        private Type? _connectionType;
        private bool _disposed;

        public FirebirdContextConnector(string version)
        {
            _version = version;
            _context = new FirebirdAssemblyContext(version);
        }

        public bool TestConnection(string connectionString)
        {
            try
            {
                if (_firebirdAssembly == null)
                {
                    LoadFirebirdAssembly();
                }

                // Create Firebird connection using the isolated assembly
                var connectionInstance = Activator.CreateInstance(_connectionType!, connectionString);
                var openMethod = _connectionType!.GetMethod("Open");
                var closeMethod = _connectionType!.GetMethod("Close");

                openMethod?.Invoke(connectionInstance, null);
                closeMethod?.Invoke(connectionInstance, null);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Firebird connection failed: {ex.Message}");
                return false;
            }
        }

        private void LoadFirebirdAssembly()
        {
            // Load the appropriate Firebird client assembly
            string assemblyPath = GetFirebirdAssemblyPath(_version);
            _firebirdAssembly = _context.LoadFromAssemblyPath(assemblyPath);
            _connectionType = _firebirdAssembly.GetType("FirebirdSql.Data.FirebirdClient.FbConnection");

            if (_connectionType == null)
            {
                throw new InvalidOperationException($"Could not find FbConnection type in assembly {assemblyPath}");
            }
        }

        private string GetFirebirdAssemblyPath(string version)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            return version switch
            {
                "2.5" => Path.Combine(baseDir, "Firebird25", "FirebirdSql.Data.FirebirdClient.dll"),
                "5.0" => Path.Combine(baseDir, "Firebird50", "FirebirdSql.Data.FirebirdClient.dll"),
                _ => throw new ArgumentException($"Unsupported Firebird version: {version}")
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _context?.Unload();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Custom AssemblyLoadContext for Firebird client isolation
    /// </summary>
    internal class FirebirdAssemblyContext : AssemblyLoadContext
    {
        private readonly string _firebirdVersion;
        private readonly Dictionary<string, Assembly> _loadedAssemblies = new();

        public FirebirdAssemblyContext(string firebirdVersion) : base($"Firebird_{firebirdVersion}", isCollectible: true)
        {
            _firebirdVersion = firebirdVersion;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Only intercept Firebird-related assemblies
            if (assemblyName.Name?.StartsWith("FirebirdSql") == true)
            {
                string assemblyPath = GetAssemblyPath(assemblyName.Name);
                if (File.Exists(assemblyPath))
                {
                    if (!_loadedAssemblies.ContainsKey(assemblyName.Name))
                    {
                        _loadedAssemblies[assemblyName.Name] = LoadFromAssemblyPath(assemblyPath);
                    }
                    return _loadedAssemblies[assemblyName.Name];
                }
            }

            // For all other assemblies, use default loading
            return null;
        }

        private string GetAssemblyPath(string assemblyName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string versionDir = _firebirdVersion == "2.5" ? "Firebird25" : "Firebird50";
            return Path.Combine(baseDir, versionDir, $"{assemblyName}.dll");
        }
    }
}