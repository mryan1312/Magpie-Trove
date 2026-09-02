# Magpie Trove

A Windows desktop app for tagging, searching and organising an image library.
Everything runs locally — no accounts, no telemetry, no uploads.

- **Tag, rate and flag** images, with a nested tag hierarchy and pinned tags on
  number keys
- **Search and filter** by tag, folder, filename, rating, flag, date and camera
  metadata
- **Cull** with pick/reject flags, then delete every reject in one step — to the
  Recycle Bin, after a confirmation stating the count and size
- **Find duplicates** by exact hash and by perceptual hash
- **Visual search** — with an optional CLIP model installed, find lookalike
  images and get tag suggestions
- **Export and transfer** — export photos with resizing, move tags between
  libraries as JSON/CSV
- **Multiple libraries**, each a self-contained folder you choose

WPF on .NET 10, SQLite for storage, ONNX Runtime for inference. x64 Windows.

## Repository layout

    Source/       the application
    packaging/    MSIX and zip build scripts, Store assets, listing material
    Bin/          the original binary the source was recovered from

Two directories are deliberately not tracked: `Libraries/` (personal image
libraries) and `Model/` (a 336 MB model download). See `.gitignore`.

## Building

Needs the .NET 10 SDK.

```powershell
dotnet build Source\MagpieTrove.csproj
```

To produce the release artifacts:

```powershell
cd packaging
.\New-Zip.ps1     # portable single-file zip -> ../dist/
.\New-Msix.ps1    # MSIX for the Microsoft Store -> out/
```

`New-Msix.ps1` needs the Windows SDK for `makeappx`
(`winget install Microsoft.WindowsSDK.10.0.26100`).
See [packaging/PUBLISHING.md](packaging/PUBLISHING.md) for the Store submission
process.

## The visual search model

Visual search needs the CLIP ViT-B/32 vision encoder (~351 MB), which the app
downloads on request from **Libraries… → Download model** and verifies against a
published SHA-256 checksum. Everything else works without it; only the VISUAL
SEARCH panel is hidden until it is installed.

The model is deliberately *not* redistributed with the app. Neither
`Xenova/clip-vit-base-patch32` nor `openai/clip-vit-base-patch32` declares a
licence on Hugging Face, and whether the MIT licence on the CLIP source repo
extends to the separately-hosted weights was
[asked of OpenAI in 2021](https://github.com/openai/CLIP/issues/203) and never
answered. Downloading on the user's behalf keeps the app out of that question.

## Licence

[MIT](LICENSE).

The CLIP model the app can download is **not** covered by that licence and is
not distributed with the app — see "The visual search model" above.

## Provenance

This source was reconstructed from the shipped binary after the original was
lost, then renamed from Taggr to Magpie Trove. The recovery process, the
decompiler artifacts that had to be corrected, and how the result was verified
against `Bin/Taggr.dll` are documented in
[Source/RECOVERY_NOTES.md](Source/RECOVERY_NOTES.md) — worth reading before
making sense of anything that looks oddly written.
