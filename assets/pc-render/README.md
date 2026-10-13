# The PC render

`render.png` is the picture of a PC in the Overview header. It's split into layers by `build/pc-render.py`, which
writes them to `src/Coreloom.App/Assets/PcRender/` and generates `src/Coreloom.App/Views/PcIllustration.xaml`:

- the glow is separated from the rest, so the app can relight it in any color;
- the fans' blades are separated from their lighting, so the app can turn them;
- the text on the pump screen and the graphics card is painted out, so the app can draw the live values.

## Replacing the render

1. Export the new render as a PNG with a transparent background, about 1100 × 1400 px, lit in one blue (the script
   separates that hue). Keep the fans visible from the front or at a shallow angle.
2. Save it here as `render.png`.
3. Update the coordinates at the top of `build/pc-render.py` if anything moved: `CROP`, the fan ellipses (`FANS`),
   the text boxes to paint out (`TEXT_BOXES`) and where the live text goes (`SCREEN_VALUE`, `SCREEN_LABEL`,
   `GPU_LABEL`).
4. Run `python3 build/pc-render.py --preview previews/` (needs Python 3.10+, `pip install numpy pillow
   opencv-python-headless`) and check the recolored previews.
5. Commit the render, the regenerated layers and `PcIllustration.xaml`.
