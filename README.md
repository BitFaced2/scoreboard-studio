# Scoreboard Studio

A Windows desktop prototype that crops a real scoreboard camera feed into live, transparent video fields for a vMix scorebug. It preserves the physical scoreboard's digits. It does not read or regenerate scores and uses no OCR, AI model, cloud service, or paid image-processing library.

## Run

Open `artifacts/ScoreboardStudio/ScoreboardStudio.exe` after publishing. The self-contained build includes .NET; it does not require a separate .NET installation. It starts with a clearly labeled, moving demo scoreboard and six sample fields. Outputs start **off**.

The public repository includes the synthetic demo and a ten-field example layout. The real reference photo and original planning images are excluded. To use **Open real scoreboard sample**, place the matching reference photo at `samples/real-scoreboard/scoreboard.png` before building. The sample button is available only when that photo is present. For your own photos, use **Open scoreboard image** and create or adjust the field boxes.

When present, sample files are copied to the `samples` folder next to the executable; keep that folder with the app. Saving the sample prompts for a new setup filename. Image setups remember a relative image path, so loading them restores the still image as well as the field settings.

## Angled cameras

In **Fields**, click **Straighten camera**, then drag the four numbered points to the scoreboard's corners in order: top left, top right, bottom right, bottom left. Choose the corrected image width and height to match the board's proportions, and click **Apply**. **Cancel** keeps the previous correction; **Reset correction** returns to the original camera geometry.

This is a four-point projective correction with bilinear pixel sampling. It straightens a flat scoreboard viewed at an angle before detection, cropping, and cleanup. It does not read any digits. Corners and corrected dimensions are saved with the layout. Invalid/crossed corners are rejected. Opening the corner editor stops NDI outputs, and starting outputs is blocked until the edit is applied or canceled. Review/reposition your field boxes after changing the correction, then start outputs explicitly.

The correction is fixed, not camera tracking. If the camera moves, realign it. It does not correct lens distortion or recover detail missing from the camera image.

## Bright digits on a glowing panel

Use **Bright colors** for saturated red/amber LEDs against a dark background. Use **White highlights** when digit cores are white but the surrounding panel has a strong colored glow, as in the supplied photo. Adjust cutoff and softness per field. The real-photo sample uses the latter mode; its crops and settings were prepared manually and are not an assertion that automatic detection understands each field.

Automatic detection has its own **Look for** mode and cutoff. White-highlight mode first looks for red/amber display panels and separated digit groups, then falls back to bright-region grouping if too few panels are found. These are editable suggestions: labels, indicators, glare, or adjacent windows can still create unwanted/merged boxes. Accepted suggestions inherit the detection mode and cutoff.

For NDI input and output, an NDI runtime must be installed. The app checks the standard vMix installation directory, standard NDI runtime folders, and the `NDI_RUNTIME_DIR_V6` / `NDI_RUNTIME_DIR_V5` environment variables. For a custom installation, set `SCOREBOARD_NDI_RUNTIME` to the full 64-bit runtime DLL path and restart. The application does not copy or redistribute vMix's runtime.

## Use with vMix

1. Make the scoreboard camera feed available as an NDI output in vMix. The camera feed must already have reached the studio; this app does not transport the remote game's feed to the studio.
2. Choose that NDI source in the left sidebar and click **Connect**. Discovery refreshes periodically; **Refresh** scans immediately.
3. If the camera is angled, use **Straighten camera** first. Draw fields manually, or click **Auto-detect fields**. Amber boxes are suggestions only. **Use suggestions** replaces the field list; **Undo detection** restores the previous list. Name each field yourself.
4. Move a field by dragging inside its box; resize from its lower-right square. Exact crop positions are also editable as percentages. Leave room for every possible digit, including scores that gain a digit.
5. Select a field and use **Cleanup** to compare its original crop with the transparent result. Adjust cutoff, softness, brightness, color, thickness, smoothing, and speckle removal. Settings are independent per field.
6. Save your setup. Layouts include perspective, field names, normalized crops, cleanup settings, output enablement, and the source name or still-image path. They do not embed images or GTZIP artwork. Loading a saved NDI layout requires connecting the source again, and never starts outputs automatically.
7. Click **Start NDI outputs**. In vMix, add each `Scoreboard - <field name>` NDI source as an input and position those inputs as layers over your existing scorebug/GT title. The machine name is added by NDI. Demo and still-image outputs explicitly contain `DEMO` or `STILL` in their names.
8. Use **Lock for live** to protect source/crop/cleanup controls. You can still stop outputs. Unlock to edit again.

