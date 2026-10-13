# Vendor logos (optional)

Coreloom shows a small vendor mark next to the processor and graphics card names. **No third-party logos are
bundled with Coreloom.** NVIDIA, AMD and Intel (and their logos) are trademarks of their owners, and each company
publishes its own logo files and usage rules. Until you add files here, Coreloom shows a plain-text badge
("NVIDIA", "AMD", "Intel") instead.

## How to add the logos

1. Download each company's official logo from its brand/press page and read the usage rules that come with it.
2. Save each one as a **transparent PNG, about 128–256 px tall** (wide logos are fine; they are scaled to fit) with
   exactly these names:

   | File | Used for |
   |---|---|
   | `nvidia.png` | NVIDIA graphics cards |
   | `amd.png` | AMD processors and graphics cards |
   | `intel.png` | Intel processors and graphics cards |

3. Optional: Coreloom's default theme is dark, so a logo that is dark-on-transparent can be hard to see. Add
   `nvidia-on-dark.png` (a version that reads well on a dark background) and/or `nvidia-on-light.png` (for the light
   theme); the same pattern works for `amd` and `intel`. When a themed file exists it is used instead of the plain one.
4. Put the files in this folder (`assets/vendors/`), commit and push; the next build embeds them.

## Trying them without rebuilding

Copy the same files into `%AppData%\Coreloom\Vendors` (create the folder if needed; portable copies use
`Data\Vendors` next to the program) and restart Coreloom. Files in that folder win over the embedded ones.
Files larger than 2 MB, or taller than 256 px, are ignored or scaled down so a logo can never use much memory.
