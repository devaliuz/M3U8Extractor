namespace YtDlpExtractor.Configuration
{
    /// <summary>
    /// Options für den Extract-Command
    /// </summary>
    public class ExtractCommandOptions
    {
        public string Url { get; set; } = "";
        public string? SeriesName { get; set; }
        public string Host { get; set; } = "auto";
        public int StartSeason { get; set; } = 1;
        public int StartEpisode { get; set; } = 1;
        public int? MaxEpisodes { get; set; }
        public bool ForceRescrape { get; set; } = false;
        public bool SkipExisting {  get; set; } = true;

        public bool IsValid => !string.IsNullOrEmpty(Url);

        public void Validate()
        {
            if (string.IsNullOrEmpty(Url))
                throw new ArgumentException("URL ist erforderlich");

            if (StartSeason <= 0)
                throw new ArgumentException("Start-Staffel muss größer als 0 sein");

            if (StartEpisode <= 0)
                throw new ArgumentException("Start-Episode muss größer als 0 sein");

            if (MaxEpisodes.HasValue && MaxEpisodes <= 0)
                throw new ArgumentException("Max-Episoden muss größer als 0 sein");
        }
    }

    /// <summary>
    /// Options für den Download-Command
    /// </summary>
    public class DownloadCommandOptions
    {
        public string? SeriesName { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public string OutputDir { get; set; } = "./Downloads";
        public int Parallel { get; set; } = 2;
        public string Quality { get; set; } = "best";

        public void Validate()
        {
            if (Parallel <= 0)
                throw new ArgumentException("Parallel-Downloads müssen größer als 0 sein");

            if (Season.HasValue && Season <= 0)
                throw new ArgumentException("Staffel muss größer als 0 sein");

            if (Episode.HasValue && Episode <= 0)
                throw new ArgumentException("Episode muss größer als 0 sein");

            if (string.IsNullOrEmpty(Quality))
                throw new ArgumentException("Qualität darf nicht leer sein");
        }

        public string GetSafeOutputDir()
        {
            return Path.GetFullPath(OutputDir);
        }
    }

    /// <summary>
    /// Options für den Export-Command
    /// </summary>
    public class ExportCommandOptions
    {
        public string Format { get; set; } = "json";
        public string? SeriesName { get; set; }
        public string? OutputFile { get; set; }

        public static readonly string[] SupportedFormats = { "json", "csv", "batch" };

        public void Validate()
        {
            if (!SupportedFormats.Contains(Format.ToLower()))
                throw new ArgumentException($"Unbekanntes Format '{Format}'. Unterstützte Formate: {string.Join(", ", SupportedFormats)}");
        }

        public string GetFileName()
        {
            if (!string.IsNullOrEmpty(OutputFile))
                return OutputFile;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var prefix = !string.IsNullOrEmpty(SeriesName)
                ? $"{SeriesName}_{timestamp}"
                : $"ytdlp_links_{timestamp}";

            return $"{prefix}.{Format.ToLower()}";
        }
    }

    /// <summary>
    /// Options für den Status-Command
    /// </summary>
    public class StatusCommandOptions
    {
        public string? SeriesName { get; set; }
        public bool ShowDetailed { get; set; } = false;
        public bool ShowOnlyActive { get; set; } = false;

        public bool HasSeriesFilter => !string.IsNullOrEmpty(SeriesName);
    }

    /// <summary>
    /// Options für den Validate-Command
    /// </summary>
    public class ValidateCommandOptions
    {
        public string? SeriesName { get; set; }
        public bool ForceRevalidation { get; set; } = false;
        public int BatchSize { get; set; } = 50;
        public int TimeoutSeconds { get; set; } = 10;

        public void Validate()
        {
            if (BatchSize <= 0)
                throw new ArgumentException("Batch-Größe muss größer als 0 sein");

            if (TimeoutSeconds <= 0)
                throw new ArgumentException("Timeout muss größer als 0 sein");
        }
    }

    /// <summary>
    /// Options für den Cleanup-Command
    /// </summary>
    public class CleanupCommandOptions
    {
        public int Days { get; set; } = 30;
        public bool DryRun { get; set; } = false;
        public bool CleanupInvalidLinks { get; set; } = true;
        public bool CleanupFailedSeries { get; set; } = true;
        public bool CleanupEmptySeries { get; set; } = false;

        public void Validate()
        {
            if (Days <= 0)
                throw new ArgumentException("Tage müssen größer als 0 sein");
        }

        public DateTime GetCutoffDate()
        {
            return DateTime.UtcNow.AddDays(-Days);
        }
    }

    /// <summary>
    /// Base class für alle Command Options mit gemeinsamen Features
    /// </summary>
    public abstract class BaseCommandOptions
    {
        public bool Verbose { get; set; } = false;
        public bool Quiet { get; set; } = false;
        public string? LogFile { get; set; }

        public virtual void Validate() { }

        protected void EnsureNotBothQuietAndVerbose()
        {
            if (Verbose && Quiet)
                throw new ArgumentException("Verbose und Quiet können nicht gleichzeitig aktiviert sein");
        }
    }
}