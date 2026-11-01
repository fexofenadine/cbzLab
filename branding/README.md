# Branding

The source artwork for the cbzLab and cbzLite logos, kept so they can be redrawn or re-lettered later
without starting from scratch.

- `badge.png` - the sunset and grid badge on its own, with no wordmark: the background both logos are
  built on, at full resolution with a transparent surround.
- `logo-cbzLab.png`, `logo-cbzLite.png` - the matching pair, 1200×1200, drawn from that badge.
- `original-cbzLab-logo.png` - the original hand-made cbzLab logo the badge's colours were sampled from.
- `make_logo.py` - redraws all of the above (except the original). `py branding/make_logo.py cbzLab cbzLite`
  rewrites `badge.png` and one `logo-<name>.png` per name given. Needs Pillow. The colours, font size,
  angle and glow are settings at the top of the script.
- `apply_logos.py` - copies the artwork, resized, to everywhere it's used: `cbzLab/Assets/logo.png`
  and `icon.ico` (the badge alone, which stays legible at 16px) for the desktop app, `site/assets/` for
  the website, and `cbzLite/assets/` for cbzLite. Run it after `make_logo.py`.
- `fonts/` - Oooh Baby, the wordmark font, with its SIL Open Font License. The website uses it too.
