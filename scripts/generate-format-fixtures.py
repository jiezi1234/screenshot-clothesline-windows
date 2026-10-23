"""Regenerate synthetic, non-user image fixtures. Development only; not imported by the app.
Use an isolated Python environment with Pillow==13.0.0 and pillow-heif==1.8.1.
"""
from pathlib import Path
from PIL import Image
import pillow_heif

pillow_heif.register_heif_opener()
destination = Path(__file__).resolve().parents[1] / "src/Clothesline.App/Fixtures"
destination.mkdir(exist_ok=True)
image = Image.new("RGB", (32, 24), (38, 146, 67))
for x in range(16, 32):
    for y in range(24):
        image.putpixel((x, y), (29, 64, 191))
image.save(destination / "format-probe.webp", lossless=True, exact=True)
image.save(destination / "format-probe.heic", quality=100)
image.save(destination / "format-probe.png")
Image.new("RGBA", (32, 24), (210, 90, 20, 128)).save(destination / "alpha.webp", lossless=True, exact=True)
image.save(destination / "animated.webp", save_all=True,
           append_images=[Image.new("RGB", (32, 24), (220, 20, 60))], duration=200, loop=0, lossless=True)
Image.new("RGBA", (1024, 768), (210, 90, 20, 128)).save(destination / "large.webp", lossless=True, exact=True)
