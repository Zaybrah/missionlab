using System.Collections.Generic;

namespace MissionLab.Core
{
    /// <summary>
    /// The navmesh as scatter's ground (ML-023): snapping a point onto its surface, and which connected part a point stands
    /// on. Pure, so the plugin and the tests share it.
    /// </summary>
    public sealed class NavWalk : IWalk
    {
        private readonly NavMesh _nav;
        private int[] _islands;                        // per triangle, the number of its connected part; worked out on first use

        public NavWalk(NavMesh nav) { _nav = nav; }

        public bool Snap(V3 p, float maxRise, out V3 onGround)
        {
            onGround = p;
            if (_nav == null || _nav.TriAt(p.X, p.Z, p.Y, maxRise, out float height) < 0) return false;
            onGround = new V3(p.X, height, p.Z);
            return true;
        }

        public int Island(V3 p)
        {
            if (_nav == null) return -1;
            int tri = _nav.TriAt(p.X, p.Z, p.Y, Scatter.SameFloor, out _);
            if (tri < 0) return -1;
            if (_islands == null) _islands = Label(_nav);
            return _islands[tri];
        }

        // One flood fill over the triangle neighbours per connected part: every triangle is visited once.
        private static int[] Label(NavMesh nav)
        {
            var label = new int[nav.Count];
            for (int i = 0; i < label.Length; i++) label[i] = -1;
            var stack = new Stack<int>();
            int next = 0;
            for (int start = 0; start < nav.Count; start++)
            {
                if (label[start] >= 0) continue;
                label[start] = next; stack.Push(start);
                while (stack.Count > 0)
                {
                    List<int> near = nav.Neighbours[stack.Pop()];
                    for (int k = 0; k < near.Count; k++) if (label[near[k]] < 0) { label[near[k]] = next; stack.Push(near[k]); }
                }
                next++;
            }
            return label;
        }
    }
}
