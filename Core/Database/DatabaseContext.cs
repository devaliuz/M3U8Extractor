using Microsoft.EntityFrameworkCore;
using YtDlpExtractor.Core.Models;

namespace YtDlpExtractor.Core.Database
{
    public class DatabaseContext : DbContext
    {
        private readonly string _connectionString;

        public DatabaseContext() : this("Data Source=ytdlp_extractor.db")
        {
        }

        public DatabaseContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
        {
        }

        // DbSets
        public DbSet<Series> Series { get; set; } = null!;
        public DbSet<Season> Seasons { get; set; } = null!;
        public DbSet<Episode> Episodes { get; set; } = null!;
        public DbSet<DownloadableLink> Links { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite(_connectionString, options =>
                {
                    options.CommandTimeout(30);
                });

                // Development Settings
                optionsBuilder.EnableSensitiveDataLogging(false);
                optionsBuilder.EnableServiceProviderCaching(true);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Series Configuration
            modelBuilder.Entity<Series>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
                entity.Property(e => e.CleanName).HasMaxLength(500);
                entity.Property(e => e.OriginalUrl).HasMaxLength(1000);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
                entity.Property(e => e.Description).HasMaxLength(200);

                // Index für bessere Performance
                entity.HasIndex(e => e.Name);
                entity.HasIndex(e => e.CleanName);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);

