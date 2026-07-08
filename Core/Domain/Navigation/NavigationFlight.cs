using System;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>單次 edge 執行的生命週期識別；防止編排層與執行層重複宣告 waypoint 完成。</summary>
    public sealed class NavigationFlight
    {
        public NavigationFlight(
            Guid token,
            int waypointIndex,
            string fromNodeId,
            string toNodeId,
            NavigationActionType actionType)
        {
            Token = token;
            WaypointIndex = waypointIndex;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            ActionType = actionType;
        }

        public Guid Token { get; }
        public int WaypointIndex { get; }
        public string FromNodeId { get; }
        public string ToNodeId { get; }
        public NavigationActionType ActionType { get; }

        public string RescueKey(string ultimateTargetNodeId) =>
            $"{FromNodeId}->{ToNodeId}|{ActionType}|{ultimateTargetNodeId}";
    }
}
