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
    store/                      listing material (screenshots)
    layout/, out/               build output, safe to delete

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


## Submission checklist

- [ ] **Packages** — upload `out\MagpieTrove-*.msix`
- [ ] **`runFullTrust` justification.** A restricted capability; the form asks
      why you need it. Routinely approved for packaged desktop apps. Suggested
      wording: *"Magpie Trove is a packaged Win32 (WPF) desktop application. It
      requires full trust to read image files from folders the user explicitly
      adds to their library."*
- [ ] **Privacy policy URL** — **mandatory**, because the app makes a network
      call: `Libraries... > Download model` fetches the CLIP model from Hugging
      Face. Written already: `store\privacy.html`. It must be **hosted at a live
      public HTTPS URL** — Partner Center validates it at submission and
      re-checks later, and a dead link can get a published app pulled. GitHub
      Pages is the least-effort option; see "Hosting the privacy policy" below.
- [ ] **Age rating** — complete the IARC questionnaire.
- [ ] **Screenshots** — at least one, minimum 1366x768.
      `store\screenshot-1-placeholder.png` is a 1400x820 capture of an *empty*
      library; replace it with images that actually show the app in use.
- [ ] **Description** — mention that the visual-search model is an optional
      351 MB download, so reviewers are not surprised by the first-run prompt.
- [ ] **Category** — Photo & video, or Productivity.

Expect a few days for a first submission to clear certification.


## Hosting the privacy policy

`store\privacy.html` is a single self-contained file — no assets, no build step.
Any static host works. GitHub Pages is the usual choice because it is free,
HTTPS by default, and stays up:

1. Create a public repo (or use an existing one).
2. Put the file in it as `docs/privacy.html`, or as `privacy.html` on a
   `gh-pages` branch.
3. Settings > Pages > set the source to that branch/folder.
4. The URL becomes `https://<user>.github.io/<repo>/privacy.html` — paste that
   into Partner Center.

Cloudflare Pages, Netlify and Vercel all work the same way and are equally free.
If you have your own domain, better still: a URL you control outlives any host.

Keep it reachable once it is up. If the URL later 404s, the listing can be
pulled.

The policy's claims were verified against the code, not assumed: the only
network-capable type in the whole app is the single `HttpClient` in
`Services/ModelInstallService.cs`, and there is no telemetry, analytics, crash
reporting or third-party SDK anywhere. If that ever changes, the policy has to
change with it.


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
