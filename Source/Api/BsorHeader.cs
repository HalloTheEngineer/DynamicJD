namespace DynamicJD.Api
{
    public sealed class BsorHeader
    {
        public float JumpDistance { get; set; }
        public string Hash { get; set; }
        public string Difficulty { get; set; }
        public string Mode { get; set; }
        public string Modifiers { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }
}
