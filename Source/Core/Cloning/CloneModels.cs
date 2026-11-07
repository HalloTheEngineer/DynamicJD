using System.Collections.Generic;

namespace DynamicJD.Core.Cloning
{
    public sealed class CloneSample
    {
        public string Hash { get; set; }
        public string SongName { get; set; }
        public string Difficulty { get; set; }
        public float Njs { get; set; }
        public float Bpm { get; set; }
        public float ReplayJumpDistance { get; set; }
        public float OriginalJumpDistance { get; set; }
        public float ReactionTime { get; set; }
        public int TypeMask { get; set; }
        public List<MapArchetype> Archetypes { get; set; } = new List<MapArchetype>();
        public float Stars { get; set; }
        public float Nps { get; set; }
        public float Accuracy { get; set; }
    }


    public sealed class CloneProgress
    {
        public string Message { get; set; } = string.Empty;
        public int Completed { get; set; }
        public int Total { get; set; }
        public int Modified { get; set; }
        public int Errors { get; set; }
    }

    public sealed class CloneResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public JdProfile Profile { get; set; }
        public int ScoresSeen { get; set; }
        public int ReplaysRead { get; set; }
        public int ModdedSamples { get; set; }
        public int SkippedSpeedModifiers { get; set; }
        public float RecommendedTypeStrength { get; set; }
        public string FitSummary { get; set; } = string.Empty;
        public Dictionary<string, int> SampleCounts { get; set; } = new Dictionary<string, int>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
