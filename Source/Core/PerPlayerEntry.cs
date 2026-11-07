using System;

namespace DynamicJD.Core
{
    public sealed class PerPlayerEntry
    {
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public BaseJdConfig Base { get; set; }
        public int SampleCount { get; set; }
        public float Confidence { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;

        public void EnsureValid()
        {
            if (Base == null) Base = new BaseJdConfig();
            Base.EnsureValid();
            if (string.IsNullOrWhiteSpace(PlayerName))
            {
                PlayerName = string.IsNullOrWhiteSpace(PlayerId) ? "Unknown player" : PlayerId;
            }
        }
    }
}
