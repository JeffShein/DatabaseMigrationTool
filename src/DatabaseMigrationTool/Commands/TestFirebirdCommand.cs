using CommandLine;
using DatabaseMigrationTool.Providers;
using System;
using System.Threading.Tasks;

namespace DatabaseMigrationTool.Commands
{
    [Verb("test-firebird", HelpText = "Test Firebird 5.0 connection with manual override")]
    public class TestFirebirdOptions
    {
        [Option('c', "connection", Required = true, HelpText = "Firebird connection string")]
        public string ConnectionString { get; set; } = string.Empty;
        
        [Option('v', "version", Default = "auto", HelpText = "Version parameter (ignored - FB 5.0 client used for all databases)")]
        public string Version { get; set; } = "auto";
        
        [Option("verbose", Default = false, HelpText = "Show verbose logging")]
        public bool Verbose { get; set; }
    }
    
    public class TestFirebirdCommand
    {
        public static Task<int> Execute(TestFirebirdOptions options)
        {
            try
            {
                Console.WriteLine("=== Firebird Connection Test ===");
                Console.WriteLine("Using FB 5.0 client with backward compatibility for all database versions");
                Console.WriteLine($"Connection string: {MaskPassword(options.ConnectionString)}");
                Console.WriteLine();
                
                // Create Firebird provider
                var firebirdProvider = new FirebirdProvider();
                
                // Set up console logging
                firebirdProvider.SetLogger(message => 
                {
                    if (options.Verbose || message.Contains("ERROR") || message.Contains("SUCCESS") || message.Contains("FAILED"))
                    {
                        Console.WriteLine($"[FB] {message}");
                    }
                });
                
                // Use connection string as-is (Version parameter no longer needed)
                string testConnectionString = options.ConnectionString;
                
                Console.WriteLine($"Testing with connection string: {MaskPassword(testConnectionString)}");
                Console.WriteLine();
                
                // Test the connection
                bool success = TestConnection(firebirdProvider, testConnectionString);
                
                Console.WriteLine();
                if (success)
                {
                    Console.WriteLine("✅ Connection test PASSED!");
                    Console.WriteLine("Firebird database is accessible using FB 5.0 client with backward compatibility.");
                    return Task.FromResult(0);
                }
                else
                {
                    Console.WriteLine("❌ Connection test FAILED!");
                    Console.WriteLine("Firebird database is not accessible.");
                    Console.WriteLine("Check the log output above for detailed error information.");
                    return Task.FromResult(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test execution failed: {ex.Message}");
                if (options.Verbose)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                return Task.FromResult(1);
            }
        }

        private static bool TestConnection(FirebirdProvider provider, string connectionString)
        {
            try
            {
                using var connection = provider.CreateConnection(connectionString);
                connection.Open();
                connection.Close();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection test failed: {ex.Message}");
                return false;
            }
        }

        private static string MaskPassword(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return connectionString;
                
            return System.Text.RegularExpressions.Regex.Replace(
                connectionString, 
                @"Password=[^;]*", 
                "Password=******", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}