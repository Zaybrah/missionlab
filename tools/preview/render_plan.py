"""Render C#-generated mission plans on the real walkable surface (verification aid)."""
import json, sys
sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection
from matplotlib.lines import Line2D
import navmesh

SURFACE, INK, MUTED = "#fcfcfb", "#0b0b0b", "#52514e"
BLUE, ORANGE, AQUA = "#2a78d6", "#eb6834", "#1baf7a"
GROUND, GROUND_EDGE = "#e4e3df", "#d2d1cc"

def draw(ax, nav, plan, authored, infils, zoom=None):
    polys = nav.verts[nav.tris][:, :, [0, 2]]
    ax.add_collection(PolyCollection(polys, facecolors=GROUND, edgecolors=GROUND_EDGE, linewidths=0.25, zorder=1))
    if authored:
        ax.plot([p[0] for p in authored], [p[2] for p in authored], ls="none", marker="o", ms=3, color=AQUA, alpha=0.55, zorder=2, markeredgewidth=0)
    ring = dict(markeredgecolor=SURFACE, markeredgewidth=1.2)
    for s in plan["spawns"]:
        if s["patrol"]:
            xs = [p[0] for p in s["patrol"]] + [s["patrol"][0][0]]; zs = [p[2] for p in s["patrol"]] + [s["patrol"][0][2]]
            ax.plot(xs, zs, color=ORANGE, lw=1.8, ls=(0, (4, 3)), zorder=4)
    for role, mk, ms in (("Guard", "s", 6.5), ("Wander", "o", 7), ("Patrol", "D", 6.5)):
        pts = [s for s in plan["spawns"] if s["role"] == role]
        ax.plot([s["pos"][0] for s in pts], [s["pos"][2] for s in pts], ls="none", marker=mk, ms=ms, color=ORANGE, zorder=5, **ring)
    for p in infils:
        ax.plot(p[0], p[2], marker="^", ms=11, color=BLUE, zorder=6, **ring)
    lo, hi = nav.verts.min(0), nav.verts.max(0)
    if zoom: ax.set_xlim(zoom[0], zoom[1]); ax.set_ylim(zoom[2], zoom[3])
    else: ax.set_xlim(lo[0] - 3, hi[0] + 3); ax.set_ylim(lo[2] - 3, hi[2] + 3)
    ax.set_aspect("equal"); ax.set_facecolor(SURFACE)
    for sp in ax.spines.values(): sp.set_visible(False)
    ax.tick_params(length=0, labelsize=7.5, colors=MUTED)
    n = len(plan["spawns"]); ind = sum(1 for s in plan["spawns"] if s["indoor"])
    ax.set_title("Seed %d: %d positions, %d groups, %d indoors" % (plan["seed"], n, plan["groups"], ind), fontsize=9.5, color=INK, loc="left", pad=6)

def main(nav_path, recon_path, plan_dir, out_png, title, seeds, infil_args):
    nav = navmesh.load(nav_path)
    authored = []; infils = [tuple(map(float, a.split(","))) for a in infil_args]
    if recon_path != "-":
        rec = json.load(open(recon_path))
        authored = [s["pos"] for s in rec["raid"]["botSpawnPoints"] if s]
        if not infils:
            for m in rec["infiltration"]["infilMethods"]:
                o = m.get("infilObject")
                if o and abs(o["pos"][0]) + abs(o["pos"][2]) > 0.01: infils.append(o["pos"])
    cols = min(3, len(seeds)); rows = (len(seeds) + cols - 1) // cols
    fig, axes = plt.subplots(rows, cols, figsize=(6.1 * cols, 6.3 * rows + 0.9), facecolor=SURFACE, squeeze=False)
    for ax in axes.flat: ax.set_visible(False)
    for ax, seed in zip(axes.flat, seeds):
        ax.set_visible(True)
        draw(ax, nav, json.load(open("%s/plan_seed%d.json" % (plan_dir, seed))), authored, infils)
    handles = [Line2D([], [], ls="none", marker="s", ms=6.5, color=ORANGE, label="Guard (holds position)"),
               Line2D([], [], ls="none", marker="o", ms=7, color=ORANGE, label="Wanderer"),
               Line2D([], [], ls="none", marker="D", ms=6.5, color=ORANGE, label="Patrol start"),
               Line2D([], [], ls=(0, (4, 3)), lw=1.8, color=ORANGE, label="Patrol route"),
               Line2D([], [], ls="none", marker="o", ms=4, color=AQUA, alpha=0.7, label="Game's own spawn points"),
               Line2D([], [], ls="none", marker="^", ms=9, color=BLUE, label="Insertion point")]
    fig.legend(handles=handles, loc="lower center", ncol=6, frameon=False, fontsize=9, labelcolor=INK)
    fig.suptitle(title, x=0.012, ha="left", fontsize=13, weight="bold", color=INK)
    fig.subplots_adjust(left=0.04, right=0.99, top=1 - 0.75 / (6.3 * rows + 0.9), bottom=0.7 / (6.3 * rows + 0.9), wspace=0.1, hspace=0.12)
    fig.savefig(out_png, dpi=110, facecolor=SURFACE)

if __name__ == "__main__":
    a = sys.argv
    k = a.index("--") if "--" in a else len(a)
    main(a[1], a[2], a[3], a[4], a[5], [int(s) for s in a[6:k]], a[k + 1:])
