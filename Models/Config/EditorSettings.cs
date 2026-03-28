using System;

namespace ArtaleAI.Models.Config
{
    public class EditorSettings
    {
        public double DeletionRadius { get; set; } = 10.0;
        public int WaypointCircleRadius { get; set; } = 4;
        public string WaypointColor { get; set; } = "Cyan";
    }
}
