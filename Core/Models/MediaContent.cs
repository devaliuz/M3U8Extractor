using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YtDlpExtractor.Core.Models
{
    // Basis-Klasse für alle Media-Inhalte
    public abstract class MediaContent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(500)]
        public string Name { get; set; } = "";

        [MaxLength(500)]
        public string CleanName { get; set; } = "";

        [MaxLength(1000)]
        public string OriginalUrl { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public MediaStatus Status { get; set; } = MediaStatus.Pending;

        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }
    }

    // Serie (Hauptcontainer)
    public class Series : MediaContent
    {
        public virtual ICollection<Season> Seasons { get; set; } = new List<Season>();

        [MaxLength(200)]
        public string? Description { get; set; }

        public int TotalSeasons => Seasons?.Count ?? 0;
        public int TotalEpisodes => Seasons?.Sum(s => s.Episodes?.Count ?? 0) ?? 0;
        public int FoundLinks => Seasons?.Sum(s => s.Episodes?.Sum(e => e.Links?.Count ?? 0) ?? 0) ?? 0;
    }

    // Staffel
    public class Season
    {
        [Key]
        public int Id { get; set; }

        public int Number { get; set; }

        [ForeignKey("Series")]
        public int SeriesId { get; set; }
        public virtual Series Series { get; set; } = null!;

        public virtual ICollection<Episode> Episodes { get; set; } = new List<Episode>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public int TotalEpisodes => Episodes?.Count ?? 0;
        public int FoundLinks => Episodes?.Sum(e => e.Links?.Count ?? 0) ?? 0;
    }

    // Episode
    public class Episode
    {
        [Key]
        public int Id { get; set; }

        public int Number { get; set; }

        [ForeignKey("Season")]
        public int SeasonId { get; set; }
        public virtual Season Season { get; set; } = null!;

        public virtual ICollection<DownloadableLink> Links { get; set; } = new List<DownloadableLink>();

        [MaxLength(500)]
        public string? Title { get; set; }

        [MaxLength(1000)]
        public string? OriginalUrl { get; set; }

        public EpisodeStatus Status { get; set; } = EpisodeStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        public int LinkCount => Links?.Count ?? 0;
        public bool HasValidLinks => Links?.Any(l => l.IsValid) ?? false;
    }

    // Downloadbarer Link
    public class DownloadableLink
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Episode")]
        public int EpisodeId { get; set; }
        public virtual Episode Episode { get; set; } = null!;

        [Required]
        [MaxLength(2000)]
        public string Url { get; set; } = "";

        [Required]
        [MaxLength(50)]
        public string HostName { get; set; } = "";

        public LinkType Type { get; set; } = LinkType.Unknown;
        public LinkQuality Quality { get; set; } = LinkQuality.Unknown;

        public bool IsValid { get; set; } = true;
        public bool IsTested { get; set; } = false;

        public DateTime FoundAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastValidated { get; set; }

        [MaxLength(500)]
        public string? ValidationError { get; set; }

        // Download-Status für yt-dlp Integration
        public DownloadStatus DownloadStatus { get; set; } = DownloadStatus.NotStarted;
        public DateTime? DownloadStarted { get; set; }
        public DateTime? DownloadCompleted { get; set; }

        [MaxLength(1000)]
        public string? DownloadPath { get; set; }

        [MaxLength(1000)]
        public string? DownloadError { get; set; }
    }

    // Enums
    public enum MediaStatus
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    public enum EpisodeStatus
    {
        Pending = 0,
        Processing = 1,
        LinksFound = 2,
        NoLinksFound = 3,
        Failed = 4,
        Skipped = 5
    }

    public enum LinkType
    {
        Unknown = 0,
        M3U8 = 1,
        MP4 = 2,
        VidmolyEmbed = 3,
        Direct = 4
    }

    public enum LinkQuality
    {
        Unknown = 0,
        Low = 1,      // 480p oder weniger
        Medium = 2,   // 720p
        High = 3,     // 1080p
        Ultra = 4     // 4K+
    }

    public enum DownloadStatus
    {
        NotStarted = 0,
        Queued = 1,
        Downloading = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }

    // Extension Methods für bessere Usability
    public static class ModelExtensions
    {
        public static void UpdateStatus(this Episode episode, EpisodeStatus status, string? error = null)
        {
            episode.Status = status;
            episode.ErrorMessage = error;
            episode.UpdatedAt = DateTime.UtcNow;

            if (status == EpisodeStatus.LinksFound)
                episode.ProcessedAt = DateTime.UtcNow;
        }

        public static void UpdateStatus(this Series series, MediaStatus status, string? error = null)
        {
            series.Status = status;
            series.ErrorMessage = error;
            series.UpdatedAt = DateTime.UtcNow;
        }

        public static void MarkAsValid(this DownloadableLink link, bool isValid = true)
        {
            link.IsValid = isValid;
            link.IsTested = true;
            link.LastValidated = DateTime.UtcNow;
        }

        public static void UpdateDownloadStatus(this DownloadableLink link, DownloadStatus status, string? error = null, string? path = null)
        {
            link.DownloadStatus = status;
            link.DownloadError = error;
            link.DownloadPath = path;

            switch (status)
            {
                case DownloadStatus.Downloading:
                    link.DownloadStarted = DateTime.UtcNow;
                    break;
                case DownloadStatus.Completed:
                    link.DownloadCompleted = DateTime.UtcNow;
                    break;
            }
        }
    }
}