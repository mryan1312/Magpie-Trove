# Store listing copy

Paste these into Partner Center under **Store listings > en-us**. Field limits
are noted; Partner Center rejects anything longer. The description field is
plain text — line breaks survive, markdown does not, so the bullets below use
a literal `•`.

---

## Short description
*(max 500 characters — shown in search results and on the product card)*

```
Magpie Trove tags, sorts and searches your image collection entirely on your own machine. Build a nested tag hierarchy, rate and flag as you go, filter by capture date or camera settings, and clear out duplicates. Add the optional visual-search model and it will find lookalike images and suggest tags for you. No account, no telemetry, nothing uploaded.
```

---

## Description
*(max 10,000 characters)*

```
Magpie Trove is a tagging and search tool for people whose image collection has outgrown folders.

Point it at a folder tree and it indexes everything in place. Your files are never moved, renamed or altered — the library is a database that sits alongside them, and you can keep as many separate libraries as you like.

TAG QUICKLY
Tags nest, so Clothes > Underwear > Jockstrap behaves the way you would expect, and a parent can be applied automatically with its children. Pin your most-used tags to the number keys and work through a folder without touching the mouse. Advance-after-tagging moves you to the next image the moment you have tagged the current one. Everything is undoable, including bulk operations.

FIND ANYTHING
Filter by tag — matching all or any — by folder, filename, star rating, or pick and reject flags. Narrow by capture date range. Narrow by camera body, lens, ISO, aperture, shutter speed or focal length, read straight from the embedded metadata. Sort by any of fifteen fields, including a random shuffle for when you are culling. Save a filter as a collection, or build a smart collection that keeps itself up to date.

CLEAR OUT DUPLICATES
Find byte-identical copies and visually near-identical ones, with an adjustable sensitivity threshold. Magpie Trove can mark the highest-resolution copy in every group as the keeper, so removing the rest is one click. Removal only ever affects the library; your files stay on disk.

VISUAL SEARCH (OPTIONAL)
Install the visual-search model and Magpie Trove learns what your images look like. It will then find lookalikes of any image, and suggest tags based on what visually similar images in your own library are already tagged with. The model is a one-time 351 MB download from inside the app, verified against a published checksum. Everything else works without it.

LOOK PROPERLY
A full-screen viewer with fit and actual-size zoom, rotation, a slideshow, and side-by-side compare for two images. Tag without leaving the viewer.

GET THINGS OUT AGAIN
Export selected photos with resizing, format conversion and rename patterns. Move tags between libraries — or in and out of other software — as JSON or CSV. Import embedded IPTC and XMP keywords from files that already carry them.

PRIVATE BY DESIGN
Magpie Trove has no account, no sign-in, no telemetry, no analytics and no advertising. Your images, tags and thumbnails stay on your computer. The only time it touches the network is if you choose to download the visual-search model.

REQUIREMENTS
64-bit Windows 10 or 11. Roughly 200 MB of disk space, plus 351 MB if you install the visual-search model.
```

---

## Product features
*(up to 20 entries, max 200 characters each — rendered as a bulleted list)*

```
Nested tag hierarchy, with parent tags applied automatically
Pin your most-used tags to the number keys and tag without the mouse
Star ratings, plus pick and reject flags for culling
Filter by tag, folder, filename, rating, flag or capture date
Filter by camera, lens, ISO, aperture, shutter speed and focal length
Sort by any of fifteen fields, including random shuffle
Collections, including smart collections that keep themselves current
Find exact and near-duplicate images, and keep the best copy of each
Optional visual search: find lookalikes and get tag suggestions
Full-screen viewer with zoom, rotation, slideshow and side-by-side compare
Export with resizing, format conversion and rename patterns
Move tags between libraries as JSON or CSV
Import embedded IPTC and XMP keywords
Keep several libraries, each in a folder you choose
Undo and redo, including bulk tagging
Runs entirely offline: no account, no telemetry, nothing uploaded
```

---

## Search terms
*(up to 7, max 30 characters each — not shown to users)*

```
image tagging
photo organizer
duplicate photo finder
offline photo manager
local image search
photo tagger
picture library
```

---

## What's new in this version
*(max 1,500 characters)*

```
First release.
```

---

## Other fields

**Category** — Photo & video (alternative: Productivity)

**Copyright and trademark info** — `Copyright (c) 2026 Matthew Adams`

**Privacy policy URL** — the hosted copy of `privacy.html` from the repository
root; see PUBLISHING.md.

**`runFullTrust` justification** — see the submission checklist in
PUBLISHING.md.

---

## A note on honesty

Two claims in this copy are load-bearing and were checked against the source
rather than assumed. Keep them true if the app changes:

- *"no telemetry, no analytics"* — the only network-capable type in the whole
  application is the single `HttpClient` in `Services/ModelInstallService.cs`.
- *"your files are never moved, renamed or altered"* — true except where the
  user explicitly invokes export or transfer, which the description says.

The 351 MB model figure and the checksum verification are both real; do not
round the number down.
