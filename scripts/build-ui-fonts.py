"""Build Fax Sans from the OFL Noto Sans SC variable font.

Requires fonttools[woff] and brotli. Pass NotoSansSC-VF.ttf as --source.
Normal app builds consume the committed fonts directly.
"""
from argparse import ArgumentParser
from pathlib import Path
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

parser = ArgumentParser(description=__doc__)
parser.add_argument("--source", required=True, type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
shell = root / "apps/windows-shell"
# User/AI content is not bounded by strings present in source code.
codepoints = set(range(0x20, 0x100)) | set(range(0x2000, 0x2070))
codepoints |= set(range(0x3000, 0x3100)) | set(range(0x4E00, 0xA000))
codepoints |= set(range(0xFF00, 0xFFF0))
for directory in (shell, root / "tests", root / "examples"):
    for path in directory.rglob("*"):
        if path.suffix in (".html", ".js", ".cjs", ".cs", ".xaml", ".json") and not {"bin", "obj", "node_modules"}.intersection(path.parts):
            codepoints.update(map(ord, path.read_text(errors="ignore")))
for weight, style, suffix in ((400, "Regular", "regular"), (600, "SemiBold", "semibold")):
    font = instantiateVariableFont(TTFont(args.source), {"wght": weight}, inplace=True)
    for name in font["name"].names:
        value = {1: "Fax Sans", 2: style, 3: "Fax Sans " + style,
                 4: "Fax Sans " + style, 6: "FaxSans-" + style,
                 16: "Fax Sans", 17: style}.get(name.nameID)
        if value:
            name.string = value.encode(name.getEncoding())
    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_legacy = True
    options.name_languages = ["*"]
    builder = subset.Subsetter(options=options)
    builder.populate(unicodes=codepoints)
    builder.subset(font)
    font.recalcTimestamp = False
    cmap = font.getBestCmap()
    assert all(ord(char) in cmap for char in "迭涉龘馨磊鬱"), "Chinese coverage regressed"
    native = shell / f"assets/fonts/fax-sans-{suffix}.ttf"
    web = shell / f"ui/fonts/fax-sans-{suffix}.woff2"
    font.save(native)
    font.flavor = "woff2"
    font.save(web)
    print(suffix, len(cmap), "mapped characters", native.stat().st_size, web.stat().st_size)
