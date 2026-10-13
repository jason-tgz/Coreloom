# Picture of your PC (optional)

The Overview page opens with a header for this PC: the motherboard name, the graphics card and the key specs, next
to a picture of the build. By default that picture is a **drawn illustration** that follows the detected hardware
(two or four memory sticks, a large or medium graphics card with its model name, the CPU temperature on the pump
screen) and lights up in your accent color. It contains no brand logos or product photos.

You can replace it with a picture of your own build, for example a 3D render or a photo.

## Image guidelines

- **PNG with a transparent background** looks best (the card color shows around the case); JPG works too.
- Square, or close to it, at least **440 × 440 px** (the picture is shown 210 px wide, so 440 px stays sharp at 200 %
  display scaling). It is scaled to fit, never cropped.
- At most 12 MB. Larger files are ignored.
- Only use images you made or have the right to use.

## For your copy only (no rebuild)

Save the file as `pc.png` (or `pc.jpg`) in `%AppData%\Coreloom` (portable copies: the `Data` folder next to the
program) and restart Coreloom. A file there wins over an embedded one.

## For every build

Save it as `pc.png` in this folder (`assets/pc/`), commit and push; the next build embeds it and every install shows
it instead of the drawing.
