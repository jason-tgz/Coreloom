#!/usr/bin/env python3
"""
Static consistency checks for Vigil.App's XAML that can run anywhere (no Windows, no WPF build).

The real verification is the Windows build (the XAML compiler) plus running the app; this script exists to
catch the common mistakes earlier and on any OS:

  * well-formed XML, and x:Class matches a partial class in the code-behind
  * StaticResource keys are defined before use (merge order respected), DynamicResource keys exist
    (including the brushes ThemeService creates at runtime), and runtime brushes are never used as
    StaticResource
  * event handler names in XAML exist in the code-behind
  * ElementName references and x:Name fields used from code-behind exist
  * every {Binding} path segment exists as a public property somewhere in the view models / core models
    or is a known WPF property (catches typos, not type mismatches)
  * icon keys referenced from view models exist, and pack URIs point at files embedded as Resource

Usage: python3 build/check-xaml.py            (exit code 1 when problems are found)
"""
from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "Vigil.App"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"

# Merge order of App.xaml; Templates.xaml merges Icons/Converters/Controls itself.
GLOBAL_ORDER = ["Themes/Icons.xaml", "Themes/Converters.xaml", "Themes/Controls.xaml", "Themes/Templates.xaml"]
VIEWS = sorted((APP / "Views").glob("*.xaml"))

EVENT_ATTRS = {
    "Click", "Loaded", "Unloaded", "KeyDown", "KeyUp", "PreviewKeyDown", "SelectionChanged", "MouseDown", "MouseUp",
    "MouseLeftButtonDown", "MouseLeftButtonUp", "Checked", "Unchecked", "TextChanged", "SizeChanged", "Closing",
    "Closed", "GotFocus", "LostFocus", "PreviewMouseDown", "MouseEnter", "MouseLeave", "ValueChanged",
}

# WPF / framework properties that legitimately appear in binding paths (RelativeSource/ElementName/TemplatedParent
# bindings, or collection helpers).
WPF_PROPERTIES = {
    "Text", "IsChecked", "SelectedItem", "SelectedValue", "SelectedIndex", "DataContext", "ActualWidth", "ActualHeight",
    "IsMouseOver", "IsEnabled", "Visibility", "Count", "Content", "Tag", "Value", "Maximum", "Minimum", "Width", "Height",
    "IsKeyboardFocused", "IsSelected", "Items", "HasItems", "IsCompactSidebar", "Foreground", "Background", "IsDropDownOpen",
}


class Problems:
    def __init__(self) -> None:
        self.items: list[str] = []

    def add(self, file: Path, message: str) -> None:
        self.items.append(f"{file.relative_to(ROOT)}: {message}")


def keys_in_order(tree: ET.ElementTree) -> list[str]:
    return [el.attrib[f"{X}Key"] for el in tree.iter() if f"{X}Key" in el.attrib]


def markup_refs(text: str, kind: str) -> list[str]:
    return re.findall(r"\{" + kind + r"\s+([^}\s,]+)\s*\}", text)


def runtime_brush_keys() -> set[str]:
    """Keys ThemeService puts into Application.Resources at startup."""
    palette = (APP / "Themes" / "ThemePalette.cs").read_text(encoding="utf-8")
    service = (APP / "Themes" / "ThemeService.cs").read_text(encoding="utf-8")
    keys: set[str] = set()
    for key in re.findall(r'\["([A-Za-z.]+)"\]\s*=', palette):
        keys.add(key + ".Brush" if key.startswith("Series.") else "Brush." + key)
    keys.update(re.findall(r'"((?:Brush|Series)\.[A-Za-z.]+)"', service))
    surface = re.search(r"SurfaceKeys\s*=\s*\[([^\]]*)\]", service)
    if surface:
        keys.update("Brush." + k for k in re.findall(r'"(\w+)"', surface.group(1)))
    return keys


def public_properties() -> set[str]:
    names: set[str] = set()
    sources = list((ROOT / "src" / "Vigil.ViewModels").rglob("*.cs")) + list((ROOT / "src" / "Vigil.Core").rglob("*.cs"))
    for path in sources:
        text = path.read_text(encoding="utf-8")
        names.update(re.findall(r"public\s+(?:required\s+|override\s+|virtual\s+|static\s+)*[\w<>\[\]?,. ()]+?\s+(\w+)\s*(?:\{|=>)", text))
        # positional record parameters become properties
        for params in re.findall(r"record(?:\s+struct|\s+class)?\s+\w+(?:<[^>]+>)?\s*\(([^)]*)\)", text):
            names.update(re.findall(r"(\w+)\s*(?:,|$|=)", params.strip()))
    return names


