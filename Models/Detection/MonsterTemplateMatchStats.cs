namespace ArtaleAI.Models.Detection
{
    /// <summary>單次模板匹配管線的粗篩／精配計時快照。</summary>
    public readonly struct MonsterTemplateMatchStats
    {
        public MonsterTemplateMatchStats(
            int totalTemplates,
            int coarseCandidates,
            int fineTemplates,
            double downscaleMs,
            double coarseScoreMs,
            double fineMs,
            bool usedFullFallback)
        {
            TotalTemplates = totalTemplates;
            CoarseCandidates = coarseCandidates;
            FineTemplates = fineTemplates;
            DownscaleMs = downscaleMs;
            CoarseScoreMs = coarseScoreMs;
            FineMs = fineMs;
            UsedFullFallback = usedFullFallback;
        }

        public int TotalTemplates { get; }
        public int CoarseCandidates { get; }
        public int FineTemplates { get; }
        public double DownscaleMs { get; }
        public double CoarseScoreMs { get; }
        public double FineMs { get; }
        public bool UsedFullFallback { get; }

        public double CoarseMs => DownscaleMs + CoarseScoreMs;
        public double TotalMs => CoarseMs + FineMs;

        public static MonsterTemplateMatchStats Merge(
            MonsterTemplateMatchStats a,
            MonsterTemplateMatchStats b)
        {
            return new MonsterTemplateMatchStats(
                a.TotalTemplates + b.TotalTemplates,
                a.CoarseCandidates + b.CoarseCandidates,
                a.FineTemplates + b.FineTemplates,
                a.DownscaleMs + b.DownscaleMs,
                a.CoarseScoreMs + b.CoarseScoreMs,
                a.FineMs + b.FineMs,
                a.UsedFullFallback || b.UsedFullFallback);
        }
    }
}
