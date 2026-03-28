using System.Collections.Generic;
using System.Linq;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>
    /// 導航路徑 (Navigation Path)
    /// 代表規劃出的完整移動序列，由多個連續的邊組成
    /// </summary>
    public class NavigationPath
    {
        /// <summary>
        /// 組成路徑的邊序列 (按順序)
        /// </summary>
        public List<NavigationEdge> Edges { get; private set; } = new List<NavigationEdge>();

        /// <summary>
        /// 路徑總成本
        /// </summary>
        public float TotalCost => Edges.Sum(e => e.Cost);

        /// <summary>
        /// 起始節點 ID
        /// </summary>
        public string? StartNodeId => Edges.FirstOrDefault()?.FromNodeId;

        /// <summary>
        /// 終點節點 ID
        /// </summary>
        public string? EndNodeId => Edges.LastOrDefault()?.ToNodeId;

        public bool IsEmpty => Edges.Count == 0;

        public NavigationPath() { }

        public NavigationPath(IEnumerable<NavigationEdge> edges)
        {
            Edges.AddRange(edges);
        }

        public void AddStep(NavigationEdge edge)
        {
            Edges.Add(edge);
        }
    }
}