                // Enum Conversion
                entity.Property(e => e.Status)
                    .HasConversion<int>();
            });

            // Season Configuration
            modelBuilder.Entity<Season>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Number).IsRequired();

                // Foreign Key zu Series
                entity.HasOne(e => e.Series)
                    .WithMany(s => s.Seasons)
                    .HasForeignKey(e => e.SeriesId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Composite Index für bessere Performance
                entity.HasIndex(e => new { e.SeriesId, e.Number }).IsUnique();
                entity.HasIndex(e => e.CreatedAt);
            });

            // Episode Configuration
            modelBuilder.Entity<Episode>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Number).IsRequired();
                entity.Property(e => e.Title).HasMaxLength(500);
                entity.Property(e => e.OriginalUrl).HasMaxLength(1000);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

                // Foreign Key zu Season
                entity.HasOne(e => e.Season)
                    .WithMany(s => s.Episodes)
                    .HasForeignKey(e => e.SeasonId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Enum Conversion
                entity.Property(e => e.Status)
                    .HasConversion<int>();

                // Composite Index für bessere Performance
                entity.HasIndex(e => new { e.SeasonId, e.Number }).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);
            });

            // DownloadableLink Configuration
            modelBuilder.Entity<DownloadableLink>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Url).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.HostName).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ValidationError).HasMaxLength(500);
                entity.Property(e => e.DownloadPath).HasMaxLength(1000);
                entity.Property(e => e.DownloadError).HasMaxLength(1000);

                // Foreign Key zu Episode
                entity.HasOne(e => e.Episode)
                    .WithMany(ep => ep.Links)
                    .HasForeignKey(e => e.EpisodeId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Enum Conversions
                entity.Property(e => e.Type)
                    .HasConversion<int>();
                entity.Property(e => e.Quality)
                    .HasConversion<int>();
                entity.Property(e => e.DownloadStatus)
                    .HasConversion<int>();

                // Indexes für bessere Performance
                entity.HasIndex(e => e.EpisodeId);
                entity.HasIndex(e => e.HostName);
                entity.HasIndex(e => e.Type);
                entity.HasIndex(e => e.IsValid);
                entity.HasIndex(e => e.DownloadStatus);
                entity.HasIndex(e => e.FoundAt);

                // Unique constraint für URL pro Episode (verhindert Duplikate)
                entity.HasIndex(e => new { e.EpisodeId, e.Url }).IsUnique();
            });
        }

        // Helper Methods für häufige Queries
        public async Task<Series?> GetSeriesByNameAsync(string name)
        {
            return await Series
                .Include(s => s.Seasons)
                    .ThenInclude(season => season.Episodes)
                        .ThenInclude(episode => episode.Links)
                .FirstOrDefaultAsync(s => s.Name == name || s.CleanName == name);
        }

        public async Task<List<Series>> GetAllSeriesWithStatsAsync()
        {
            return await Series
                .Include(s => s.Seasons)
                    .ThenInclude(season => season.Episodes)
                        .ThenInclude(episode => episode.Links)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        public async Task<Episode?> GetEpisodeWithLinksAsync(int seasonId, int episodeNumber)
        {
            return await Episodes
                .Include(e => e.Links)
                .Include(e => e.Season)
                    .ThenInclude(s => s.Series)
                .FirstOrDefaultAsync(e => e.SeasonId == seasonId && e.Number == episodeNumber);
        }

        public async Task<List<DownloadableLink>> GetPendingDownloadsAsync()
        {
            return await Links
                .Include(l => l.Episode)
                    .ThenInclude(e => e.Season)
                        .ThenInclude(s => s.Series)
                .Where(l => l.IsValid && l.DownloadStatus == DownloadStatus.NotStarted)
                .OrderBy(l => l.Episode.Season.Series.Name)
                .ThenBy(l => l.Episode.Season.Number)
                .ThenBy(l => l.Episode.Number)
                .ToListAsync();
        }

        public async Task<List<DownloadableLink>> GetValidLinksForSeriesAsync(string seriesName)
        {
            return await Links
                .Include(l => l.Episode)
                    .ThenInclude(e => e.Season)
                        .ThenInclude(s => s.Series)
                .Where(l => l.IsValid &&
                           (l.Episode.Season.Series.Name == seriesName ||
                            l.Episode.Season.Series.CleanName == seriesName))
                .OrderBy(l => l.Episode.Season.Number)
                .ThenBy(l => l.Episode.Number)
                .ToListAsync();
        }

        // Bulk Operations für bessere Performance
        public async Task BulkInsertLinksAsync(IEnumerable<DownloadableLink> links)
        {
            await Links.AddRangeAsync(links);
            await SaveChangesAsync();
        }

        public async Task UpdateLinkStatusBulkAsync(IEnumerable<int> linkIds, DownloadStatus status)
        {
            var links = await Links.Where(l => linkIds.Contains(l.Id)).ToListAsync();
            foreach (var link in links)
            {
                link.UpdateDownloadStatus(status);
            }
            await SaveChangesAsync();
        }

        // Cleanup Operations
        public async Task<int> CleanupInvalidLinksAsync(DateTime olderThan)
        {
            var invalidLinks = await Links
                .Where(l => !l.IsValid && l.LastValidated < olderThan)
                .ToListAsync();

            Links.RemoveRange(invalidLinks);
            return await SaveChangesAsync();
        }

        public async Task<int> CleanupOldSeriesAsync(DateTime olderThan)
        {
            var oldSeries = await Series
                .Where(s => s.CreatedAt < olderThan && s.Status == MediaStatus.Failed)
                .ToListAsync();

            Series.RemoveRange(oldSeries);
            return await SaveChangesAsync();
        }

        // Statistiken
        public async Task<DatabaseStats> GetStatisticsAsync()
        {
            var stats = new DatabaseStats
            {
                TotalSeries = await Series.CountAsync(),
                TotalSeasons = await Seasons.CountAsync(),
                TotalEpisodes = await Episodes.CountAsync(),
                TotalLinks = await Links.CountAsync(),
                ValidLinks = await Links.CountAsync(l => l.IsValid),
                CompletedDownloads = await Links.CountAsync(l => l.DownloadStatus == DownloadStatus.Completed),
                PendingDownloads = await Links.CountAsync(l => l.DownloadStatus == DownloadStatus.NotStarted && l.IsValid),
                FailedDownloads = await Links.CountAsync(l => l.DownloadStatus == DownloadStatus.Failed)
            };

            return stats;
        }
    }
}