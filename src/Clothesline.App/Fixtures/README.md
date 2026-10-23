# Synthetic format probes

These small images contain only generated color blocks. They contain no user photos, upstream project assets or third-party sample artwork.

- `format-probe.webp` / `format-probe.png`: lossless 32 × 24 green and blue blocks.
- `format-probe.heic`: the same generated pixels encoded as 8-bit HEVC/HEIF, quality 100. Lossy color differences are allowed by the probe.
- `alpha.webp`: 32 × 24 RGBA, alpha 128.
- `animated.webp`: two frames; the first is the probe and the second is red.
- `large.webp`: 1024 × 768 RGBA, alpha 128, for thumbnail bounds.

Reproduction script: `scripts/generate-format-fixtures.py` at the repository root. The initial assets were generated with Pillow 13.0.0 / pillow-heif 1.8.1 (libheif 1.23.6, x265 4.3+1-e9b8812); the large WebP used Pillow 12.2.0. These are development-only generators. No Python, libwebp, libheif or x265 library is included in the application or required to build it. The application decodes using Windows components already on the user's machine.

Only the encoded files are embedded. A successful probe establishes support for this sample, not every WebP/HEIF variant or the ability to encode those formats. Test-only simulated failures do not change system codec registration.
