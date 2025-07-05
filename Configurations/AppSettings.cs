namespace YtDlpExtractor.Configuration
{
    /// <summary>
    /// Zentrale Anwendungseinstellungen
    /// </summary>
    public class AppSettings
    {
        // Database Settings
        public DatabaseSettings Database { get; set; } = new DatabaseSettings();

        // Logging Settings
        public LoggingSettings Logging { get; set; } = new LoggingSettings();

        // Default Paths
        public PathSettings Paths { get; set; } = new PathSettings();

        // Performance Settings
        public PerformanceSettings Performance { get; set; } = new PerformanceSettings();

        // Browser Settings
        public BrowserSettings Browser { get; set; } = new BrowserSettings();

        public void Validate()
        {
            Database.Validate();
            Logging.Validate();
            Paths.Validate();
            Performance.Validate();
            Browser.Validate();
        }
    }

    /// <summary>
    /// Datenbank-Einstellungen
    /// </summary>
    public class DatabaseSettings
    {
        public string ConnectionString { get; set; } = "Data Source=ytdlp_extractor.db";
        public bool EnableAutoMigration { get; set; } = true;
        public bool EnableSensitiveDataLogging { get; set; } = false;
        public int CommandTimeout { get; set; } = 30; // Sekunden
        public bool EnableConnectionPooling { get; set; } = true;
        public bool EnableRetryOnFailure { get; set; } = true;
        public int MaxRetryCount { get; set; } = 3;

        public void Validate()
        {
            if (string.IsNullOrEmpty(ConnectionString))
                throw new ArgumentException("ConnectionString darf nicht leer sein");

            if (CommandTimeout <= 0)
                throw new ArgumentException("CommandTimeout muss größer als 0 sein");

            if (MaxRetryCount < 0)
                throw new ArgumentException("MaxRetryCount darf nicht negativ sein");
        }

        public string GetDatabasePath()
        {
            // Extrahiere Pfad aus SQLite Connection String
            var match = System.Text.RegularExpressions.Regex.Match(
                ConnectionString, @"Data Source=([^;]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value : "ytdlp_extractor.db";
        }
    }

    /// <summary>
    /// Logging-Einstellungen
    /// </summary>
    public class LoggingSettings
    {
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
        public bool EnableConsoleLogging { get; set; } = true;
        public bool EnableFileLogging { get; set; } = false;
        public string LogFilePath { get; set; } = "logs/ytdlp-extractor.log";
        public bool EnableErrorLogging { get; set; } = true;
        public string ErrorLogPath { get; set; } = "logs/errors.log";
        public int MaxLogFileSizeMB { get; set; } = 10;
        public int MaxLogFiles { get; set; } = 5;
        public bool LogToDatabase { get; set; } = false;

        public void Validate()
        {
            if (EnableFileLogging && string.IsNullOrEmpty(LogFilePath))
                throw new ArgumentException("LogFilePath ist erforderlich wenn FileLogging aktiviert ist");

            if (EnableErrorLogging && string.IsNullOrEmpty(ErrorLogPath))
                throw new ArgumentException("ErrorLogPath ist erforderlich wenn ErrorLogging aktiviert ist");

            if (MaxLogFileSizeMB <= 0)
                throw new ArgumentException("MaxLogFileSizeMB muss größer als 0 sein");

            if (MaxLogFiles <= 0)
                throw new ArgumentException("MaxLogFiles muss größer als 0 sein");
        }

        public void EnsureLogDirectoriesExist()
        {
            if (EnableFileLogging)
            {
                var logDir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(logDir))
                    Directory.CreateDirectory(logDir);
            }

            if (EnableErrorLogging)
            {
                var errorDir = Path.GetDirectoryName(ErrorLogPath);
                if (!string.IsNullOrEmpty(errorDir))
                    Directory.CreateDirectory(errorDir);
            }
        }
    }

    /// <summary>
    /// Pfad-Einstellungen
    /// </summary>
    public class PathSettings
    {
        public string DefaultDownloadDirectory { get; set; } = "./Downloads";
        public string TempDirectory { get; set; } = "./temp";
        public string CacheDirectory { get; set; } = "./cache";
        public string ExportDirectory { get; set; } = "./exports";
        public string ChromeDriverPath { get; set; } = ""; // Leer = automatisch
        public string YtDlpPath { get; set; } = "yt-dlp"; // System PATH

        public void Validate()
        {
            // Validierung ist optional da Pfade zur Laufzeit erstellt werden können
        }

        public void EnsureDirectoriesExist()
        {
            var directories = new[]
            {
                DefaultDownloadDirectory,
                TempDirectory,
                CacheDirectory,
                ExportDirectory
            };

            foreach (var dir in directories)
            {
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }

        public string GetFullDownloadPath()
        {
            return Path.GetFullPath(DefaultDownloadDirectory);
        }

        public string GetFullTempPath()
        {
            return Path.GetFullPath(TempDirectory);
        }
    }

    /// <summary>
    /// Performance-Einstellungen
    /// </summary>
    public class PerformanceSettings
    {
        public int MaxConcurrentExtractions { get; set; } = 1;
        public int MaxConcurrentDownloads { get; set; } = 2;
        public int DefaultTimeoutSeconds { get; set; } = 30;
        public int PageLoadTimeoutSeconds { get; set; } = 10;
        public int NetworkTimeoutSeconds { get; set; } = 15;
        public int DelayBetweenRequests { get; set; } = 500; // Milliseconds
        public bool EnableCaching { get; set; } = true;
        public int CacheExpirationHours { get; set; } = 24;

        public void Validate()
        {
            if (MaxConcurrentExtractions <= 0)
                throw new ArgumentException("MaxConcurrentExtractions muss größer als 0 sein");

            if (MaxConcurrentDownloads <= 0)
                throw new ArgumentException("MaxConcurrentDownloads muss größer als 0 sein");

            if (DefaultTimeoutSeconds <= 0)
                throw new ArgumentException("DefaultTimeoutSeconds muss größer als 0 sein");

            if (PageLoadTimeoutSeconds <= 0)
                throw new ArgumentException("PageLoadTimeoutSeconds muss größer als 0 sein");

            if (NetworkTimeoutSeconds <= 0)
                throw new ArgumentException("NetworkTimeoutSeconds muss größer als 0 sein");

            if (DelayBetweenRequests < 0)
                throw new ArgumentException("DelayBetweenRequests darf nicht negativ sein");

            if (CacheExpirationHours <= 0)
                throw new ArgumentException("CacheExpirationHours muss größer als 0 sein");
        }
    }

    /// <summary>
    /// Browser-Einstellungen
    /// </summary>
    public class BrowserSettings
    {
        public bool Headless { get; set; } = true;
        public bool DisableImages { get; set; } = true;
        public bool DisableJavaScript { get; set; } = false;
        public bool DisableGpu { get; set; } = true;
        public bool NoSandbox { get; set; } = true;
        public string UserAgent { get; set; } = "";
        public List<string> AdditionalArguments { get; set; } = new List<string>();
        public bool EnableNetworkMonitoring { get; set; } = true;
        public bool SavePageSource { get; set; } = false;
        public string? ProxyServer { get; set; }

        public void Validate()
        {
            // Browser-Settings sind meistens optional
        }

        public List<string> GetChromeOptions()
        {
            var options = new List<string>();

            if (Headless)
                options.Add("--headless");

            if (DisableImages)
                options.Add("--disable-images");

            if (DisableGpu)
                options.Add("--disable-gpu");

            if (NoSandbox)
                options.Add("--no-sandbox");

            if (!string.IsNullOrEmpty(UserAgent))
                options.Add($"--user-agent={UserAgent}");

            if (!string.IsNullOrEmpty(ProxyServer))
                options.Add($"--proxy-server={ProxyServer}");

            options.Add("--disable-blink-features=AutomationControlled");
            options.Add("--disable-dev-shm-usage");
            options.Add("--disable-logging");
            options.Add("--log-level=3");
            options.Add("--silent");

            options.AddRange(AdditionalArguments);

            return options;
        }
    }

    /// <summary>
    /// Log-Level Enum
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }

    /// <summary>
    /// Settings-Loader für verschiedene Quellen
    /// </summary>
    public static class SettingsLoader
    {
        public static AppSettings LoadDefault()
        {
            return new AppSettings();
        }

        public static AppSettings LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return LoadDefault();

            try
            {
                var json = File.ReadAllText(filePath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                settings?.Validate();
                return settings ?? LoadDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Laden der Settings aus {filePath}: {ex.Message}");
                return LoadDefault();
            }
        }

        public static void SaveToFile(AppSettings settings, string filePath)
        {
            try
            {
                settings.Validate();
                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Fehler beim Speichern der Settings nach {filePath}: {ex.Message}");
            }
        }
    }
}