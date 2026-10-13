"""
Original PC illustration for Coreloom's Overview header (src/Coreloom.App/Views/PcIllustration.xaml), described once
and emitted twice:
  * SVG, for previews, with one hardware variant baked in;
  * XAML, for the app, where the variable parts are bound to SystemSummaryViewModel.

Usage:
  python3 build/pc-illustration.py page src/Coreloom.App/Views/PcIllustration.xaml   # regenerate the control
  python3 build/pc-illustration.py svg preview.svg ram=4 gpu=medium "gpu_label=RX 9060 XT" accent=#3DDC97 temp=61°

Coordinates: a 300 x 300 canvas, y down. The case is drawn in an oblique projection: depth goes right and up.
Everything is original artwork (no logos, no product photos); the accent color comes from the theme at run time.
"""
from __future__ import annotations

import sys
from dataclasses import dataclass, field

# ---------------------------------------------------------------------------------------------------------------
# Paint
# ---------------------------------------------------------------------------------------------------------------

ACCENT = "ACCENT"


def hexa(color: str, alpha: float = 1.0) -> tuple[str, float]:
    return (color, alpha)


@dataclass
class Solid:
    color: str
    alpha: float = 1.0


@dataclass
class Linear:
    x1: float
    y1: float
    x2: float
    y2: float
    stops: list  # (offset, color, alpha); color may be ACCENT


@dataclass
class Radial:
    cx: float
    cy: float
    rx: float
    ry: float
    stops: list


@dataclass
class Dots:
    spacing: float
    r: float
    color: str
    alpha: float
    background: str


Paint = Solid | Linear | Radial | Dots | None


@dataclass
class Shape:
    d: str
    fill: Paint = None
    stroke: Paint = None
    sw: float = 1.0
    opacity: float = 1.0
    cap: str = "round"


@dataclass
class Text:
    x: float          # centre
    baseline: float
    size: float
    weight: int
    color: str
    alpha: float
    sample: str       # preview text
    binding: str | None = None
    width: float = 60
    spacing: float = 0  # letter spacing (em/1000 in XAML terms is not available; preview only)


@dataclass
class Group:
    items: list = field(default_factory=list)
    clip: str | None = None
    visible: str | None = None       # XAML: view-model bool property name ("!Name" = inverted)
    preview_visible: bool = True
    opacity: float = 1.0
    comment: str | None = None

    def add(self, *items):
        self.items.extend(items)
        return self


# ---------------------------------------------------------------------------------------------------------------
# Geometry helpers
# ---------------------------------------------------------------------------------------------------------------

def f(v: float) -> str:
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def poly(*pts) -> str:
    return "M" + " L".join(f"{f(x)},{f(y)}" for x, y in pts) + " Z"


def rect(x, y, w, h, r=0.0) -> str:
    if r <= 0:
        return poly((x, y), (x + w, y), (x + w, y + h), (x, y + h))
    r = min(r, w / 2, h / 2)
    return (f"M{f(x + r)},{f(y)} L{f(x + w - r)},{f(y)} A{f(r)},{f(r)} 0 0 1 {f(x + w)},{f(y + r)} "
            f"L{f(x + w)},{f(y + h - r)} A{f(r)},{f(r)} 0 0 1 {f(x + w - r)},{f(y + h)} "
            f"L{f(x + r)},{f(y + h)} A{f(r)},{f(r)} 0 0 1 {f(x)},{f(y + h - r)} "
            f"L{f(x)},{f(y + r)} A{f(r)},{f(r)} 0 0 1 {f(x + r)},{f(y)} Z")


def ellipse(cx, cy, rx, ry) -> str:
    return (f"M{f(cx - rx)},{f(cy)} A{f(rx)},{f(ry)} 0 1 0 {f(cx + rx)},{f(cy)} "
            f"A{f(rx)},{f(ry)} 0 1 0 {f(cx - rx)},{f(cy)} Z")


def ring(cx, cy, rx, ry, t) -> str:
    """Elliptical ring (outer minus inner) as one even-odd path."""
    return ellipse(cx, cy, rx, ry) + " " + ellipse(cx, cy, rx - t, max(0.5, ry - t * ry / rx))


D = (52.0, -22.0)  # full case depth (screen offset from the glass plane to the back)


def at(x, y, z):
    return (x + D[0] * z, y + D[1] * z)


# ---------------------------------------------------------------------------------------------------------------
# The scene
# ---------------------------------------------------------------------------------------------------------------

FRONT = (40.0, 44.0, 222.0, 270.0)            # outer front face x0, y0, x1, y1
GLASS = (45.0, 49.0, 217.0, 262.0)            # inner glass opening


