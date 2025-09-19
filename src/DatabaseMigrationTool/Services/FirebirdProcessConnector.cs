using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DatabaseMigrationTool.Services
{
    /// <summary>
    /// Connects to Firebird databases using separate processes to avoid DLL conflicts
    /// </summary>
    public class FirebirdProcessConnector
    {
        public class FirebirdConnectionResult
        {
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public string? ServerVersion { get; set; }
            public int DatabaseSizePages { get; set; }
            public string? CharacterSet { get; set; }
        }

        /// <summary>
        /// Test connection to Firebird database using process isolation
        /// </summary>
        public static async Task<FirebirdConnectionResult> TestConnectionAsync(string version, string connectionString, int timeoutMs = 30000)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{GetFirebirdTestProjectPath()}\" -- \"{version}\" \"{connectionString}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(GetFirebirdTestProjectPath())
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    process.Kill();
                    return new FirebirdConnectionResult
                    {
                        Success = false,
                        ErrorMessage = $"Connection test timed out after {timeoutMs}ms"
                    };
                }

                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    try
                    {
                        return JsonSerializer.Deserialize<FirebirdConnectionResult>(output)
                            ?? new FirebirdConnectionResult { Success = false, ErrorMessage = "Failed to parse response" };
                    }
                    catch (JsonException)
                    {
                        return new FirebirdConnectionResult
                        {
                            Success = false,
                            ErrorMessage = $"Invalid response format: {output}"
                        };
                    }
                }
                else
                {
                    return new FirebirdConnectionResult
                    {
                        Success = false,
                        ErrorMessage = string.IsNullOrEmpty(error) ? $"Process failed with exit code {process.ExitCode}" : error
                    };
                }
            }
            catch (Exception ex)
            {
                return new FirebirdConnectionResult
                {
                    Success = false,
                    ErrorMessage = $"Process execution failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Get the path to the Firebird test utility project
        /// </summary>
        private static string GetFirebirdTestProjectPath()
        {
            // For now, return the main project path - we'll create a separate test utility later
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(Path.GetDirectoryName(currentDir)!, "FirebirdTestUtility", "FirebirdTestUtility.csproj");
        }

        /// <summary>
        /// Execute a simple query using process isolation
        /// </summary>
        public static async Task<FirebirdConnectionResult> ExecuteTestQueryAsync(string version, string connectionString, string query = "SELECT FIRST 1 RDB$RELATION_NAME FROM RDB$RELATIONS")
        {
            // Similar implementation to TestConnectionAsync but with query execution
            // This would be implemented when we create the separate test utility
            return await TestConnectionAsync(version, connectionString);
        }
    }
}