def binding_paths(text: str) -> list[str]:
    paths: list[str] = []
    for match in re.finditer(r"\{Binding(?:\s+([^,}=]+?))?(\s*,[^}]*)?\}", text):
        path = (match.group(1) or "").strip()
        rest = match.group(2) or ""
        explicit = re.search(r"Path=([^,}\s]+)", rest)
        if explicit:
            path = explicit.group(1)
        if path and not path.startswith("("):
            paths.append(path)
    # <Binding Path="X" /> elements inside MultiBinding
    paths.extend(re.findall(r'<Binding\s+Path="([^"]+)"', text))
    return paths


def main() -> int:
    problems = Problems()
    runtime_keys = runtime_brush_keys()
    props = public_properties()

    trees: dict[Path, ET.ElementTree] = {}
    for path in [APP / "App.xaml", *(APP / p for p in GLOBAL_ORDER), *VIEWS]:
        try:
            trees[path] = ET.parse(path)
        except ET.ParseError as exc:
            problems.add(path, f"XML parse error: {exc}")
    if problems.items:
        print("\n".join(problems.items))
        return 1

    # ---- resource keys -------------------------------------------------------------------------
    defined_so_far: set[str] = set()
    for rel in GLOBAL_ORDER:
        path = APP / rel
        text = path.read_text(encoding="utf-8")
        local_keys = keys_in_order(trees[path])
        for key in markup_refs(text, "StaticResource"):
            if key in runtime_keys or key.startswith(("Brush.", "Series.")):
                problems.add(path, f"runtime brush '{key}' must be a DynamicResource")
            elif key not in defined_so_far and key not in local_keys:
                problems.add(path, f"StaticResource '{key}' is not defined in this or an earlier dictionary")
            elif key in local_keys and key not in defined_so_far:
                # must be defined earlier in the same file (StaticResource resolves while loading)
                use = text.find("{StaticResource " + key)
                define = text.find(f'x:Key="{key}"')
                if define > use:
                    problems.add(path, f"StaticResource '{key}' is used before it is defined")
        defined_so_far.update(local_keys)

    global_keys = set(defined_so_far)
    all_keys = global_keys | runtime_keys

    for path in [APP / "App.xaml", *(APP / p for p in GLOBAL_ORDER), *VIEWS]:
        text = path.read_text(encoding="utf-8")
        local_keys = set(keys_in_order(trees[path]))
        if path in VIEWS or path.name == "App.xaml":
            for key in markup_refs(text, "StaticResource"):
                if key.startswith(("Brush.", "Series.")):
                    problems.add(path, f"runtime brush '{key}' must be a DynamicResource")
                elif key not in global_keys and key not in local_keys:
                    problems.add(path, f"StaticResource '{key}' is not defined")
        for key in markup_refs(text, "DynamicResource"):
            if key not in all_keys and key not in local_keys:
                problems.add(path, f"DynamicResource '{key}' is not defined anywhere (and not created by ThemeService)")

    # ---- x:Class, handlers, names ------------------------------------------------------------
    code = {p: p.read_text(encoding="utf-8") for p in APP.rglob("*.cs") if "/obj/" not in str(p) and "/bin/" not in str(p)}
    all_code = "\n".join(code.values())
    for path in [APP / "App.xaml", *VIEWS]:
        root = trees[path].getroot()
        cls = root.attrib.get(f"{X}Class")
        if not cls:
            problems.add(path, "missing x:Class")
            continue
        short = cls.split(".")[-1]
        if not re.search(rf"partial\s+class\s+{short}\b", all_code):
            problems.add(path, f"no partial class {short} in code-behind")
            continue
        body_files = [t for t in code.values() if re.search(rf"partial\s+class\s+{short}\b", t)]
        body = "\n".join(body_files)
        names = {el.attrib[f"{X}Name"] for el in root.iter() if f"{X}Name" in el.attrib}
        for el in root.iter():
            for attr, value in el.attrib.items():
                local = attr.split("}")[-1]
                if local in EVENT_ATTRS and not value.startswith("{"):
                    if not re.search(rf"\b(void|Task)\s+{re.escape(value)}\s*\(", body):
                        problems.add(path, f"handler '{value}' for {local} not found in {short}")
        text = path.read_text(encoding="utf-8")
        for name in re.findall(r"ElementName=(\w+)", text):
            if name not in names:
                problems.add(path, f"ElementName '{name}' does not exist")
        # fields used from code-behind that should come from x:Name
        for field in set(re.findall(r"\b([A-Z]\w+)\.(?:SelectedItem|ItemsSource|Content|ToolTip|Margin|Width|BeginAnimation)\b", body)):
            if field not in names and not re.search(rf"\b(class|var|static)\s+{field}\b", all_code) and field not in {"Motion", "AnimatedText", "Timeline", "Sidebar"}:
                if field[0].isupper() and field not in ("ThemeService", "NativeWindow", "SnapLayoutSupport", "WindowState"):
                    problems.add(path, f"code-behind uses '{field}' which is not an x:Name in the XAML")
        if short == "MainWindow":
            for required in ["Sidebar", "NavList", "SettingsNav", "MaximizeButton", "RootBorder", "LivePulse", "LivePulseScale"]:
                if required not in names:
                    problems.add(path, f"code-behind expects x:Name '{required}'")

    # ---- bindings ----------------------------------------------------------------------------------
    for path in [*(APP / p for p in GLOBAL_ORDER), *VIEWS]:
        text = path.read_text(encoding="utf-8")
        for binding in binding_paths(text):
            for segment in re.split(r"[.\[\]]", binding):
                segment = segment.strip()
                if not segment or segment.isdigit():
                    continue
                if segment not in props and segment not in WPF_PROPERTIES:
                    problems.add(path, f"binding path segment '{segment}' (in '{binding}') is not a known property")

    # ---- icon keys referenced from code / view models -------------------------------------------
    vm_text = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "src" / "Vigil.ViewModels").rglob("*.cs"))
    for key in set(re.findall(r'"(Icon\.\w+)"', vm_text + all_code)):
        if key not in global_keys:
            problems.add(APP / "Themes" / "Icons.xaml", f"icon '{key}' is referenced from code but not defined")
    for key in set(re.findall(r'FindResource\("([^"]+)"', all_code)) | set(re.findall(r'"(Glyph\.\w+)"', all_code)):
        if key not in global_keys:
            problems.add(APP / "Views" / "MainWindow.xaml.cs", f"resource '{key}' looked up from code is not defined")

    # ---- pack URIs -------------------------------------------------------------------------------
    csproj = (APP / "Vigil.App.csproj").read_text(encoding="utf-8")
    everything = all_code + "\n".join(p.read_text(encoding="utf-8") for p in trees)
    for uri in set(re.findall(r"pack://application:,,,/([\w/.-]+)", everything)):
        file = APP / uri
        if not file.exists():
            problems.add(APP / "Vigil.App.csproj", f"pack URI target '{uri}' does not exist")
        elif f'Resource Include="{uri.replace("/", chr(92))}"' not in csproj:
            problems.add(APP / "Vigil.App.csproj", f"'{uri}' is not included as <Resource>")

    # ---- settings sections -----------------------------------------------------------------------
    settings_vm = (ROOT / "src" / "Vigil.ViewModels" / "Pages" / "SettingsViewModel.cs").read_text(encoding="utf-8")
    sections = re.search(r"Sections\s*\{\s*get;\s*\}\s*=\s*\[([^\]]*)\]", settings_vm)
    page = (APP / "Views" / "SettingsPage.xaml").read_text(encoding="utf-8")
    if sections:
        declared = re.findall(r'"(\w+)"', sections.group(1))
        shown = re.findall(r"ConverterParameter=(\w+)\}\"\s*>", page) + re.findall(r"EqualsToVisibility\},\s*ConverterParameter=(\w+)", page)
        for name in declared:
            if name not in shown:
                problems.add(APP / "Views" / "SettingsPage.xaml", f"settings section '{name}' has no panel")

    if problems.items:
        print("\n".join(sorted(set(problems.items))))
        print(f"\n{len(set(problems.items))} problem(s)")
        return 1

    n_bindings = sum(len(binding_paths(p.read_text(encoding='utf-8'))) for p in [*(APP / q for q in GLOBAL_ORDER), *VIEWS])
    print(f"XAML checks passed: {len(trees)} files, {len(all_keys)} resource keys, {n_bindings} bindings checked.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
