using YtDlpExtractor.Core.Models;

namespace YtDlpExtractor.Configuration
{
    /// <summary>
    /// Options für den ExtractionService
    /// </summary>
    public class ExtractionOptions
    {
        public string? SeriesName { get; set; }
        public string PreferredHost { get; set; } = "auto";
        public int StartSeason { get; set; } = 1;
        public int StartEpisode { get; set; } = 1;
        public int? MaxEpisodes { get; set; }
        public int MaxConsecutiveErrors { get; set; } = 5;
        public int DelayBetweenEpisodes { get; set; } = 500; // Milliseconds
        public int DelayAfterError { get; set; } = 2000; // Milliseconds
        public bool ContinueOnError { get; set; } = true;
        public bool SkipExistingEpisodes { get; set; } = false;

        public void Validate()
        {
            if (StartSeason <= 0)
                throw new ArgumentException("Start-Staffel muss größer als 0 sein");

            if (StartEpisode <= 0)
                throw new ArgumentException("Start-Episode muss größer als 0 sein");

            if (MaxEpisodes.HasValue && MaxEpisodes <= 0)
                throw new ArgumentException("Max-Episoden muss größer als 0 sein");

            if (MaxConsecutiveErrors <= 0)
                throw new ArgumentException("Max-Consecutive-Errors muss größer als 0 sein");

            if (DelayBetweenEpisodes < 0)
                throw new ArgumentException("Delay-Between-Episodes darf nicht negativ sein");

            if (DelayAfterError < 0)
                throw new ArgumentException("Delay-After-Error darf nicht negativ sein");
        }
    }

