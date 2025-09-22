using System;
using FirebirdSql.Data.FirebirdClient;

namespace FirebirdTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Minimal Firebird 5.0 Connection Test ===");
            Console.WriteLine();

            string databasePath = "C:\\Dev\\DatabaseMigrationTool\\POSNL50.FDB";
            Console.WriteLine($"Testing connection to: {databasePath}");
            Console.WriteLine();

            // Test different connection string variations
            TestConnection("Minimal", $"User=SYSDBA;Password=Hosis11223344;Database={databasePath};ServerType=1;");
            TestConnection("With DataSource=localhost", $"DataSource=localhost;User=SYSDBA;Password=Hosis11223344;Database={databasePath};ServerType=1;");
            TestConnection("With empty DataSource", $"DataSource=;User=SYSDBA;Password=Hosis11223344;Database={databasePath};ServerType=1;");
            TestConnection("No ServerType", $"User=SYSDBA;Password=Hosis11223344;Database={databasePath};");
            TestConnection("ServerType=0", $"User=SYSDBA;Password=Hosis11223344;Database={databasePath};ServerType=0;");

            Console.WriteLine();
            Console.WriteLine("Test completed. Press any key to exit.");
            Console.ReadKey();
        }

        static void TestConnection(string testName, string connectionString)
        {
            Console.WriteLine($"--- {testName} ---");
            Console.WriteLine($"Connection string: {MaskPassword(connectionString)}");

            try
            {
                using var connection = new FbConnection(connectionString);
                Console.WriteLine("✓ FbConnection created successfully");

                connection.Open();
                Console.WriteLine("✓ Connection opened successfully");

                // Try a simple query
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT CURRENT_TIMESTAMP FROM RDB$DATABASE";
                var result = command.ExecuteScalar();
                Console.WriteLine($"✓ Query executed successfully: {result}");

                connection.Close();
                Console.WriteLine("✓ Connection closed successfully");

                Console.WriteLine($"SUCCESS: {testName} worked!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ FAILED: {ex.Message}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"  Inner exception: {ex.InnerException.Message}");
                }
            }

            Console.WriteLine();
        }

        static string MaskPassword(string connectionString)
        {
            return connectionString.Replace("Password=Hosis11223344", "Password=******");
        }
    }
}