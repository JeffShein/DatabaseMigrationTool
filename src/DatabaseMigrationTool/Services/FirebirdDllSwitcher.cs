using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DatabaseMigrationTool.Services
{
    /// <summary>
    /// Switches DLL search paths to enable different Firebird versions
    /// </summary>
    public class FirebirdDllSwitcher : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string? lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetDllDirectory(uint nBufferLength, System.Text.StringBuilder? lpBuffer);

        private readonly string? _originalDllDirectory;
        private bool _disposed;

        public FirebirdDllSwitcher()
        {
            // Save current DLL directory
            var buffer = new System.Text.StringBuilder(260);
            uint length = GetDllDirectory((uint)buffer.Capacity, buffer);
            _originalDllDirectory = length > 0 ? buffer.ToString() : null;
        }

        /// <summary>
        /// Switch to Firebird version-specific DLL directory
        /// </summary>
        public bool SwitchToVersion(string version)
        {
            try
            {
                string dllPath = GetFirebirdDllPath(version);

                if (!Directory.Exists(dllPath))
                {
                    throw new DirectoryNotFoundException($"Firebird DLL directory not found: {dllPath}");
                }

                return SetDllDirectory(dllPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to switch DLL directory: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restore original DLL directory
        /// </summary>
        public bool RestoreOriginalDirectory()
        {
            return SetDllDirectory(_originalDllDirectory);
        }

        private string GetFirebirdDllPath(string version)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            return version switch
            {
                "2.5" => Path.Combine(baseDir, "FirebirdDlls", "v25"),
                "5.0" => Path.Combine(baseDir, "FirebirdDlls", "v50"),
                _ => throw new ArgumentException($"Unsupported Firebird version: {version}")
            };
        }

        /// <summary>
        /// Test if switching works by trying to connect
        /// </summary>
        public static bool TestVersionSwitch(string version, string connectionString)
        {
            using var switcher = new FirebirdDllSwitcher();

            if (!switcher.SwitchToVersion(version))
            {
                return false;
            }

            try
            {
                // Force load the correct Firebird DLL by creating a connection
                using var connection = new FirebirdSql.Data.FirebirdClient.FbConnection(connectionString);
                connection.Open();
                connection.Close();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                switcher.RestoreOriginalDirectory();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                RestoreOriginalDirectory();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}