    /// <summary>
    /// Ergebnis einer Extraktion
    /// </summary>
    public class ExtractionResult
    {
        public string StartUrl { get; set; } = "";
        public Series Series { get; set; } = null!;
        public int TotalEpisodes { get; set; }
        public int ProcessedEpisodes { get; set; }
        public int TotalLinksFound { get; set; }
        public int TotalErrors { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public TimeSpan Duration => (CompletedAt ?? DateTime.UtcNow) - StartedAt;
        public List<string> ErrorMessages { get; set; } = new List<string>();
        public Dictionary<string, int> HostStatistics { get; set; } = new Dictionary<string, int>();

        public bool HasErrors => TotalErrors > 0;
        public bool IsCompleted => CompletedAt.HasValue;
        public double SuccessRate => TotalEpisodes > 0 ? (double)(TotalEpisodes - TotalErrors) / TotalEpisodes * 100 : 0;

        public void AddError(string message)
        {
            TotalErrors++;
            ErrorMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public void AddFoundLink(string hostName)
        {
            TotalLinksFound++;
            if (HostStatistics.ContainsKey(hostName))
                HostStatistics[hostName]++;
            else
                HostStatistics[hostName] = 1;
        }

        public void Complete()
        {
            CompletedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Options für den DownloadService
    /// </summary>
    public class DownloadOptions
    {
        public string? SeriesName { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public string OutputDirectory { get; set; } = "./Downloads";
        public int MaxParallelDownloads { get; set; } = 2;
        public string Quality { get; set; } = "best";
        public string FileNameTemplate { get; set; } = "{SeriesName}_S{Season:D2}E{Episode:D2}";
        public bool OverwriteExisting { get; set; } = false;
        public bool CreateSubdirectories { get; set; } = true;
        public string YtDlpPath { get; set; } = "yt-dlp";
        public List<string> AdditionalYtDlpArgs { get; set; } = new List<string>();
        public int TimeoutMinutes { get; set; } = 30;
        public bool ValidateBeforeDownload { get; set; } = true;

        public void Validate()
        {
            if (MaxParallelDownloads <= 0)
                throw new ArgumentException("Max-Parallel-Downloads muss größer als 0 sein");

            if (string.IsNullOrEmpty(OutputDirectory))
                throw new ArgumentException("Output-Directory darf nicht leer sein");

            if (Season.HasValue && Season <= 0)
                throw new ArgumentException("Staffel muss größer als 0 sein");

            if (Episode.HasValue && Episode <= 0)
                throw new ArgumentException("Episode muss größer als 0 sein");

            if (TimeoutMinutes <= 0)
                throw new ArgumentException("Timeout muss größer als 0 sein");

            if (string.IsNullOrEmpty(Quality))
                throw new ArgumentException("Qualität darf nicht leer sein");

            if (string.IsNullOrEmpty(FileNameTemplate))
                throw new ArgumentException("FileNameTemplate darf nicht leer sein");
        }

        public string GetFullOutputPath()
        {
            return Path.GetFullPath(OutputDirectory);
        }

        public string FormatFileName(Series series, int season, int episode)
        {
            return FileNameTemplate
                .Replace("{SeriesName}", series.CleanName)
                .Replace("{Season:D2}", season.ToString("D2"))
                .Replace("{Season}", season.ToString())
                .Replace("{Episode:D2}", episode.ToString("D2"))
                .Replace("{Episode}", episode.ToString());
        }
    }

    /// <summary>
    /// Ergebnis eines Download-Vorgangs
    /// </summary>
    public class DownloadResult
    {
        public int TotalDownloads { get; set; }
        public int SuccessfulDownloads { get; set; }
        public int FailedDownloads { get; set; }
        public int SkippedDownloads { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public TimeSpan Duration => (CompletedAt ?? DateTime.UtcNow) - StartedAt;
        public List<DownloadError> Errors { get; set; } = new List<DownloadError>();
        public Dictionary<string, int> QualityStatistics { get; set; } = new Dictionary<string, int>();

        public bool HasErrors => FailedDownloads > 0;
        public bool IsCompleted => CompletedAt.HasValue;
        public double SuccessRate => TotalDownloads > 0 ? (double)SuccessfulDownloads / TotalDownloads * 100 : 0;

        public void AddError(string fileName, string error)
        {
            FailedDownloads++;
            Errors.Add(new DownloadError
            {
                FileName = fileName,
                ErrorMessage = error,
                Timestamp = DateTime.UtcNow
            });
        }

        public void AddSuccess(string quality)
        {
            SuccessfulDownloads++;
            if (QualityStatistics.ContainsKey(quality))
                QualityStatistics[quality]++;
            else
                QualityStatistics[quality] = 1;
        }

        public void Complete()
        {
            CompletedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Download-Fehler Details
    /// </summary>
    public class DownloadError
    {
        public string FileName { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Options für Link-Validierung
    /// </summary>
    public class ValidationOptions
    {
        public int TimeoutSeconds { get; set; } = 10;
        public int MaxRetries { get; set; } = 3;
        public int DelayBetweenRetries { get; set; } = 1000; // Milliseconds
        public bool ValidateRedirects { get; set; } = true;
        public bool CheckContentType { get; set; } = false;
        public List<string> AcceptedContentTypes { get; set; } = new List<string>
        {
            "application/vnd.apple.mpegurl",
            "video/mp4",
            "text/html"
        };

        public void Validate()
        {
            if (TimeoutSeconds <= 0)
                throw new ArgumentException("Timeout muss größer als 0 sein");

            if (MaxRetries < 0)
                throw new ArgumentException("Max-Retries darf nicht negativ sein");

            if (DelayBetweenRetries < 0)
                throw new ArgumentException("Delay-Between-Retries darf nicht negativ sein");
        }
    }

    /// <summary>
    /// Validierungs-Ergebnis
    /// </summary>
    public class ValidationResult
    {
        public int TotalLinks { get; set; }
        public int ValidLinks { get; set; }
        public int InvalidLinks { get; set; }
        public List<string> ValidationErrors { get; set; } = new List<string>();
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public TimeSpan Duration => (CompletedAt ?? DateTime.UtcNow) - StartedAt;

        public bool IsCompleted => CompletedAt.HasValue;
        public double ValidPercentage => TotalLinks > 0 ? (double)ValidLinks / TotalLinks * 100 : 0;

        public void Complete()
        {
            CompletedAt = DateTime.UtcNow;
        }

        public void AddValidationError(string error)
        {
            ValidationErrors.Add($"[{DateTime.Now:HH:mm:ss}] {error}");
        }
    }
}