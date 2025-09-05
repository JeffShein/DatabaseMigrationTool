using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.IO;

namespace DatabaseMigrationTool.Services
{
    public static class LoggingService
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DatabaseMigrationTool",
            "Logs");

        public static Microsoft.Extensions.Logging.ILogger CreateLogger<T>()
        {
            return CreateLoggerFactory().CreateLogger<T>();
        }

        public static ILoggerFactory CreateLoggerFactory()
        {
            // Ensure log directory exists
            Directory.CreateDirectory(LogDirectory);

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "DatabaseMigrationTool")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine(LogDirectory, "application-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{ThreadId}] {Message:lj} {Properties}{NewLine}{Exception}")
                .CreateLogger();

            return new LoggerFactory().AddSerilog();
        }

        public static void ConfigureDevelopmentLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithThreadId()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine(LogDirectory, "debug-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{ThreadId}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        public static void CloseAndFlush()
        {
            Log.CloseAndFlush();
        }
    }
}