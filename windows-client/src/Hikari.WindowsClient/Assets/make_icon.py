"""Generate the Hikari Windows client icon.

Draws the 光 ("hikari" — light) glyph in gold on a wisteria washi-paper tile and
writes a multi-resolution .ico. Re-run this if the palette in Themes/HikariTheme.cs
ever changes.
"""
import os
from PIL import Image, ImageDraw, ImageFont

WISTERIA = (107, 76, 138, 255)
WISTERIA_DIM = (74, 45, 107, 255)
GOLD = (206, 168, 76, 255)
CREAM = (250, 243, 232, 255)

SIZE = 512
RADIUS = 96

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\msyh.ttc",     # Microsoft YaHei
    r"C:\Windows\Fonts\msgothic.ttc",  # MS Gothic
    r"C:\Windows\Fonts\simsun.ttc",
    r"C:\Windows\Fonts\seguiemj.ttf",
]


def load_font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


def build():
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    # Rounded tile with a subtle vertical gradient from wisteria to its dim tone.
    tile = Image.new("RGBA", (SIZE, SIZE), WISTERIA)
    gradient = Image.new("RGBA", (1, SIZE))
    for y in range(SIZE):
        t = y / (SIZE - 1)
        gradient.putpixel((0, y), tuple(
            int(a + (b - a) * t) for a, b in zip(WISTERIA, WISTERIA_DIM)
        ))
    tile = gradient.resize((SIZE, SIZE))

    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, SIZE - 1, SIZE - 1], RADIUS, fill=255)
    image.paste(tile, (0, 0), mask)

    # Gold hairline border, echoing the gold-leaf accent in the app themes.
    draw.rounded_rectangle(
        [8, 8, SIZE - 9, SIZE - 9], RADIUS - 8, outline=GOLD, width=6)

    font = load_font(300)
    text = "光"
    box = draw.textbbox((0, 0), text, font=font)
    x = (SIZE - (box[2] - box[0])) / 2 - box[0]
    y = (SIZE - (box[3] - box[1])) / 2 - box[1]

    draw.text((x + 5, y + 6), text, font=font, fill=(0, 0, 0, 70))
    draw.text((x, y), text, font=font, fill=CREAM)

    return image


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    icon = build()
    icon.save(
        os.path.join(here, "Hikari.ico"),
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    icon.resize((256, 256), Image.LANCZOS).save(os.path.join(here, "Hikari.png"))
    print("wrote Hikari.ico and Hikari.png")


if __name__ == "__main__":
    main()
