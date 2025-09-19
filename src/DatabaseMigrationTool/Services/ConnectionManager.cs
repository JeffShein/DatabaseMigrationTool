using DatabaseMigrationTool.Constants;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using System.Collections.Concurrent;
using System.Data.Common;

namespace DatabaseMigrationTool.Services
{
    public interface IConnectionManager : IDisposable
    {
        Task<ConnectionResult> GetConnectionAsync(string providerName, string connectionString, CancellationToken cancellationToken = default);
        Task<ConnectionResult> TestConnectionAsync(string providerName, string connectionString, CancellationToken cancellationToken = default);
        Task ReturnConnectionAsync(DbConnection connection);
        Task CloseAllConnectionsAsync();
        int ActiveConnectionCount { get; }
    }
    
    public class ConnectionManager : IConnectionManager
    {
        private readonly ConcurrentDictionary<string, ConnectionPool> _connectionPools = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly IUserSettingsService _settingsService;
        private bool _disposed;
        
        public ConnectionManager(IUserSettingsService settingsService)
        {
            _settingsService = settingsService;
        }
        
        public int ActiveConnectionCount => _connectionPools.Values.Sum(pool => pool.ActiveConnections);
        
        public async Task<ConnectionResult> GetConnectionAsync(string providerName, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(providerName))
                {
                    return ConnectionResult.Fail("Provider name is required");
                }

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return ConnectionResult.Fail("Connection string is required");
                }

                var provider = DatabaseProviderFactory.Create(providerName);
                var enhancedConnectionString = EnhanceConnectionStringWithTimeouts(connectionString);

                // For Firebird, always create a new connection instead of using pooling
                // This prevents metadata locks from being held across operations
                if (providerName.Equals("Firebird", StringComparison.OrdinalIgnoreCase))
                {
                    var connection = provider.CreateConnection(enhancedConnectionString);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return ConnectionResult.Create(connection, connectionString, providerName);
                }

                // For other database types, use connection pooling
                var poolKey = GeneratePoolKey(providerName, enhancedConnectionString);

                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var pool = _connectionPools.GetOrAdd(poolKey, _ => new ConnectionPool(provider, enhancedConnectionString, _settingsService));
                    var connection = await pool.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

