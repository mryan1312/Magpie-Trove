Magpie Trove 1.0.0  —  Windows x64
===========================

A local image tagging and search tool. Everything runs on your machine;
nothing is uploaded anywhere.


GETTING STARTED
---------------

1. Unzip MagpieTrove.exe wherever you like. There is no installer and nothing is
   written to Program Files or the registry.

2. Run MagpieTrove.exe.

   Windows may show "Windows protected your PC" the first time, because this
   build is not code-signed. Click "More info", then "Run anyway".

   The first launch takes a few seconds longer than later ones — the app
   unpacks itself into your temp folder once.

3. Click "Add folders..." and pick a folder of images.


REQUIREMENTS
------------

64-bit Windows 10 or 11. Nothing else — the .NET runtime is built into the
executable, so there is no separate runtime to install.


THE VISUAL SEARCH MODEL (optional, 351 MB)
------------------------------------------

Magpie Trove works out of the box for browsing, folders, tags, ratings, flags,
duplicate detection and export.

One group of features needs a machine-learning model that is too large to
ship inside the download: "visual search" — finding lookalike images and
getting tag suggestions. Until the model is installed, the VISUAL SEARCH
panel stays hidden and everything else behaves normally.

To install it:

  Libraries...  ->  set "Model directory" if you want it somewhere specific
                ->  click "Download model"

That fetches about 351 MB (the CLIP ViT-B/32 vision encoder) from Hugging
Face, checks it against a published SHA-256 checksum, and only then puts it
in place. A partial or corrupted download is discarded rather than used. You
can cancel mid-download and resume later, and it is a one-time step.

If someone has already given you the model file (clip-vit-b32-vision.onnx),
just drop it in the model directory instead and skip the download.

After the model is installed, click "Analyse images" in the VISUAL SEARCH
panel to build fingerprints for your library.


WHERE YOUR DATA LIVES
---------------------

  %LOCALAPPDATA%\Magpie Trove\
      settings.json     your preferences and library list
      magpietrove.db          the default library's database
      thumbnails\       the thumbnail cache
      models\           the visual search model, if installed

You can keep several libraries in different folders — see "Libraries...".
Your image files are never moved or modified unless you explicitly use the
export or transfer tools.


UNINSTALLING
------------

Delete MagpieTrove.exe. To remove your tags, ratings and cache as well, also delete
the %LOCALAPPDATA%\Magpie Trove folder. Your images are untouched either way.
