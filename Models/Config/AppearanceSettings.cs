using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Minimap;

namespace ArtaleAI.Models.Config
{
    public class AppearanceSettings
    {
        public MonsterStyle Monster { get; set; } = new();
        public PartyRedBarStyle PartyRedBar { get; set; } = new();
        public DetectionBoxStyle DetectionBox { get; set; } = new();
        public AttackRangeStyle AttackRange { get; set; } = new();
        public MinimapStyle Minimap { get; set; } = new();
        public MinimapPlayerStyle MinimapPlayer { get; set; } = new();
        public MinimapOtherPlayerStyle MinimapOtherPlayer { get; set; } = new();
        public PlayerVitalsStyle PlayerVitals { get; set; } = new();
    }
}
