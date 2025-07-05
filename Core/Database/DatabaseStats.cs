namespace YtDlpExtractor.Core.Database
{
    public class DatabaseStats
    {
        public int TotalSeries { get; set; }
        public int TotalSeasons { get; set; }
        public int TotalEpisodes { get; set; }
        public int TotalLinks { get; set; }
        public int ValidLinks { get; set; }
        public int CompletedDownloads { get; set; }
        public int PendingDownloads { get; set; }
        public int FailedDownloads { get; set; }

        public double ValidLinksPercentage => TotalLinks > 0 ? (double)ValidLinks / TotalLinks * 100 : 0;
        public double CompletionRate => ValidLinks > 0 ? (double)CompletedDownloads / ValidLinks * 100 : 0;
    }
}