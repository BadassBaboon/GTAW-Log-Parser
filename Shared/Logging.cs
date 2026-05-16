using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace GTAWParser.Shared
{
    /// <summary>
    /// Configures Serilog's static <see cref="Log"/> logger with a rolling
    /// file sink under <c>%LocalAppData%\GTAW-Log-Parser\logs\</c>, plus an
    /// optional Sentry crash-reporting sink when a DSN is provided.
    /// </summary>
    public static class Logging
    {
        /// <summary>
        /// Environment variable read at startup for the Sentry DSN. Setting
        /// it opts in to crash reporting; leaving it unset disables Sentry
        /// entirely. There is no hardcoded fallback — privacy by default.
        /// </summary>
        public const string SentryDsnEnvVar = "GTAW_SENTRY_DSN";

        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GTAW-Log-Parser",
            "logs");

        /// <summary>
        /// Idempotent. Subsequent calls replace the previous logger.
        /// </summary>
        public static void Initialize(string appName)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
            }
            catch
            {
                // If we can't create the log directory we still want the
                // Debug sink so that an attached debugger sees output.
            }

            string? sentryDsn = Environment.GetEnvironmentVariable(SentryDsnEnvVar);

            LoggerConfiguration config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("App", appName)
                .WriteTo.File(
                    path: Path.Combine(LogDirectory, $"{appName}-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug();

            if (!string.IsNullOrWhiteSpace(sentryDsn))
            {
                config = config.WriteTo.Sentry(o =>
                {
                    o.Dsn = sentryDsn;
                    o.MinimumBreadcrumbLevel = LogEventLevel.Information;
                    o.MinimumEventLevel = LogEventLevel.Error;
                    o.AttachStacktrace = true;
                    o.Release = $"{appName}@{typeof(Logging).Assembly.GetName().Version}";
                });
            }

            Log.Logger = config.CreateLogger();

            if (!string.IsNullOrWhiteSpace(sentryDsn))
                Log.Information("Sentry crash reporting enabled for {App}", appName);
        }

        /// <summary>
        /// Flushes and closes the logger. Call from app exit.
        /// </summary>
        public static void Shutdown()
        {
            Log.CloseAndFlush();
        }
    }
}
