# Magpie Trove — recovered source

Originally shipped as **Taggr**. The source was lost and reconstructed from
`Bin/Taggr.dll` + `Bin/Taggr.pdb` using the ILSpy decompiler, then renamed to
Magpie Trove. **The project builds cleanly** (`dotnet build`, 0 errors, 2
warnings — both pre-existing fire-and-forget async calls, see below).

`Bin/` still holds the original Taggr binaries. They are the reference the
recovery was verified against; keep them.

## Layout

Mirrors the original, which is known exactly because the WPF resource names are
baked into the shipped assembly (`app.baml`, `themes/dark.baml`,
`views/mainwindow.baml`, ...):

    App.xaml / App.xaml.cs
    themes/Dark.xaml
    views/*.xaml + *.xaml.cs      7 windows
    Common/ Controls/ Converters/ Data/ Models/ Services/ ViewModels/
    Properties/AssemblyInfo.cs
    assets/magpietrove.ico        <Resource>, via /MagpieTrove;component/assets/magpietrove.ico
    app.ico                       <ApplicationIcon>

## How it was recovered

1. **C#** — `ilspycmd -p` run on Windows, where the WPF reference assemblies
   resolve. This matters: run on Linux without them, the decompiler cannot
   resolve `Window`, `Point`, `Key` and so on, and emits pseudo-code that looks
   plausible but is wrong (see below).
2. **XAML** — the `.baml` resources decompiled with ILSpy's
   `ICSharpCode.BamlDecompiler`, again with WPF references available so it could
   resolve the connection IDs in each window's generated `Connect()` method back
   into real `Name="..."` and `Click="..."` attributes. Without that step the
   XAML comes out with `<!--Unknown connection ID: N-->` where the wiring should
   be, which compiles to windows whose controls are never assigned and whose
   buttons do nothing.
3. The members the WPF markup compiler regenerates from XAML —
   `InitializeComponent`, `IComponentConnector.Connect`,
   `IStyleConnector.Connect`, `_CreateDelegate`, `_contentLoaded`, `App.Main`,
   and the `internal TextBlock Foo;` backing fields for every `Name=` — were
   stripped from the code-behind and the classes made `partial`. They come back
   at build time; leaving them in is a duplicate-member build error.

## Artifacts that were fixed

All decompiler output, not original code:

- `((Application)this).OnStartup(e)` inside `override OnStartup` →
  `base.OnStartup(e)`. As written that is a virtual call back into itself:
  StackOverflow at launch. Same for `OnExit`.
- `((Panel)this).InternalChildren` and similar → plain member access (18 sites).
- `((Point)(ref _offset)).Y` → `_offset.Y` (54 sites);
  `((Size)(ref val))._002Ector(a, b)` → `val = new Size(a, b)`;
  `base._002Ector()` (the implicit base constructor call, emitted explicitly)
  deleted.
- Raw `System.Windows.Input.Key` ordinals → the real enum. Note
  `(e.Key - 34).ToString()`: as decompiled that is `Key.ToString()` and yields an
  enum *name*, not a number — the rating shortcut would have passed `"Tab"`
  instead of `"3"`. The original is `(e.Key - Key.D0).ToString()`.
- `ImageMetadata` bound to `MagpieTrove.Services.ImageMetadata` instead of
  `System.Windows.Media.ImageMetadata` in three Services files, which also made
  the following `is BitmapMetadata` check permanently false.
- `GenerateNext(ref flag)` → `out`.
- Compiler-generated collection types (`<>z__ReadOnlyArray`,
  `<>z__ReadOnlySingleElementList`, `<>y__InlineArray4`) restored to the
  collection expressions they came from.
- Tuple element names ILSpy dropped from local declarations in
  `SuggestionService`, recovered from the lambda signatures that kept them.
- `ClipEmbedder.Std[2]`: ILSpy rendered CLIP's `0.27577711f` as
  `201f / (232f * (float)Math.PI)`. Bit-identical (`3E8D32A8`), but misleading,
  so it is written as the literal.
- One XAML escape: `StringFormat= {0:P0}` → `StringFormat={}{0:P0}`.

## Verified against the shipped binary

- **Public API surface is identical.** Every public type and method in
  `Bin/Taggr.dll` is present in the rebuild and vice versa. The only difference
  is `XamlGeneratedNamespace.GeneratedInternalTypeHelper`, a markup-compiler
  artifact whose visibility changed between PresentationBuildTasks 9.0.18
  (original) and 10.0.11 (this build).
- **WPF resource keys were identical** before the rename: `app.baml`,
  `themes/dark.baml`, `views/*.baml` (7), `assets/taggr.ico`.

## The rename

Taggr → Magpie Trove touched namespaces, assembly name, product metadata,
user-visible strings, the data directory and the database filename.
`Services/LegacyMigration.cs` handles existing installations: it moves
`%LOCALAPPDATA%\Taggr` to `%LOCALAPPDATA%\MagpieTrove`, repoints the absolute
paths inside `settings.json`, and renames each library's `taggr.db` to
`magpietrove.db` as that library is opened — checkpointing the write-ahead log
first so nothing is stranded in it. It only ever migrates into a clean slate, so
it cannot clobber newer data.

Verified against a copy of a real 23.9 MB library: 38 tags and 5,437 image
records survived intact.

The repository folder is still `E:\Dev\Taggr`. Only the product was renamed.

## Known remaining items

- `<Nullable>annotations</Nullable>` rather than `enable`. The source is
  `?`-annotated throughout, but decompilation does not reproduce the null-flow
  facts the original compiler had, so `enable` produces ~370 warnings (294 of
  them CS8600) that say nothing about the code. Flip it when someone wants to
  work through them.
- Two CS4014 fire-and-forget warnings, both faithful to the original:
  `Dispatcher.BeginInvoke` in `ThumbnailService`, and `RescanAsync()` called from
  a synchronous method in `MainViewModel`.
- `views/ViewerWindow.xaml.cs` `OnPreviewKeyDown` uses `goto`-based control flow
  where the original had a `switch`. Functionally equivalent, just hard to read.
- Targets `net10.0-windows`. The original targeted `net9.0`; it was retargeted
  because only the .NET 10 runtime is installed here.
- `SQLitePCLRaw.bundle_e_sqlite3` is pinned to 2.1.13, above the 2.1.11 that
  `Microsoft.Data.Sqlite` 10.0.10 pulls in, because 2.1.11 carries
  GHSA-2m69-gcr7-jv3q. Patch-level bump, same provider API.
- ONNX Runtime is the `Microsoft.ML.OnnxRuntime.DirectML` build rather than the
  plain package the original referenced. `ClipEmbedder` always asked for the
  DirectML execution provider, but against the CPU-only runtime that call threw
  and was swallowed, so inference silently ran on the CPU. With the right
  package it engages — about 9x faster on a GTX 1080. The fallback was widened
  at the same time so that a failure when the session is built, not just when
  the provider is registered, still degrades to CPU.

## Not recovered

`[assembly: InternalsVisibleTo("MagpieTrove.Tests")]` (originally
`"Taggr.Tests"`) means a test project existed. No corresponding DLL was found
anywhere under `E:\Dev\Taggr`, so it could not be recovered — the same process
would work on it if it turns up in a NuGet cache, a CI artifact, or on another
machine.