If no new NDI frame arrives for 1.5 seconds, the app displays **NO SIGNAL**, dims the source preview, and sends transparent fields. It resumes when video returns. This detects missing frames, not a camera or upstream encoder repeatedly transmitting a frozen image. Stopping outputs sends a transparent frame before closing the senders.

## Current limits

- Early prototype, verified with the supplied real photo, synthetic geometry/video, and local NDI loopback. It still needs testing against a live game camera and the intended vMix scorebug.
- Detection groups bright pixels and warm display panels. It does not recognize field meanings or track camera movement. Reflections and labels can produce extra/merged suggestions. Crops remain fixed until edited.
- Background removal uses either the brightest RGB channel or the dimmest channel for white-highlight isolation. Complex lighting may require manual adjustments. It cannot restore missing LED segments, lost detail, occlusions, or severe blur.
- Up to 24 fields; independent NDI streams at a target of 30 fps. No audio. Throughput depends on crop size, processing controls, network, and available CPU. Source frames may be skipped to keep latency bounded.
- Output widths are padded to an even number of pixels for NDI compatibility. Alpha is straight, not premultiplied.
- GTZIP files stay in vMix. There is no GTZIP importer, scorebug renderer, automatic vMix layer setup, or remote venue transport in this version.
- Image import supports PNG/JPEG/BMP for setup. Recorded video-file playback is not implemented.
- This build is a portable application. A signed EXE/MSI installer, optional desktop shortcut, and installer upgrade flow remain release work.

## Development

Requires the .NET 8 SDK on Windows. There are no NuGet package dependencies.

Clone this repository with `git clone https://github.com/BitFaced2/scoreboard-studio.git`, then run the following commands from the repository folder. Build outputs are generated locally and are not stored in Git.

```powershell
dotnet build ScoreboardStudio.sln -c Release
dotnet run --project src/ScoreboardStudio.App
dotnet run --project tests/ScoreboardStudio.Checks -c Release
dotnet run --project tests/ScoreboardStudio.Checks -c Release -- --ndi
dotnet run --project tests/ScoreboardStudio.Checks -c Release -- --image samples/real-scoreboard/scoreboard.png samples/real-scoreboard/real-scoreboard.scoreboard.json artifacts/real-scoreboard-checks
powershell -ExecutionPolicy Bypass -File scripts/Build.ps1
```

The normal console checks require neither NDI nor the reference photo. `--ndi` additionally exercises an installed runtime with actual discovery, receive, cleaned output, alpha, signal loss, and recovery. `--image` requires the optional reference photo and produces a comparison sheet, detection overlay, and perspective example. The WPF executable also supports `--smoke-test <output-directory>` to exercise field selection/rename/crop/cleanup and render the work areas to PNG. When the reference photo is present, it also loads the real sample and verifies perspective apply/cancel/reset. These tests publish temporary local NDI sources only when `--ndi` is explicitly used.

The `Core` project contains pixels, detection, layout persistence, the worker pipeline, and the NDI native bindings. The `App` project is the WPF desktop editor. `Checks` is a dependency-free console verification harness.

Native integration follows the official [NDI receive](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-recv), [send](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-send), [discovery](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-find), and [frame-format](https://docs.ndi.video/all/developing-with-ndi/sdk/frame-types) documentation. vMix documents [NDI transparency](https://download.vmix.com/help29/NDIAlphaChannel.html) and [input layers](https://www1.vmix.com/help29/InputSettingsMultiView.html).
