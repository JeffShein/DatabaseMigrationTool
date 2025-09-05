using DatabaseMigrationTool.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatabaseMigrationTool.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabaseMigrationServices(this IServiceCollection services)
        {
            // Logging
            services.AddSingleton<ILoggerFactory>(LoggingService.CreateLoggerFactory());
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            
            // Core services
            services.AddSingleton<IConnectionManager, ConnectionManager>();
            services.AddSingleton<IConfigurationValidator, ConfigurationValidator>();
            services.AddSingleton<IValidationService, ValidationService>();
            services.AddSingleton<IUserSettingsService, UserSettingsService>();
            
            // Business services
            services.AddTransient<IExportService, ExportService>();
            services.AddTransient<IImportService, ImportService>();
            services.AddTransient<ISchemaService, SchemaService>();
            
            // Data access services
            services.AddTransient<IStreamingDataReader, StreamingDataReader>();
            services.AddTransient<StreamingDataWriter>();
            
            // Database providers - these will be resolved by factory
            services.AddTransient<SqlServerProvider>();
            services.AddTransient<MySqlProvider>();
            services.AddTransient<PostgreSqlProvider>();
            services.AddTransient<FirebirdProvider>();
            
            // Legacy services (keeping for backward compatibility during transition)
            services.AddTransient<DatabaseExporter>();
            services.AddTransient<DatabaseImporter>();
            services.AddTransient<TableImporter>();
            services.AddTransient<ConnectionProfileManager>();
            services.AddTransient<OperationRecoveryService>();
            
            // WPF UI components
            services.AddTransient<Views.MainWindow>();
            
            return services;
        }
        
        public static IServiceCollection AddErrorHandling(this IServiceCollection services)
        {
            // Initialize error handler if not already done
            Utilities.ErrorHandler.Initialize();
            
            return services;
        }
        
        public static IDatabaseProvider GetDatabaseProvider(this IServiceProvider serviceProvider, string providerName)
        {
            return providerName switch
            {
                Constants.DatabaseConstants.ProviderNames.SqlServer => serviceProvider.GetRequiredService<SqlServerProvider>(),
                Constants.DatabaseConstants.ProviderNames.MySQL => serviceProvider.GetRequiredService<MySqlProvider>(),
                Constants.DatabaseConstants.ProviderNames.PostgreSQL => serviceProvider.GetRequiredService<PostgreSqlProvider>(),
                Constants.DatabaseConstants.ProviderNames.Firebird => serviceProvider.GetRequiredService<FirebirdProvider>(),
                _ => throw new ArgumentException($"Unknown provider: {providerName}", nameof(providerName))
            };
        }
    }
}