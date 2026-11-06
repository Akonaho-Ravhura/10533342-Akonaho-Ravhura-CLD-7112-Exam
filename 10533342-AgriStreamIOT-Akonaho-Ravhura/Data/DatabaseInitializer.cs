namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    /// <summary>
    /// Runs migrations and seeding after the web host has started, so a slow or unreachable
    /// database can no longer block startup (which caused HTTP 500.37 on Azure App Service).
    /// Retries a few times, then records the outcome in <see cref="DatabaseStatus"/>.
    /// </summary>
    public class DatabaseInitializer : BackgroundService
    {
        private const int MaxAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;
        private readonly DatabaseStatus _status;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(IServiceProvider services, IConfiguration config,
            DatabaseStatus status, ILogger<DatabaseInitializer> logger)
        {
            _services = services;
            _config = config;
            _status = status;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the host finish starting before touching the database.
            await Task.Yield();

            var connectionString = _config.GetConnectionString("DefaultConnection") ?? string.Empty;
            if (connectionString.Contains("<your-server>", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "ConnectionStrings:DefaultConnection still contains the '<your-server>' placeholder from appsettings.json. " +
                    "Set the DefaultConnection connection string in the Azure App Service configuration " +
                    "(Settings > Environment variables > Connection strings) and allow Azure services through the SQL server firewall.");
                _status.SetFailed("Connection string is not configured (placeholder value). See application logs.");
                return;
            }

            for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
            {
                _status.SetAttempt(attempt);
                try
                {
                    _logger.LogInformation("Database initialisation attempt {Attempt}/{Max}.", attempt, MaxAttempts);
                    await DbSeeder.SeedAsync(_services);
                    _status.SetReady();
                    _logger.LogInformation("Database initialisation completed.");
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Database initialisation attempt {Attempt}/{Max} failed.", attempt, MaxAttempts);
                    if (attempt == MaxAttempts)
                    {
                        _status.SetFailed($"Database initialisation failed after {MaxAttempts} attempts ({ex.GetType().Name}). See application logs.");
                        return;
                    }
                }

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