def build(variant: dict) -> Group:
    x0, y0, x1, y1 = FRONT
    gx0, gy0, gx1, gy1 = GLASS
    root = Group(comment="PC illustration")

    # Ground shadow
    root.add(Shape(ellipse(158, 279, 132, 11), Radial(0.5, 0.5, 0.5, 0.5, [(0, "#000000", 0.55), (1, "#000000", 0)])))

    # Feet
    for fx in (54, 196):
        root.add(Shape(rect(fx, 268, 14, 9, 2), Linear(0, 0, 0, 1, [(0, "#1A1D24", 1), (1, "#07080B", 1)])))
    root.add(Shape(poly((257, 250), (268, 245), (268, 253), (259, 257)), Solid("#090A0E")))

    # Right side panel (mesh) and top panel
    right = poly((x1, y0), (x1 + D[0], y0 + D[1]), (x1 + D[0], y1 + D[1]), (x1, y1))
    top = poly((x0, y0), (x0 + D[0], y0 + D[1]), (x1 + D[0], y0 + D[1]), (x1, y0))
    root.add(Shape(right, Linear(0, 0, 1, 0, [(0, "#151820", 1), (1, "#0C0E13", 1)])))
    root.add(Shape(right, Dots(3.2, 0.62, "#000000", 0.55, "")))
    root.add(Shape(right, Linear(0, 0, 0, 1, [(0, "#FFFFFF", 0.05), (0.5, "#FFFFFF", 0), (1, "#000000", 0.25)])))
    root.add(Shape(top, Linear(0, 0, 0, 1, [(0, "#1D212B", 1), (1, "#12151C", 1)])))
    root.add(Shape(top, Dots(3.2, 0.62, "#000000", 0.6, "")))
    # light from the top fans leaking through the top mesh
    root.add(Shape(top, Radial(0.45, 0.85, 0.55, 0.9, [(0, ACCENT, 0.32), (1, ACCENT, 0)])))
    # edges
    root.add(Shape(f"M{f(x1)},{f(y0)} L{f(x1 + D[0])},{f(y0 + D[1])}", stroke=Solid("#FFFFFF", 0.10), sw=0.8))
    root.add(Shape(f"M{f(x0)},{f(y0)} L{f(x0 + D[0])},{f(y0 + D[1])} L{f(x1 + D[0])},{f(y0 + D[1])}", stroke=Solid("#FFFFFF", 0.07), sw=0.8))

    # Front frame
    root.add(Shape(rect(x0, y0, x1 - x0, y1 - y0, 3), Linear(0, 0, 0, 1, [(0, "#1B1F28", 1), (1, "#0A0C10", 1)])))

    # ---- Interior (clipped to the glass) ---------------------------------------------------------------
    inner = Group(clip=rect(gx0, gy0, gx1 - gx0, gy1 - gy0, 1.5), comment="interior")
    root.add(inner)
    zb = 0.86  # back wall depth
    bx0, by0 = at(gx0, gy0, zb)
    bx1, by1 = at(gx1, gy1, zb)
    # back wall
    inner.add(Shape(rect(gx0 - 5, gy0 - 30, gx1 - gx0 + 60, gy1 - gy0 + 40), Solid("#0D1017")))
    inner.add(Shape(poly((bx0, by0), (bx1 + 20, by0), (bx1 + 20, by1), (bx0, by1)),
                    Linear(0, 0, 0, 1, [(0, "#181D2A", 1), (1, "#0D1018", 1)])))
    # ambient accent glow on the back wall
    inner.add(Shape(rect(70, 40, 170, 200), Radial(0.55, 0.45, 0.55, 0.5, [(0, ACCENT, 0.20), (0.6, ACCENT, 0.06), (1, ACCENT, 0)])))
    # left inner wall
    inner.add(Shape(poly((gx0, gy0 - 40), (bx0, by0 - 40), (bx0, by1), (gx0, gy1)),
                    Linear(0, 0, 1, 0, [(0, "#161A24", 1), (1, "#0F121A", 1)])))
    inner.add(Shape(poly((gx0, gy0 - 40), (bx0, by0 - 40), (bx0, by1), (gx0, gy1)),
                    Linear(0, 0, 0, 1, [(0, ACCENT, 0.06), (0.5, ACCENT, 0.0), (1, ACCENT, 0.10)])))
    # floor
    inner.add(Shape(poly((gx0, gy1), (bx0, by1), (bx1 + 20, by1), (gx1 + 20, gy1)),
                    Linear(0, 0, 0, 1, [(0, "#0B0D13", 1), (1, "#151924", 1)])))

    # Motherboard on the back wall
    mbx, mby, mbw, mbh = 108, 54, 104, 150
    inner.add(Shape(rect(mbx, mby, mbw, mbh, 3), Linear(0, 0, 1, 1, [(0, "#343B4D", 1), (0.6, "#242936", 1), (1, "#1D212C", 1)])))
    inner.add(Shape(rect(mbx, mby, mbw, mbh, 3), stroke=Solid("#FFFFFF", 0.09), sw=0.7))
    # traces / texture
    for i, (tx, ty, tw) in enumerate([(116, 132, 40), (116, 137, 30), (160, 190, 40), (118, 188, 26)]):
        inner.add(Shape(f"M{tx},{ty} L{tx + tw},{ty}", stroke=Solid("#FFFFFF", 0.05), sw=0.7))
    # VRM heatsinks (L shape around the socket)
    inner.add(Shape(rect(112, 58, 52, 13, 2), Linear(0, 0, 0, 1, [(0, "#4A5160", 1), (1, "#2A2E38", 1)])))
    inner.add(Shape(rect(112, 58, 12, 52, 2), Linear(0, 0, 1, 0, [(0, "#464D5B", 1), (1, "#272B34", 1)])))
    for k in range(5):
        inner.add(Shape(f"M{115 + k * 9},{61} L{115 + k * 9},{68}", stroke=Solid("#000000", 0.35), sw=1.2))
    # rear I/O cover
    inner.add(Shape(rect(196, 58, 13, 46, 2), Linear(0, 0, 1, 0, [(0, "#2B303B", 1), (1, "#1A1D24", 1)])))
    # chipset heatsink with accent line
    inner.add(Shape(rect(168, 164, 38, 30, 3), Linear(0, 0, 1, 1, [(0, "#30353F", 1), (1, "#1B1E25", 1)])))
    inner.add(Shape(f"M172,185 L202,173", stroke=Solid(ACCENT, 0.85), sw=1.4))
    for sy in (176, 198):
        inner.add(Shape(rect(118, sy, 46, 2.2, 1), Solid("#0E1015")))
    # M.2 cover
    inner.add(Shape(rect(122, 182, 40, 8, 2), Linear(0, 0, 0, 1, [(0, "#2C313B", 1), (1, "#1C1F26", 1)])))

    # RAM sticks (2 or 4)
    ram = Group(comment="memory")
    inner.add(ram)
    for i, (rx_, needs_four) in enumerate([(174, False), (181, True), (188, False), (195, True)]):
        stick = Group(visible="FourMemorySticks" if needs_four else None,
                      preview_visible=(not needs_four) or variant["ram"] == 4)
        stick.add(Shape(rect(rx_ - 5, 62, 15.4, 30), Radial(0.5, 0.4, 0.5, 0.5, [(0, ACCENT, 0.55), (1, ACCENT, 0)])))
        stick.add(Shape(rect(rx_ - 0.6, 116, 6.6, 6, 0.6), Solid("#101217")))
        stick.add(Shape(rect(rx_, 74, 5.4, 44, 1), Linear(0, 0, 1, 0, [(0, "#3F4552", 1), (0.5, "#2A2E38", 1), (1, "#171A20", 1)])))
        stick.add(Shape(rect(rx_, 74, 5.4, 12, 1.2), Linear(0, 0, 0, 1, [(0, "#FFFFFF", 1), (0.35, ACCENT, 1), (1, ACCENT, 0.7)])))
        ram.add(stick)

    # Radiator + top fans (under the ceiling, front to back)
    rad = Group(comment="radiator and top fans")
    inner.add(rad)
    rad.add(Shape(poly(at(58, 52, 0.18), at(206, 52, 0.18), at(206, 52, 0.82), at(58, 52, 0.82)), Solid("#08090D")))
    rad.add(Shape(rect(58 + D[0] * 0.18, 52 + D[1] * 0.18, 148, 7), Linear(0, 0, 0, 1, [(0, "#22262F", 1), (1, "#101218", 1)])))
    for cx in (80, 122, 164):
        c = at(cx, 72, 0.5)
        rad.add(Shape(ellipse(c[0], c[1], 29, 12), Radial(0.5, 0.5, 0.5, 0.5, [(0, ACCENT, 0.45), (1, ACCENT, 0)])))
        rad.add(Shape(ellipse(c[0], c[1] - 1, 19, 6.2), Solid("#06070A")))
        rad.add(Shape(ring(c[0], c[1] - 1, 19, 6.2, 1.8), Linear(0, 0, 1, 0, [(0, ACCENT, 0.7), (0.5, "#FFFFFF", 0.95), (1, ACCENT, 0.7)])))
        rad.add(Shape(ellipse(c[0], c[1] - 1, 5.5, 2), Solid("#1E222B")))
        for a, b in ((-12, -3), (4, 2.5), (11, -2)):
            rad.add(Shape(f"M{f(c[0])},{f(c[1] - 1)} Q{f(c[0] + a * 0.6)},{f(c[1] - 1 + b)} {f(c[0] + a)},{f(c[1] - 1 + b * 0.6)}",
                          stroke=Solid("#FFFFFF", 0.10), sw=2.2))

    # AIO tubes (pump to radiator)
    pc = (147, 108)
    for dx in (0, 6.5):
        tube = (f"M{f(pc[0] - 6 + dx)},{f(pc[1] - 14)} C{f(pc[0] - 8 + dx)},{f(pc[1] - 30)} {f(pc[0] - 18 + dx)},{f(pc[1] - 34)} {f(pc[0] - 20 + dx)},{f(63)}")
        rad.add(Shape(tube, stroke=Solid("#0B0C10", 1), sw=4.4))
        rad.add(Shape(tube, stroke=Solid("#FFFFFF", 0.12), sw=1.0))

    # AIO pump with a round screen (live temperature)
    pump = Group(comment="pump")
    inner.add(pump)
    pump.add(Shape(ellipse(pc[0], pc[1], 26, 26), Radial(0.5, 0.5, 0.5, 0.5, [(0, ACCENT, 0.42), (0.55, ACCENT, 0.14), (1, ACCENT, 0)])))
    pump.add(Shape(ellipse(pc[0] - 2.5, pc[1] + 1.2, 17.5, 17.5), Solid("#0A0B0F")))
    pump.add(Shape(ring(pc[0], pc[1], 17.5, 17.5, 2.6), Linear(0, 0, 1, 1, [(0, "#FFFFFF", 0.95), (0.35, ACCENT, 1), (1, ACCENT, 0.6)])))
    pump.add(Shape(ellipse(pc[0], pc[1], 14.6, 14.6), Radial(0.4, 0.35, 0.7, 0.7, [(0, "#1A2030", 1), (1, "#06070B", 1)])))
    pump.add(Text(pc[0], pc[1] + 2.6, 10.5, 600, "#FFFFFF", 0.96, variant["temp"], "ScreenValue", width=28))
    pump.add(Text(pc[0], pc[1] + 9.2, 5.0, 600, "#FFFFFF", 0.6, variant["temp_label"], "ScreenLabel", width=24))
    pump.add(Shape(f"M{f(pc[0] - 9)},{f(pc[1] - 8.5)} A13,13 0 0 1 {f(pc[0] + 6)},{f(pc[1] - 12.5)}", stroke=Solid("#FFFFFF", 0.18), sw=1.0))

    # Graphics card (front face towards the glass, backplate on top, end cap on the left)
    for size, length in (("large", 132), ("medium", 104)):
        gpu = Group(visible="HasLargeGpu" if size == "large" else "HasMediumGpu",
                    preview_visible=variant["gpu"] == size, comment=f"{size} graphics card")
        inner.add(gpu)
        zf, zbk = 0.22, 0.78
        right_x = 196
        left_x = right_x - length
        top_y, h = 146, 26
        fx0, fy0 = at(left_x, top_y, zf)
        fx1 = fx0 + length
        bx0_, by0_ = at(left_x, top_y, zbk)
        # glow under the card
        gpu.add(Shape(rect(fx0 - 10, fy0 + h - 6, length + 40, 30), Radial(0.5, 0.25, 0.55, 0.6, [(0, ACCENT, 0.30), (1, ACCENT, 0)])))
        # backplate (top face)
        gpu.add(Shape(poly((fx0, fy0), (fx1, fy0), (bx0_ + length, by0_), (bx0_, by0_)), Linear(0, 1, 0, 0, [(0, "#3A404C", 1), (1, "#232730", 1)])))
        gpu.add(Shape(f"M{f(fx0 + 3)},{f(fy0 - 0.6)} L{f(fx1 - 3)},{f(fy0 - 0.6)}", stroke=Solid("#FFFFFF", 0.22), sw=0.7))
        # power connector + cable
        pcx = fx0 + length * 0.62
        gpu.add(Shape(rect(pcx, fy0 - 6.5, 9, 4.5, 1), Solid("#0C0D11")))
        gpu.add(Shape(f"M{f(pcx + 4.5)},{f(fy0 - 6)} C{f(pcx + 5)},{f(fy0 - 18)} {f(pcx + 20)},{f(fy0 - 22)} {f(pcx + 28)},{f(fy0 - 26)}",
                      stroke=Solid("#0A0B0F", 1), sw=3.6))
        # end cap (left face)
        gpu.add(Shape(poly((fx0, fy0), (bx0_, by0_), (bx0_, by0_ + h), (fx0, fy0 + h)), Linear(0, 0, 1, 0, [(0, "#1E222A", 1), (1, "#121419", 1)])))
        # front face (shroud)
        gpu.add(Shape(rect(fx0, fy0, length, h, 1.2), Linear(0, 0, 0, 1, [(0, "#353A45", 1), (0.45, "#20242C", 1), (1, "#121419", 1)])))
        gpu.add(Shape(rect(fx0 + 0.5, fy0 + 0.5, length - 1, h - 1, 1), stroke=Solid("#FFFFFF", 0.08), sw=0.6))
        # vents on the right third
        for k in range(6):
            vx = fx0 + length - 30 + k * 4.4
            gpu.add(Shape(f"M{f(vx)},{f(fy0 + 7)} L{f(vx - 2)},{f(fy0 + h - 7)}", stroke=Solid("#000000", 0.45), sw=1.4))
        # light bar
        gpu.add(Shape(rect(fx0 + 6, fy0 + h - 4.2, length * 0.55, 1.6, 0.8), Linear(0, 0, 1, 0, [(0, ACCENT, 0.0), (0.2, ACCENT, 1), (0.8, "#FFFFFF", 0.9), (1, ACCENT, 0.0)])))
        # model label
        gpu.add(Text(fx0 + 8 + 30, fy0 + 15.6, 7.6, 700, "#FFFFFF", 0.92, variant["gpu_label"], "GpuLabel", width=60))
        # I/O bracket at the rear end
        gpu.add(Shape(rect(fx1 - 1, fy0 - 2, 3.4, h + 6, 0.8), Linear(0, 0, 0, 1, [(0, "#4A505C", 1), (1, "#2A2E36", 1)])))

    # Bottom fans on the floor
    for cx in (72, 116, 160):
        c = at(cx, 254, 0.45)
        inner.add(Shape(ellipse(c[0], c[1], 30, 11), Radial(0.5, 0.5, 0.5, 0.5, [(0, ACCENT, 0.50), (1, ACCENT, 0)])))
        inner.add(Shape(ellipse(c[0], c[1], 19, 6.4), Solid("#07080B")))
        inner.add(Shape(ring(c[0], c[1], 19, 6.4, 1.8), Linear(0, 0, 1, 0, [(0, ACCENT, 0.75), (0.5, "#FFFFFF", 0.9), (1, ACCENT, 0.75)])))
        inner.add(Shape(ellipse(c[0], c[1], 5.5, 2), Solid("#1E222B")))
        for a, b in ((-12, 2.5), (5, -2.6), (11, 2)):
            inner.add(Shape(f"M{f(c[0])},{f(c[1])} Q{f(c[0] + a * 0.6)},{f(c[1] + b)} {f(c[0] + a)},{f(c[1] + b * 0.6)}",
                            stroke=Solid("#FFFFFF", 0.10), sw=2.2))

    # ---- Glass ---------------------------------------------------------------------------------------------
    glass = rect(gx0, gy0, gx1 - gx0, gy1 - gy0, 1.5)
    root.add(Shape(glass, Linear(0, 0, 1, 1, [(0, "#9FB6FF", 0.10), (0.5, "#FFFFFF", 0.02), (1, "#9FB6FF", 0.06)])))
    root.add(Group(clip=glass, comment="reflections").add(
        Shape(poly((60, 49), (92, 49), (40, 262), (8, 262)), Linear(0, 0, 1, 0, [(0, "#FFFFFF", 0), (0.5, "#FFFFFF", 0.07), (1, "#FFFFFF", 0)])),
        Shape(poly((112, 49), (122, 49), (70, 262), (60, 262)), Solid("#FFFFFF", 0.035)),
        Shape(rect(gx0, gy0, 5, gy1 - gy0), Linear(0, 0, 1, 0, [(0, "#FFFFFF", 0.22), (1, "#FFFFFF", 0)])),
    ))
    root.add(Shape(glass, stroke=Solid("#FFFFFF", 0.10), sw=0.8))
    # bottom bar of the frame
    root.add(Shape(rect(x0, gy1, x1 - x0, y1 - gy1, 0), Linear(0, 0, 0, 1, [(0, "#14171E", 1), (1, "#0A0B0F", 1)])))
    root.add(Shape(f"M{f(x0 + 3)},{f(y0 + 0.5)} L{f(x1 - 3)},{f(y0 + 0.5)}", stroke=Solid("#FFFFFF", 0.16), sw=0.8))
    return root