                    return ConnectionResult.Create(connection, connectionString, providerName);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail(ex, "GetConnection");
            }
        }
        
        public async Task<ConnectionResult> TestConnectionAsync(string providerName, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(providerName))
                {
                    return ConnectionResult.Fail("Provider name is required");
                }
                
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return ConnectionResult.Fail("Connection string is required");
                }
                
                var provider = DatabaseProviderFactory.Create(providerName);
                
                // Enhance connection string with timeout settings
                var enhancedConnectionString = EnhanceConnectionStringWithTimeouts(connectionString);
                
                using var connection = provider.CreateConnection(enhancedConnectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                
                return ConnectionResult.Create(connection, connectionString, providerName);
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail(ex, "TestConnection");
            }
        }
        
        public async Task ReturnConnectionAsync(DbConnection connection)
        {
            if (connection == null)
                return;

            try
            {
                // Check if this is a Firebird connection by examining the connection type
                bool isFirebirdConnection = connection.GetType().Name.Contains("FbConnection", StringComparison.OrdinalIgnoreCase);

                if (isFirebirdConnection)
                {
                    // Always close and dispose Firebird connections to release metadata locks
                    if (connection.State != System.Data.ConnectionState.Closed)
                    {
                        await connection.CloseAsync();
                    }
                    connection.Dispose();
                    return;
                }

                // For non-Firebird connections, use normal pooling logic
                var poolKey = FindPoolKeyForConnection(connection);
                if (poolKey != null && _connectionPools.TryGetValue(poolKey, out var pool))
                {
                    await pool.ReturnConnectionAsync(connection);
                }
                else
                {
                    // Connection not from a pool, dispose it
                    if (connection.State != System.Data.ConnectionState.Closed)
                    {
                        await connection.CloseAsync();
                    }
                    connection.Dispose();
                }
            }
            catch
            {
                // Ignore errors when returning connections
                try
                {
                    connection.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
        }
        
        public async Task CloseAllConnectionsAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                var closeTasks = _connectionPools.Values.Select(pool => pool.CloseAllAsync());
                await Task.WhenAll(closeTasks);
                _connectionPools.Clear();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Forcibly closes all connections and ensures clean state for Firebird DDL operations
        /// This is particularly important for Firebird metadata operations that require exclusive access
        /// </summary>
        public async Task EnsureCleanStateForFirebirdDDLAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                // Close all connection pools to ensure no lingering connections
                await CloseAllConnectionsAsync();

                // Give Firebird a moment to release all locks
                await Task.Delay(500);

                // Force garbage collection to ensure disposed connections are cleaned up
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Console.WriteLine("DEBUG: Ensured clean state for Firebird DDL operations");
            }
            finally
            {
                _semaphore.Release();
            }
        }
        
        private static string GeneratePoolKey(string providerName, string connectionString)
        {
            // Create a hash-based key to avoid storing connection strings directly
            var combined = $"{providerName}|{connectionString}";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(hashBytes);
        }
        
        private string? FindPoolKeyForConnection(DbConnection connection)
        {
            // This is a simplified approach - in a real implementation,
            // you might want to track connections more precisely
            return _connectionPools.Keys.FirstOrDefault(key =>
            {
                if (_connectionPools.TryGetValue(key, out var pool))
                {
                    return pool.ContainsConnection(connection);
                }
                return false;
            });
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                CloseAllConnectionsAsync().Wait(TimeSpan.FromSeconds(DatabaseConstants.ConnectionCloseTimeout));
                _semaphore.Dispose();
                _disposed = true;
            }
        }
        
        private string EnhanceConnectionStringWithTimeouts(string originalConnectionString)
        {
            var settings = _settingsService.Settings.Performance;

            // For Firebird connections, use manual parsing instead of DbConnectionStringBuilder
            // DbConnectionStringBuilder can corrupt Firebird connection strings
            if (originalConnectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase) &&
                (originalConnectionString.Contains(".fdb", StringComparison.OrdinalIgnoreCase) ||
                 originalConnectionString.Contains(".gdb", StringComparison.OrdinalIgnoreCase)))
            {
                // This is likely a Firebird connection string - handle manually
                string enhanced = originalConnectionString;

                // Add connection timeout if not present
                if (!enhanced.Contains("Connection Timeout", StringComparison.OrdinalIgnoreCase) &&
                    !enhanced.Contains("ConnectionTimeout", StringComparison.OrdinalIgnoreCase))
                {
                    enhanced += $";Connection Timeout={settings.ConnectionTimeout}";
                }

                return enhanced;
            }

            // For other connection types, use the standard approach
            try
            {
                var builder = new System.Data.Common.DbConnectionStringBuilder
                {
                    ConnectionString = originalConnectionString
                };

                // Add connection timeout if not already specified
                if (!builder.ContainsKey("Connection Timeout") && !builder.ContainsKey("ConnectionTimeout"))
                {
                    builder["Connection Timeout"] = settings.ConnectionTimeout;
                }

                // Add command timeout if not already specified
                if (!builder.ContainsKey("Command Timeout") && !builder.ContainsKey("CommandTimeout"))
                {
                    builder["Command Timeout"] = settings.CommandTimeout;
                }

                return builder.ConnectionString;
            }
            catch
            {
                // If DbConnectionStringBuilder fails, return original string
                return originalConnectionString;
            }
        }
    }
    
    internal class ConnectionPool
    {
        private readonly IDatabaseProvider _provider;
        private readonly string _connectionString;
        private readonly ConcurrentQueue<DbConnection> _availableConnections = new();
        private readonly ConcurrentDictionary<DbConnection, DateTime> _activeConnections = new();
        private readonly SemaphoreSlim _semaphore = new(10, 10); // Max 10 connections per pool
        private readonly Timer _cleanupTimer;
        
        public int ActiveConnections => _activeConnections.Count;
        
        private readonly IUserSettingsService _settingsService;
        
        public ConnectionPool(IDatabaseProvider provider, string connectionString, IUserSettingsService settingsService)
        {
            _provider = provider;
            _connectionString = connectionString;
            _settingsService = settingsService;
            
            // Clean up stale connections every 5 minutes
            _cleanupTimer = new Timer(CleanupStaleConnections, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }
        
        public async Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            try
            {
                // Try to get an available connection
                while (_availableConnections.TryDequeue(out var connection))
                {
                    if (IsConnectionValid(connection))
                    {
                        _activeConnections[connection] = DateTime.UtcNow;
                        return connection;
                    }
                    else
                    {
                        connection.Dispose();
                    }
                }
                
                // Create a new connection
                var newConnection = _provider.CreateConnection(_connectionString);
                await newConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                _activeConnections[newConnection] = DateTime.UtcNow;
                return newConnection;
            }
            catch
            {
                _semaphore.Release();
                throw;
            }
        }
        
        public async Task ReturnConnectionAsync(DbConnection connection)
        {
            if (_activeConnections.TryRemove(connection, out _))
            {
                if (IsConnectionValid(connection))
                {
                    _availableConnections.Enqueue(connection);
                }
                else
                {
                    try
                    {
                        if (connection.State != System.Data.ConnectionState.Closed)
                        {
                            await connection.CloseAsync();
                        }
                    }
                    finally
                    {
                        connection.Dispose();
                    }
                }
                
                _semaphore.Release();
            }
        }
        
        public bool ContainsConnection(DbConnection connection)
        {
            return _activeConnections.ContainsKey(connection);
        }
        
        public async Task CloseAllAsync()
        {
            // Close active connections
            var activeConnections = _activeConnections.Keys.ToList();
            foreach (var connection in activeConnections)
            {
                try
                {
                    if (connection.State != System.Data.ConnectionState.Closed)
                    {
                        await connection.CloseAsync();
                    }
                    connection.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            _activeConnections.Clear();
            
            // Close available connections
            while (_availableConnections.TryDequeue(out var connection))
            {
                try
                {
                    if (connection.State != System.Data.ConnectionState.Closed)
                    {
                        await connection.CloseAsync();
                    }
                    connection.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            _cleanupTimer?.Dispose();
        }
        
        private static bool IsConnectionValid(DbConnection connection)
        {
            return connection != null &&
                   connection.State == System.Data.ConnectionState.Open;
        }
        
        private void CleanupStaleConnections(object? state)
        {
            var staleThreshold = DateTime.UtcNow.AddMinutes(-30);
            var staleConnections = _activeConnections
                .Where(kvp => kvp.Value < staleThreshold)
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var connection in staleConnections)
            {
                if (_activeConnections.TryRemove(connection, out _))
                {
                    try
                    {
                        connection.Dispose();
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }
        
    }
}