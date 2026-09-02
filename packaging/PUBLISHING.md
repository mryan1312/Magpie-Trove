# Publishing Magpie Trove to the Microsoft Store

Everything needed to produce the MSIX lives in this folder. The Store signs the
package on submission, which is the whole reason to take this route: a
Store-installed Magpie Trove satisfies Smart App Control, so it installs and
runs on machines where the plain unsigned zip is blocked outright.

    New-Assets.ps1              regenerates Images/ from Source/assets/magpietrove.ico
    AppxManifest.template.xml   manifest with {{TOKEN}} placeholders
    New-Msix.ps1                publish -> layout -> .msix   (Store channel)
    New-Zip.ps1                 publish -> ../dist/*.zip     (sideload channel)
    README.zip.txt              the README that ships inside the zip
    Images/                     generated tile and logo assets (committed)
    store/                      Store listing material (screenshots)
    layout/, out/               build output, safe to delete

The privacy policy is not here — it lives at `privacy.html` in the repository
root, because that is where GitHub Pages serves it from.

Note the project still lives under `E:\Dev\Taggr`. Only the product was renamed;
the folder was left alone deliberately, so nothing outside the repo breaks.


## Identity

Reserved in Partner Center, from **Product Management > Product identity**:

| Manifest field | Value |
|---|---|
| `Identity/Name` | `Meryndi.MagpieTrove` |
| `Identity/Publisher` | `CN=ADCAE01A-460A-4EED-A5F9-4250834464BA` |
| `Properties/PublisherDisplayName` | `Meryndi` |
| Package Family Name | `Meryndi.MagpieTrove_8xr7vqtychad0` |
| Store ID | `9P8B4NSC5LQL` |

`Properties/DisplayName` in the manifest is `Magpie Trove`, which must match the
reserved app name exactly. If Partner Center shows the reserved name with
different spacing or punctuation, change the manifest to match it, not the
other way round.


## Build the package

```powershell
cd E:\Dev\Taggr\packaging
.\New-Msix.ps1 -IdentityName 'Meryndi.MagpieTrove' `
               -Publisher 'CN=ADCAE01A-460A-4EED-A5F9-4250834464BA' `
               -PublisherDisplayName 'Meryndi'
```

Output: `out\MagpieTrove-1.0.0.0-x64.msix`, about 68 MB.

Leave it **unsigned** — Partner Center signs it. The script only signs when you
pass `-TestSign`, and it refuses to test-sign a package carrying real Store
identity.

Bump `-Version` for each submission. The fourth part must stay `0`; the Store
reserves it.


## Verify locally before uploading

Done once already and passed: the layout registered, launched from the Start
menu, rendered correctly, and opened its database. Repeat after any change that
touches the manifest or the publish output.

Developer Mode is enabled on this machine, so:

```powershell
Add-AppxPackage -Register .\layout\AppxManifest.xml
```

That registers the loose layout with no signing at all. Launch Magpie Trove from
the Start menu, confirm it runs, then
`Get-AppxPackage *MagpieTrove* | Remove-AppxPackage`.

The alternative is `.\New-Msix.ps1 -TestSign` followed by the two admin commands
it prints. Less reliable here: Smart App Control is enforced on this machine and
may refuse a self-signed package regardless.

**Do not try to sideload the Store package itself.**

```
Add-AppxPackage .\out\MagpieTrove-1.0.0.0-x64.msix -AllowUnsigned
  -> 0x80073D2C  the package's publisher is not in the unsigned namespace
```

That is by design. `-AllowUnsigned` only accepts a package whose Publisher
carries the marker OID `OID.2.25.311729368913984317654407730594956997722=1`,
and adding it would change the package identity — the family name would stop
being `Meryndi.MagpieTrove_8xr7vqtychad0` and Partner Center would reject the
upload. Register the layout instead, as above.


## Submission checklist

- [ ] **Packages** — upload `out\MagpieTrove-*.msix`
- [ ] **`runFullTrust` justification** — a restricted capability, so the
      submission form asks why you need it. Text ready to paste under
      "The runFullTrust capability" below.
- [ ] **Privacy policy URL** — **mandatory**, because the app makes a network
      call: `Libraries... > Download model` fetches the CLIP model from Hugging
      Face. Written already: `privacy.html` in the repository root. It must be
      **hosted at a live public HTTPS URL** — Partner Center validates it at
      submission and re-checks later, and a dead link can get a published app
      pulled. See "Hosting the privacy policy" below.
- [ ] **Age rating** — complete the IARC questionnaire.
- [ ] **Screenshots** — `store\screenshot-1-library.png` and
      `store\screenshot-2-viewer.png`, both 1400x820, taken from the demo
      library described below. Add more if you want; the minimum is 1366x768.
- [ ] **Description** — mention that the visual-search model is an optional
      351 MB download, so reviewers are not surprised by the first-run prompt.
- [ ] **Category** — Photo & video, or Productivity.

Expect a few days for a first submission to clear certification.


## Hosting the privacy policy

The policy lives at `privacy.html` in the repository root — a single
self-contained file, no assets and no build step. It is served from there by
GitHub Pages:

1. Settings > Pages > Source: **Deploy from a branch**, branch `main`, folder
   `/ (root)`.
2. The URL becomes
   `https://mryan1312.github.io/Magpie-Trove/privacy.html`.
3. Load it in a browser and confirm it renders before pasting it into Partner
   Center — they validate the link at submission.

Keep it reachable afterwards. If the URL later 404s, the listing can be pulled.

Edit the file in the root; do not keep a second copy anywhere, or the two will
drift and the hosted one will quietly go stale. Any static host works equally
well if you move off Pages — Cloudflare Pages, Netlify, Vercel — and a URL on
your own domain outlives all of them.

The policy's claims were verified against the code, not assumed: the only
network-capable type in the whole app is the single `HttpClient` in
`Services/ModelInstallService.cs`, and there is no telemetry, analytics, crash
reporting or third-party SDK anywhere. If that ever changes, the policy has to
change with it.


## The runFullTrust capability

Paste this into the restricted-capability justification field. It is routinely
approved for packaged desktop apps — the point of the text is to show a reviewer
that the capability is structural rather than elective, and to bound what the
app does with it.

```
Magpie Trove is a Win32 desktop application (WPF on .NET) distributed through
the MSIX packaging model. It declares runFullTrust because that capability is a
structural requirement of packaging a desktop application: the app's entry point
is Windows.FullTrustApplication, and a Win32 process cannot run inside the
restricted app container.

The capability is used for three things:

1. Reading image files from folders the user explicitly adds to their library
   through the app's "Add folders..." picker. The app indexes those images and
   writes its own database and thumbnail cache to its package-local application
   data. It does not enumerate or read anything the user has not chosen.

2. Loading the native components it depends on: ONNX Runtime, which analyses
   images locally on the user's own hardware, and SQLite, which stores the tag
   database. Both are ordinary redistributable libraries shipped inside the
   package. No image data leaves the device.

3. A single "Reveal in Explorer" command, which launches explorer.exe /select
   on the file the user currently has selected.

The application runs entirely as the standard user. It declares no elevation
manifest and never requests administrator rights. It installs no service, driver
or scheduled task, writes nothing to the registry, and runs no background
process outside its own window. It makes one outbound network request, to
huggingface.co, and only when the user explicitly chooses to download the
optional image-analysis model; that download is verified against a published
SHA-256 checksum before use. The app contains no telemetry, analytics or
advertising.
```

Every claim in that text was checked against the source, not assumed. If the app
changes, re-check these before reusing it:

| Claim | How it was verified |
|---|---|
| No elevation | No `requestedExecutionLevel` or application manifest anywhere |
| One process launch | `Process.Start` appears once, `explorer.exe /select` in `MainViewModel` |
| No registry, services, drivers, scheduled tasks | No `Registry.`, `ServiceController`, `TaskScheduler`, `CreateService` or `DeviceIoControl` |
| Native code only via packages | No `DllImport` in the source; native code arrives with ONNX Runtime and SQLite |
| One network endpoint | The only network-capable type is the single `HttpClient` in `Services/ModelInstallService.cs` |
| Checksum verified | `ModelInstallService.ExpectedSha256`, checked before the file is moved into place |

The wording says "the user's own hardware" rather than "the CPU" deliberately:
inference now runs on the GPU where DirectML is available, and falls back to the
CPU where it is not. See "GPU inference" below.


## GPU inference

`ClipEmbedder` asks for the DirectML execution provider and, since the move to
`Microsoft.ML.OnnxRuntime.DirectML`, actually gets it.

It did not before. The original build referenced plain
`Microsoft.ML.OnnxRuntime`, the CPU-only package, whose native library has no
DirectML entry point — so `AppendExecutionProvider_DML()` threw
`EntryPointNotFoundException`, an empty `catch` swallowed it, and every
embedding was computed on the CPU. The intent was in the code from the start;
only the package was wrong.

Measured on a GTX 1080 with the CLIP ViT-B/32 encoder, batch of 8:

| Provider | Per image | Throughput |
|---|---|---|
| CPU | 58.1 ms | 17.2 images/sec |
| GPU (DirectML) | 6.5 ms | 154.5 images/sec |

Roughly 9x, which on a 5,000-image library is about five minutes of "Analyse
images" down to about thirty seconds.

**The fallback was hardened at the same time.** DirectML can fail either when
the provider is registered or only when the session is built, on an unsupported
or broken driver. Originally only the registration call sat inside the
try/catch, so a driver-stage failure would have escaped as an unhandled
exception. `ClipEmbedder` now builds the session inside the same try and falls
back to a clean CPU session, so any DirectML failure degrades to CPU instead of
breaking analysis.

**On package size.** `Microsoft.AI.DirectML` comes in transitively and adds
`DirectML.dll` (17.7 MB) to the package, taking the MSIX from 69 to 79 MB. That
DLL is bundled rather than taken from Windows, so it does not raise the
manifest's `MinVersion`. The 2.1 MB `DirectML.Debug.dll` alongside it is
stripped at publish time by the `RemoveDirectMLDebugLayer` target in the
csproj — it is only loaded for `DML_CREATE_DEVICE_FLAG_DEBUG`, which nothing
here requests.

`ClipEmbedder.Provider` records which path was taken, but nothing in the UI
reads it. If you ever want to show users whether they are on GPU or CPU, that
value is already there.

Overstating any of this is the one way a straightforward approval turns into a
rejection, so keep it accurate rather than flattering.


## The demo library

Screenshots come from a throwaway library at `Libraries\Demo`, which is
gitignored along with the rest of `Libraries/`. It holds 19 wildlife photographs
taken from Wikimedia Commons, filtered to **public domain, CC0 or "no
restrictions" only** — anything under CC BY or CC BY-SA was skipped, so no
attribution is owed and the images are safe to publish in a Store listing.

It carries a 25-tag hierarchy (Animal > Bird > Eagle and so on), pinned tags on
keys 1-6, star ratings and a mix of pick and reject flags, so screenshots show
the app doing something rather than sitting empty. One image is deliberately
left untagged, which makes the "Untagged only" filter demonstrable.

To rebuild or extend it, the fixture scripts are in the session scratchpad
rather than the repo, since they are not part of the product. The library
itself is disposable: delete `Libraries\Demo` and the entry in
`%LOCALAPPDATA%\MagpieTrove\settings.json` to be rid of it.


## Things worth knowing

**User data moves under MSIX.** Confirmed by running the packaged build:
settings and the database land in

    %LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\MagpieTrove\

rather than `%LOCALAPPDATA%\MagpieTrove`. Both `Database._dataDirectory` and
`AppSettingsService.SharedDataDirectory` derive from
`SpecialFolder.LocalApplicationData`, and MSIX redirects that write
transparently. So the Store build will not see data created by the zip build.
Nothing is lost — the app supports libraries at arbitrary paths, so it is a
matter of re-adding the folder — but if you ship both channels, say so in the
listing.

A cosmetic consequence: the status bar tooltip shows the *logical* library path,
because that is what Windows reports to the app; for the default library the
file is really in the container. Anyone who copies that path into Explorer will
find nothing there. Harmless, but worth knowing if a user reports it.

**The rename migration.** `Services/LegacyMigration.cs` moves
`%LOCALAPPDATA%\Taggr` to `%LOCALAPPDATA%\MagpieTrove`, repoints the absolute
paths inside `settings.json`, and renames each library's `taggr.db` to
`magpietrove.db` as that library is opened. It only ever migrates into a clean
slate, so it cannot clobber newer data. Delete the class once no installation
can still be carrying the old layout — which, since nothing shipped under the
old name, is arguably already true; it is kept only for your own local profile.

**x64 only.** ARM64 machines will run this under emulation, which works but is
slower, and ONNX inference is where that will show. If ARM64 matters, publish a
second package with `-r win-arm64` and combine them into an `.msixbundle`.

**Do not bundle the model.** Keep it a runtime download. This was checked
properly, and bundling is not safe:

- Neither `Xenova/clip-vit-base-patch32` nor `openai/clip-vit-base-patch32`
  declares a licence on Hugging Face — the frontmatter has no `license:` field.
- The MIT licence on github.com/openai/CLIP covers the *code*. Whether it
  reaches the separately-hosted weights was asked in
  [issue #203](https://github.com/openai/CLIP/issues/203) in December 2021 and
  was never answered by OpenAI.

Redistribution is the act that needs a grant, and there isn't one to point at.
As things stand the app never redistributes: the user fetches the model from
Hugging Face themselves, on Hugging Face's terms. Bundling would turn that into
distributing someone else's artifact without a licence, to save a paragraph of
privacy policy. Not worth it.

If bundling ever becomes important, switch to
`laion/CLIP-ViT-B-32-laion2B-s34B-b79K`, which declares `license: mit`
explicitly. Same ViT-B/32 architecture and 512-dim output, but it needs an ONNX
export via Optimum, and embeddings from different weights are not
interchangeable — bump `ClipEmbedder.ModelId` so existing vectors are
invalidated rather than silently mixed, and expect every library to need
re-analysis.

Separately, note that both models' cards carry the line *"Any deployed use case
of the model — whether commercial or not — is currently out of scope."* That is
model-card guidance rather than a licence term, and shipping software uses CLIP
widely regardless, but it applies to this app either way and is worth knowing
about rather than being surprised by.

**Regenerating assets.** The tiles come from `Source/assets/magpietrove.ico` via
`New-Assets.ps1`. GDI+ cannot read that icon, so the script decodes it with the
WPF icon decoder and hands the pixels to System.Drawing; if you change the icon,
rerun the script rather than converting by hand.