# ---------------------------------------------------------------------------------------------------------------
# SVG
# ---------------------------------------------------------------------------------------------------------------

class SvgWriter:
    def __init__(self, accent: str):
        self.accent = accent
        self.defs: list[str] = []
        self.n = 0

    def color(self, c):
        return self.accent if c == ACCENT else c

    def paint(self, p: Paint) -> str:
        if p is None:
            return "none"
        if isinstance(p, Solid):
            return self.color(p.color) + (f'" fill-opacity="{p.alpha}' if p.alpha < 1 else "")
        self.n += 1
        pid = f"p{self.n}"
        if isinstance(p, Linear):
            stops = "".join(f'<stop offset="{o}" stop-color="{self.color(c)}" stop-opacity="{a}"/>' for o, c, a in p.stops)
            self.defs.append(f'<linearGradient id="{pid}" x1="{p.x1}" y1="{p.y1}" x2="{p.x2}" y2="{p.y2}">{stops}</linearGradient>')
        elif isinstance(p, Radial):
            stops = "".join(f'<stop offset="{o}" stop-color="{self.color(c)}" stop-opacity="{a}"/>' for o, c, a in p.stops)
            # SVG radial gradients are circular in bbox units; scale for rx != ry.
            self.defs.append(f'<radialGradient id="{pid}" cx="{p.cx}" cy="{p.cy}" r="{p.rx}" gradientTransform="translate({p.cx} {p.cy}) scale(1 {p.ry / p.rx}) translate({-p.cx} {-p.cy})">{stops}</radialGradient>')
        elif isinstance(p, Dots):
            self.defs.append(f'<pattern id="{pid}" width="{p.spacing}" height="{p.spacing}" patternUnits="userSpaceOnUse">'
                             f'<circle cx="{p.spacing / 2}" cy="{p.spacing / 2}" r="{p.r}" fill="{p.color}" fill-opacity="{p.alpha}"/></pattern>')
        return f"url(#{pid})"

    def item(self, it, out: list[str]):
        if isinstance(it, Group):
            if not it.preview_visible:
                return
            attrs = ""
            if it.clip:
                self.n += 1
                cid = f"c{self.n}"
                self.defs.append(f'<clipPath id="{cid}"><path d="{it.clip}"/></clipPath>')
                attrs += f' clip-path="url(#{cid})"'
            out.append(f"<g{attrs}>")
            for child in it.items:
                self.item(child, out)
            out.append("</g>")
        elif isinstance(it, Shape):
            fill = self.paint(it.fill)
            stroke = self.paint(it.stroke) if it.stroke else "none"
            stroke = stroke.replace("fill-opacity", "stroke-opacity")
            out.append(f'<path d="{it.d}" fill="{fill}" fill-rule="evenodd" stroke="{stroke}" stroke-width="{it.sw}" stroke-linecap="{it.cap}"'
                       + (f' opacity="{it.opacity}"' if it.opacity < 1 else "") + "/>")
        elif isinstance(it, Text):
            out.append(f'<text x="{it.x}" y="{it.baseline}" font-family="Inter" font-size="{it.size}" font-weight="{it.weight}" '
                       f'fill="{it.color}" fill-opacity="{it.alpha}" text-anchor="middle" letter-spacing="0.2">{it.sample}</text>')

    def write(self, root: Group, size=300) -> str:
        body: list[str] = []
        self.item(root, body)
        return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}" width="{size}" height="{size}">'
                f'<defs>{"".join(self.defs)}</defs>{"".join(body)}</svg>')


