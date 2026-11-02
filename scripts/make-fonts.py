#!/usr/bin/env python3
"""Makes the app's fonts: static weights of Nunito, the rounded type standing in for the Mac app's SF Rounded.

WPF can't pick a weight from a variable font, so each weight is its own font with a family name of its own, which the
styles name directly (Bubble.cs). Subset to the characters the app uses, to keep them small.

Usage: python3 scripts/make-fonts.py path/to/Nunito[wght].ttf
The variable font is at https://github.com/google/fonts/tree/main/ofl/nunito (SIL Open Font License, OFL.txt).
Needs fontTools: pip install fonttools
"""
import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

WEIGHTS = {"SemiBold": 600, "Bold": 700, "ExtraBold": 800, "Black": 900}
# Latin, Latin-1, Latin Extended-A, general punctuation, arrows, ×, −, ♭ and ♯
UNICODES = ([*range(0x20, 0x7F), *range(0xA0, 0x180), *range(0x2010, 0x2040), *range(0x2190, 0x2194), 0xD7, 0x2212,
             0x2122, 0x266D, 0x266F])
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "JaredsWindowsUtils", "Assets", "Fonts")


def main(source):
    for style, weight in WEIGHTS.items():
        font = instancer.instantiateVariableFont(TTFont(source), {"wght": weight}, updateFontNames=True)
        family = f"Nunito {style}"
        names = font["name"]
        # One family per weight, so WPF never has to choose or fake one
        for record in list(names.names):
            if record.nameID in (16, 17, 21, 22):
                names.removeNames(nameID=record.nameID)
        names.setName(family, 1, 3, 1, 0x409)
        names.setName("Regular", 2, 3, 1, 0x409)
        names.setName(f"{family} Regular", 4, 3, 1, 0x409)
        names.setName(f"Nunito-{style}", 6, 3, 1, 0x409)
        font["OS/2"].usWeightClass = weight
        font["OS/2"].fsSelection = (font["OS/2"].fsSelection & ~0b1100001) | 0b1000000  # REGULAR, not bold or italic
        font["head"].macStyle = 0
        options = subset.Options()
        options.layout_features = ["*"]
        options.name_IDs = ["*"]
        options.name_languages = ["*"]
        subsetter = subset.Subsetter(options)
        subsetter.populate(unicodes=UNICODES)
        subsetter.subset(font)
        path = os.path.join(OUT, f"Nunito-{style}.ttf")
        font.save(path)
        print(f"Wrote {os.path.normpath(path)} ({os.path.getsize(path) // 1024} KB)")


if __name__ == "__main__":
    main(sys.argv[1])
