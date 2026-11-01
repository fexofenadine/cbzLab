#!/usr/bin/env python3
#builds the static site into _site/ from site/*.html (copied as-is) plus the
#project's real markdown docs (converted to html and wrapped in the same
#header/footer chrome) - this is what keeps the published site in sync with
#docs/USER_GUIDE.md, docs/BUILDING.md and CHANGELOG.md rather than a hand
#duplicated copy that drifts. run locally with `python site/build_site.py`
#to preview, or via .github/workflows/pages.yml in ci.
import base64
import json
import pathlib
import re
import shutil
import sys

try:
    import markdown
except ImportError:
    sys.exit("missing dependency: pip install markdown")

ROOT = pathlib.Path(__file__).resolve().parent.parent
SITE = ROOT / "site"
OUT = ROOT / "_site"

NAV = """
<header class="top">
  <div class="wrap">
    <a class="brand" href="index.html">
      <img src="assets/logo-200.png" alt="">
      <span>cbzLab</span>
    </a>
    <nav>
      <a href="guide.html">User Guide</a>
      <a href="building.html">Building</a>
      <a href="changelog.html">Changelog</a>
      <a href="lite/">cbzLite</a>
      <a href="https://github.com/fexofenadine/cbzLab">GitHub</a>
    </nav>
  </div>
</header>
"""

FOOTER = """
<footer>
  <div class="wrap">
    cbzLab is developed by <a href="https://github.com/fexofenadine">fexofenadine</a>.
    Source on <a href="https://github.com/fexofenadine/cbzLab">GitHub</a>.
  </div>
</footer>
"""

PAGE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} - cbzLab</title>
<link rel="icon" type="image/png" href="assets/logo-200.png">
<link rel="preload" href="assets/fonts/ooohbaby-latin.woff2" as="font" type="font/woff2" crossorigin>
<link rel="preload" href="assets/fonts/audiowide-latin.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="styles.css">
</head>
<body>
{nav}
<div class="doc"><div class="wrap">
{content}
</div></div>
{footer}
</body>
</html>
"""

#(source markdown file, output html filename, page title)
DOC_PAGES = [
    (ROOT / "docs" / "USER_GUIDE.md", "guide.html", "User Guide"),
    (ROOT / "docs" / "BUILDING.md", "building.html", "Building from source"),
    (ROOT / "CHANGELOG.md", "changelog.html", "Changelog"),
]


def rewrite_relative_links(html: str, md_dir: pathlib.Path) -> str:
    #docs/*.md link to sibling repo files (../README.md, ARCHIVED.md, etc.)
    #that don't exist as pages on the site - point those back at github so
    #they still resolve, while leaving in-site links (screenshots/*, http(s)://)
    #alone. src= is always an image/asset, so it needs the raw content host,
    #not the html "blob" viewer page a plain github link would give an <img>.
    def fix(match: re.Match) -> str:
        attr, url = match.group(1), match.group(2)
        if url.startswith(("http://", "https://", "#", "screenshots/")):
            return match.group(0)
        target = (md_dir / url).resolve()
        try:
            rel = target.relative_to(ROOT)
        except ValueError:
            return match.group(0)
        host = (
            "https://raw.githubusercontent.com/fexofenadine/cbzLab/master/"
            if attr == "src"
            else "https://github.com/fexofenadine/cbzLab/blob/master/"
        )
        return f'{attr}="{host}{rel.as_posix()}"'

    return re.sub(r'(href|src)="([^"]+)"', fix, html)


def build_doc_page(src: pathlib.Path, out_name: str, title: str) -> None:
    text = src.read_text(encoding="utf-8")
    html = markdown.markdown(text, extensions=["extra", "sane_lists", "toc"])
    html = rewrite_relative_links(html, src.parent)
    (OUT / out_name).write_text(
        PAGE.format(title=title, nav=NAV, content=html, footer=FOOTER),
        encoding="utf-8",
    )


LITE = ROOT / "cbzLite"
DESKTOP_ASSETS = ROOT / "cbzLab" / "Assets"


def inline_json(data) -> str:
    #"</" would end the surrounding <script> element early
    return json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")


def lite_themes() -> dict:
    #the desktop's themes.json plus its one-theme-per-file extras, named after the file, the same
    #merge ThemeService.LoadThemes does at startup
    doc = json.loads((DESKTOP_ASSETS / "themes.json").read_text(encoding="utf-8"))
    themes = doc.setdefault("themes", {})
    for path in sorted((DESKTOP_ASSETS / "themes").glob("*.json")):
        try:
            themes[path.stem] = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            print(f"warning: skipped theme {path.name}: {e}", file=sys.stderr)
    return doc


def build_lite() -> pathlib.Path:
    #cbzLite ships as one self-contained html file - stylesheet, scripts, and the desktop app's own
    #schema.json/themes.json all inlined - so it also works saved locally and opened offline
    html = (LITE / "index.html").read_text(encoding="utf-8")

    css = (LITE / "styles.css").read_text(encoding="utf-8")
    html = html.replace('<link rel="stylesheet" href="styles.css">', f"<style>\n{css}</style>")

    def script(match: re.Match) -> str:
        source = (LITE / match.group(1)).read_text(encoding="utf-8")
        if "</script" in source.lower():
            sys.exit(f"{match.group(1)} contains '</script', which would break inlining")
        return f"<script>\n{source}</script>"

    html = re.sub(r'<script src="([^"]+)"></script>', script, html)

    #the logo and favicon go in as data uris too, so a saved copy still has them offline
    def asset(match: re.Match) -> str:
        path = LITE / "assets" / match.group(2)
        mime = {".png": "image/png", ".webp": "image/webp", ".svg": "image/svg+xml"}[path.suffix]
        data = base64.b64encode(path.read_bytes()).decode("ascii")
        return f'{match.group(1)}="data:{mime};base64,{data}"'

    html = re.sub(r'\b(src|href)="assets/([^"]+)"', asset, html)

    schema = json.loads((DESKTOP_ASSETS / "schema.json").read_text(encoding="utf-8"))
    html = html.replace(
        '<script id="cbz-schema" type="application/json"></script>',
        f'<script id="cbz-schema" type="application/json">{inline_json(schema)}</script>',
    )
    html = html.replace(
        '<script id="cbz-themes" type="application/json"></script>',
        f'<script id="cbz-themes" type="application/json">{inline_json(lite_themes())}</script>',
    )
    html = html.replace(
        "<!--build_site.py inlines the stylesheet, scripts, schema and themes into one self-contained file-->\n", ""
    )

    out_dir = OUT / "lite"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / "index.html"
    out.write_text(html, encoding="utf-8")
    return out


def main() -> None:
    if OUT.exists():
        shutil.rmtree(OUT)
    OUT.mkdir(parents=True)

    for item in SITE.iterdir():
        if item.name == "build_site.py":
            continue
        dest = OUT / item.name
        if item.is_dir():
            shutil.copytree(item, dest)
        else:
            shutil.copy2(item, dest)

    screenshots_src = ROOT / "docs" / "screenshots"
    if screenshots_src.is_dir():
        shutil.copytree(screenshots_src, OUT / "screenshots", dirs_exist_ok=True)

    for src, out_name, title in DOC_PAGES:
        if src.is_file():
            build_doc_page(src, out_name, title)
        else:
            print(f"warning: {src} not found, skipping {out_name}", file=sys.stderr)

    if (LITE / "index.html").is_file():
        print(f"built {build_lite()}")

    print(f"built {OUT}")


if __name__ == "__main__":
    main()