# ---------------------------------------------------------------------------------------------------------------
# XAML
# ---------------------------------------------------------------------------------------------------------------

class XamlWriter:
    """Static artwork goes into one canvas (cached as a bitmap in the app); bound text goes into an overlay."""

    def __init__(self):
        self.lines: list[str] = []
        self.overlay: list[str] = []
        self.vis_stack: list[str] = []

    @staticmethod
    def argb(color: str, alpha: float) -> str:
        a = max(0, min(255, round(alpha * 255)))
        return f"#{a:02X}{color.lstrip('#').upper()}"

    @staticmethod
    def gradient_tags(p):
        if isinstance(p, Linear):
            return (f'<LinearGradientBrush StartPoint="{f(p.x1)},{f(p.y1)}" EndPoint="{f(p.x2)},{f(p.y2)}">', "</LinearGradientBrush>")
        return (f'<RadialGradientBrush Center="{f(p.cx)},{f(p.cy)}" GradientOrigin="{f(p.cx)},{f(p.cy)}" RadiusX="{f(p.rx)}" RadiusY="{f(p.ry)}">',
                "</RadialGradientBrush>")

    def gradient(self, p, prop, ind, stops) -> list[str]:
        open_tag, close_tag = self.gradient_tags(p)
        lines = [f"{ind}<Path.{prop}>", f"{ind}  {open_tag}"]
        lines += [f'{ind}    <GradientStop Offset="{f(o)}" Color="{col}" />' for o, col in stops]
        lines += [f"{ind}  {close_tag}", f"{ind}</Path.{prop}>"]
        return lines

    def dots(self, p: Dots, ind: str) -> list[str]:
        s = p.spacing
        return [f"{ind}<Path.Fill>",
                f'{ind}  <DrawingBrush TileMode="Tile" Viewport="0,0,{f(s)},{f(s)}" ViewportUnits="Absolute" Viewbox="0,0,{f(s)},{f(s)}" ViewboxUnits="Absolute">',
                f"{ind}    <DrawingBrush.Drawing>",
                f"{ind}      <DrawingGroup>",
                f'{ind}        <GeometryDrawing Brush="#00000000" Geometry="M0,0 L{f(s)},0 {f(s)},{f(s)} 0,{f(s)} Z" />',
                f'{ind}        <GeometryDrawing Brush="{self.argb(p.color, p.alpha)}">',
                f'{ind}          <GeometryDrawing.Geometry>',
                f'{ind}            <EllipseGeometry Center="{f(s / 2)},{f(s / 2)}" RadiusX="{f(p.r)}" RadiusY="{f(p.r)}" />',
                f'{ind}          </GeometryDrawing.Geometry>',
                f'{ind}        </GeometryDrawing>',
                f"{ind}      </DrawingGroup>",
                f"{ind}    </DrawingBrush.Drawing>",
                f"{ind}  </DrawingBrush>",
                f"{ind}</Path.Fill>"]

    def path(self, d, ind, attrs, children):
        head = f'{ind}<Path Data="F0 {d}"' + "".join(" " + a for a in attrs if a)
        if children:
            self.lines.append(head + ">")
            self.lines.extend(children)
            self.lines.append(f"{ind}</Path>")
        else:
            self.lines.append(head + " />")

    def shape(self, it: Shape, ind: str):
        inner = ind + "  "
        stroke_attrs = []
        if it.stroke is not None:
            st = it.stroke
            assert isinstance(st, Solid)
            stroke_attrs = ['Stroke="{DynamicResource Brush.Accent}"' if st.color == ACCENT else f'Stroke="{self.argb(st.color, st.alpha)}"',
                            f'StrokeThickness="{f(it.sw)}"']
            if it.cap == "round":
                stroke_attrs.append('StrokeStartLineCap="Round" StrokeEndLineCap="Round"')
            if st.color == ACCENT and st.alpha < 1:
                stroke_attrs.append(f'Opacity="{f(st.alpha)}"')
        p = it.fill
        if p is None:
            self.path(it.d, ind, stroke_attrs, [])
        elif isinstance(p, Solid):
            if p.color == ACCENT:
                self.path(it.d, ind, ['Fill="{DynamicResource Brush.Accent}"'] + ([f'Opacity="{f(p.alpha)}"'] if p.alpha < 1 else []) + stroke_attrs, [])
            else:
                self.path(it.d, ind, [f'Fill="{self.argb(p.color, p.alpha)}"'] + stroke_attrs, [])
        elif isinstance(p, Dots):
            self.path(it.d, ind, stroke_attrs, self.dots(p, inner))
        else:
            has_accent = any(c == ACCENT for _, c, _ in p.stops)
            has_other = any(c != ACCENT for _, c, _ in p.stops)
            if not has_accent:
                self.path(it.d, ind, stroke_attrs, self.gradient(p, "Fill", inner, [(o, self.argb(c, a)) for o, c, a in p.stops]))
                return
            # Accent light follows the user's accent color: the accent brush shaped by an opacity mask.
            mask = [(o, self.argb("#000000", a)) for o, c, a in p.stops]
            self.path(it.d, ind, ['Fill="{DynamicResource Brush.Accent}"'] + stroke_attrs, self.gradient(p, "OpacityMask", inner, mask))
            if has_other:
                # white highlights on top of the accent light
                over = [(o, self.argb(c, a) if c != ACCENT else self.argb("#FFFFFF", 0)) for o, c, a in p.stops]
                self.path(it.d, ind, [], self.gradient(p, "Fill", inner, over))

    def text(self, it: Text):
        top = it.baseline - it.size * 0.96
        weight = {400: "Normal", 500: "Medium", 600: "SemiBold", 700: "Bold"}[it.weight]
        value = f"{{Binding {it.binding}}}" if it.binding else it.sample
        line = (f'<TextBlock Canvas.Left="{f(it.x - it.width / 2)}" Canvas.Top="{f(top)}" Width="{f(it.width)}" TextAlignment="Center" '
                f'Text="{value}" FontSize="{f(it.size)}" FontWeight="{weight}" Foreground="{self.argb(it.color, it.alpha)}" />')
        if self.vis_stack:
            name = self.vis_stack[-1]
            inv = name.startswith("!")
            conv = ", ConverterParameter=Invert" if inv else ""
            line = (f'<Canvas Visibility="{{Binding {name.lstrip("!")}, Converter={{StaticResource BoolToVisibility}}{conv}}}">' + line + "</Canvas>")
        self.overlay.append(line)

    def item(self, it, ind: str):
        if isinstance(it, Group):
            attrs = []
            if it.visible:
                inv = it.visible.startswith("!")
                name = it.visible.lstrip("!")
                attrs.append(f'Visibility="{{Binding {name}, Converter={{StaticResource BoolToVisibility}}' + (", ConverterParameter=Invert" if inv else "") + '}"')
                self.vis_stack.append(it.visible)
            if it.clip:
                attrs.append(f'Clip="{it.clip}"')
            if it.comment:
                self.lines.append(f"{ind}<!-- {it.comment} -->")
            self.lines.append(f"{ind}<Canvas{(' ' + ' '.join(attrs)) if attrs else ''}>")
            for child in it.items:
                self.item(child, ind + "  ")
            self.lines.append(f"{ind}</Canvas>")
            if it.visible:
                self.vis_stack.pop()
        elif isinstance(it, Shape):
            self.shape(it, ind)
        elif isinstance(it, Text):
            self.text(it)

    def write(self, root: Group, ind: str) -> tuple[str, str]:
        self.item(root, ind)
        return "\n".join(self.lines), "\n".join(ind + l for l in self.overlay)


