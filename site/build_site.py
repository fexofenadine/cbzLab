#!/usr/bin/env python3
#builds the static site into _site/ from site/*.html (copied as-is) plus the
#project's real markdown docs (converted to html and wrapped in the same
#header/footer chrome) - this is what keeps the published site in sync with
#docs/USER_GUIDE.md, docs/BUILDING.md and CHANGELOG.md rather than a hand
#duplicated copy that drifts. run locally with `python site/build_site.py`
#to preview, or via .github/workflows/pages.yml in ci.
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
    <a class="brand" href="index.html" style="text-decoration:none">
      <img src="assets/logo-200.png" alt="">
      <span>cbzLab</span>
    </a>
    <nav>
      <a href="guide.html">User Guide</a>
      <a href="building.html">Building</a>
      <a href="changelog.html">Changelog</a>
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
<title>{title} — cbzLab</title>
<link rel="icon" type="image/png" href="assets/logo-200.png">
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

    print(f"built {OUT}")


if __name__ == "__main__":
    main()
