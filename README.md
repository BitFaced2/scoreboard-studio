# TMS Scoreboard Studio

A Windows desktop prototype that crops a real scoreboard camera feed into live, transparent video fields for a vMix scorebug. It preserves the physical scoreboard's digits. It does not read or regenerate scores and uses no OCR, AI model, cloud service, or paid image-processing library.

The desktop interface uses Total Media Sports branding, with the supplied logo embedded in the application, blue action buttons, and red navigation accents. Existing setup files and NDI source names remain compatible.

## Run

Download the Windows ZIP from [GitHub Releases](https://github.com/BitFaced2/scoreboard-studio/releases), extract it, and open `ScoreboardStudio.exe`. The self-contained build includes .NET. The app starts with no source or fields, and outputs stopped. The illustrated PDF user guide is included in the ZIP and available as a separate release download.

Choose **Load image or video** to select your own PNG, JPEG, BMP or recorded video. There is no built-in demo or sample-media button, and release packages contain no sample media or layouts. Use **Pause / Play**, **Restart**, and **Loop video** for recordings. Audio is muted. Windows supplies the video decoder; H.264 MP4 is the recommended format. Other offered video containers depend on the codecs installed on the machine. Frames are fitted within 1280 x 720 while preserving aspect ratio for processing.

For a live feed, select an NDI source and click **Connect**. The dropdown lists NDI feeds only; USB cameras and capture cards must first be exposed as NDI through vMix or another sender. An empty list does not affect local file playback. NDI input/output requires an installed runtime; local image/video processing does not. The app checks the standard vMix runtime location automatically.

Fields are placed only by the operator: draw them manually or click **Find fields**, inspect the amber suggestions, then click **Replace fields with suggestions**. Loading, reconnecting, camera movement, and video looping never replace your boxes. Pending detection results are discarded if you edit the setup. Setups remember a relative image or video path so they can restore the source. Media is referenced, not embedded: copy it along with the setup when moving computers. Loading a setup never starts outputs automatically. A paused video, or a video that reaches the end with Loop disabled, holds its last frame until you resume or restart.

The TMS PDF user guide covers the internal workflow. Source test fixtures in `samples` are developer checks and are not included in the application package.

## Angled cameras

In **Fields**, click **Straighten camera**, then drag the four numbered points to the scoreboard's corners in order: top left, top right, bottom right, bottom left. Choose the corrected image width and height to match the board's proportions, and click **Apply**. **Cancel** keeps the previous correction; **Reset correction** returns to the original camera geometry.

This is a four-point projective correction with bilinear pixel sampling. It straightens a flat scoreboard viewed at an angle before detection, cropping, and cleanup. It does not read any digits. Corners and corrected dimensions are saved with the layout. Invalid/crossed corners are rejected. Opening the corner editor stops NDI outputs, and starting outputs is blocked until the edit is applied or canceled. Review/reposition your field boxes after changing the correction, then start outputs explicitly.

The correction is fixed, not camera tracking. If the camera moves, realign it. It does not correct lens distortion or recover detail missing from the camera image.

Straightening retains existing field boxes; cutting a display out of the view does not delete its fields. Select unwanted fields under **Your fields** and click **Remove selected field**, then resize the remaining boxes. Alternatively, use **Find fields → Replace fields with suggestions** to rebuild the field list for the corrected image. Save the updated setup afterward.

The corner editor freezes the picture while you work. Drag a numbered dot or its label directly; no preliminary click is needed. Handles retain a usable size as the preview scales. Click a corner and use the arrow keys for one-pixel adjustments, or Shift plus arrows for ten pixels. Apply resumes the current picture.

## Camera stabilization and reliability

After framing and setting up the board, click **Stabilize camera**. This captures a fixed reference and corrects small shifts, rotation, and zoom before perspective correction and cropping. It does not move or recreate the field boxes. Review alignment and start outputs explicitly. The tracker requires visible detail around the board; it is intended for small camera disturbances, not following a moving camera around a venue.

If tracking is lost, the status reports it and the outputs become transparent. Returning the camera to the reference view allows recovery. For a deliberate reposition, stop stabilization, realign the perspective and fields, then enable it again. **Set reference** explicitly captures the current view and stops outputs for review. References are session-only; loading a setup or changing sources disables stabilization until the operator sets a new reference.

The app has standard Windows minimize/maximize/resize controls and additional minimize/restore buttons in its header. Startup fits the screen's available work area; smaller windows scroll to keep controls reachable. Recorded playback reuses its render surface, previews reuse bitmap buffers, and unchanged frames reuse processed results.

Diagnostics are written to `%LOCALAPPDATA%\TMS Scoreboard Studio\Diagnostics\app.log`, with a bounded previous log. On a handled UI crash, the app attempts to save a timestamped recovered setup there before closing. Media files remain external. Native decoder or process termination failures may not allow setup recovery.

## Bright digits on a glowing panel

Use **Bright colors** for saturated red/amber LEDs against a dark background. Use **White highlights** when digit cores are white but the surrounding panel has a strong colored glow, as in an overexposed scoreboard photo. Adjust cutoff and softness per field. Review each crop manually; automatic detection does not understand field meanings.

Automatic detection has its own **Look for** mode and cutoff. White-highlight mode first looks for red/amber display panels and separated digit groups, then falls back to bright-region grouping if too few panels are found. These are editable suggestions: labels, indicators, glare, or adjacent windows can still create unwanted/merged boxes. Accepted suggestions inherit the detection mode and cutoff.

For NDI input and output, an NDI runtime must be installed. The app checks the standard vMix installation directory, standard NDI runtime folders, and the `NDI_RUNTIME_DIR_V6` / `NDI_RUNTIME_DIR_V5` environment variables. For a custom installation, set `SCOREBOARD_NDI_RUNTIME` to the full 64-bit runtime DLL path and restart. The application does not copy or redistribute vMix's runtime.

## Use with vMix

1. Make the scoreboard camera feed available as an NDI output in vMix. The camera feed must already have reached the studio; this app does not transport the remote game's feed to the studio.
2. Choose that NDI source in the left sidebar and click **Connect**. Discovery refreshes periodically; **Refresh** scans immediately.
3. If the camera is angled, use **Straighten camera** first. Draw fields manually, or click **Find fields**. Amber boxes are suggestions only. **Replace fields with suggestions** replaces the field list; **Undo detection** restores the previous list. Name each field yourself.
4. Move a field by dragging inside its box; resize from its lower-right square. Exact crop positions are also editable as percentages. Leave room for every possible digit, including scores that gain a digit.
5. Select a field and use **Cleanup** to compare its original crop with the transparent result. Adjust cutoff, softness, brightness, color, thickness, smoothing, and speckle removal. Settings are independent per field.
6. Save your setup. Layouts include perspective, field names, normalized crops, cleanup settings, output enablement, and the source name or image/video path. They do not embed media or GTZIP artwork. Loading a saved NDI layout requires connecting the source again, and never starts outputs automatically.
7. Click **Start NDI outputs**. In vMix, add each `Scoreboard - <field name>` NDI source as an input and position those inputs as layers over your existing scorebug/GT title. The machine name is added by NDI. Still-image outputs explicitly contain `STILL` in their names.
8. Use **Lock for live** to protect source/crop/cleanup controls. You can still stop outputs. Unlock to edit again.

If no new NDI frame arrives for 1.5 seconds, the app displays **NO SIGNAL**, dims the source preview, and sends transparent fields. It resumes when video returns. This detects missing frames, not a camera or upstream encoder repeatedly transmitting a frozen image. Stopping outputs sends a transparent frame before closing the senders.

## Current limits

- Early prototype, verified with the supplied real photo, synthetic geometry/video, and local NDI loopback. It still needs testing against a live game camera and the intended vMix scorebug.
- Detection groups bright pixels and warm display panels only when requested. It does not recognize field meanings. Reflections and labels can produce extra/merged suggestions. Stabilization separately tracks a fixed camera reference; crops remain fixed until edited.
- Background removal uses either the brightest RGB channel or the dimmest channel for white-highlight isolation. Complex lighting may require manual adjustments. It cannot restore missing LED segments, lost detail, occlusions, or severe blur.
- Up to 24 fields; independent NDI streams at a target of 30 fps. No audio. Throughput depends on crop size, processing controls, network, and available CPU. Source frames may be skipped to keep latency bounded.
- Output widths are padded to an even number of pixels for NDI compatibility. Alpha is straight, not premultiplied.
- GTZIP files stay in vMix. There is no GTZIP importer, scorebug renderer, automatic vMix layer setup, or remote venue transport in this version.
- Image import supports PNG/JPEG/BMP. Local video uses Windows media decoding, with playback controls and automatic looping. Unsupported codecs report an error; no decoder package or FFmpeg executable is bundled.
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
powershell -ExecutionPolicy Bypass -File scripts/Package-Release.ps1 -VerifyReferencePhoto -VerifyNdi
```

The normal console checks require neither NDI nor the reference photo. `--ndi` additionally exercises an installed runtime with actual discovery, receive, cleaned output, alpha, signal loss, and recovery. `--image` requires the optional reference photo and produces a comparison sheet, detection overlay, and perspective example. The WPF executable also supports `--smoke-test <output-directory>` to exercise field selection/rename/crop/cleanup and render the work areas to PNG. Pass `--test-layout <photo-layout.json>` to supply a photo fixture for field editing and perspective apply/cancel/reset. `--video-smoke-test <directory> --video-file <clip.mp4> --test-layout <layout.json> --ndi` checks direct video decoding, pause/restart/loop, persistence, NDI output, source switching and invalid media; omit `--ndi` when no runtime is available. These tests publish temporary local NDI sources only when `--ndi` is explicitly used.

The `Core` project contains pixels, detection, layout persistence, the worker pipeline, and the NDI native bindings. The `App` project is the WPF desktop editor. `Checks` is a dependency-free console verification harness.

Native integration follows the official [NDI receive](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-recv), [send](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-send), [discovery](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-find), and [frame-format](https://docs.ndi.video/all/developing-with-ndi/sdk/frame-types) documentation. vMix documents [NDI transparency](https://download.vmix.com/help29/NDIAlphaChannel.html) and [input layers](https://www1.vmix.com/help29/InputSettingsMultiView.html).