# The control around the generated canvases. Edit here (not in the .xaml) and regenerate.
PAGE = r"""<UserControl x:Class="Coreloom.App.Views.PcIllustration"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             AutomationProperties.Name="Illustration of this PC"
             Focusable="False">

  <!--
    An original illustration of a PC that follows the detected hardware (bound to SystemSummaryViewModel): two or four
    memory sticks, a large or medium graphics card (or none) with its model name, the CPU or GPU temperature on the
    pump's screen, and lighting in the user's accent color. Generated by build/pc-illustration.py (see
    docs/ARCHITECTURE.md): edit the script and regenerate rather than editing the paths by hand.

    A render of the real build can replace it: pc.png in %AppData%\Coreloom (or Data next to a portable copy) or in
    assets/pc/ at build time. See assets/pc/README.md.
  -->
  <Grid>
    <Viewbox x:Name="Art" Stretch="Uniform">
      <Grid Width="300" Height="300">
        <!-- Static artwork, rasterised once (twice the canvas size: sharp up to about 280 % display scaling at 210 px) -->
        <Canvas>
          <Canvas.CacheMode>
            <BitmapCache RenderAtScale="2" />
          </Canvas.CacheMode>
{base}
        </Canvas>
        <!-- Live text (pump screen, card name) -->
        <Canvas TextOptions.TextFormattingMode="Ideal" TextOptions.TextRenderingMode="Grayscale" IsHitTestVisible="False">
{overlay}
        </Canvas>
      </Grid>
    </Viewbox>
    <Image x:Name="CustomImage" Stretch="Uniform" Visibility="Collapsed" RenderOptions.BitmapScalingMode="HighQuality" />
  </Grid>
</UserControl>
"""


def main():
    mode = sys.argv[1]
    variant = {"ram": 2, "gpu": "large", "temp": "54°", "temp_label": "CPU", "gpu_label": "RTX 5090"}
    accent = "#5B8CFF"
    for arg in sys.argv[3:]:
        k, v = arg.split("=", 1)
        if k == "accent":
            accent = v
        else:
            variant[k] = int(v) if v.isdigit() else v
    root = build(variant)
    out = sys.argv[2]
    if mode == "svg":
        with open(out, "w", encoding="utf-8", newline="\n") as file:
            file.write(SvgWriter(accent).write(root))
    elif mode == "page":
        base, overlay = XamlWriter().write(root, "          ")
        with open(out, "w", encoding="utf-8", newline="\n") as file:
            file.write(PAGE.format(base=base, overlay=overlay))
    else:
        raise SystemExit(f"unknown mode {mode!r}: use 'page' or 'svg'")


if __name__ == "__main__":
    main()
