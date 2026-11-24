# TMS Scoreboard Studio

A Windows desktop prototype that crops a real scoreboard camera feed into live, transparent video fields for a vMix scorebug. It preserves the physical scoreboard's digits. The scoreboard processing does not read or regenerate scores and uses no OCR, AI model, cloud service, or paid image-processing library.

The desktop interface uses Total Media Sports branding, with the supplied logo embedded in the application, blue action buttons, and red navigation accents. Older setups retain their separate NDI outputs when loaded. New sessions default to one combined field output.

## Run

Download the Windows ZIP from [GitHub Releases](https://github.com/BitFaced2/scoreboard-studio/releases), extract it, and open `ScoreboardStudio.exe`. The self-contained build includes .NET. The app starts with no source or fields, and outputs stopped. The illustrated PDF user guide is included in the ZIP and available as a separate release download.

Choose **Load image or video** to select your own PNG, JPEG, BMP or recorded video. There is no built-in demo or sample-media button, and release packages contain no sample media or layouts. Use **Pause / Play**, **Restart**, and **Loop video** for recordings. Audio is muted. Windows supplies the video decoder; H.264 MP4 is the recommended format. Other offered video containers depend on the codecs installed on the machine. Frames are fitted within 1280 x 720 while preserving aspect ratio for processing.

For a live feed, select an NDI source and click **Connect**. The dropdown lists NDI feeds only; USB cameras and capture cards must first be exposed as NDI through vMix or another sender. An empty list does not affect local file playback. NDI input/output requires an installed runtime; local image/video processing does not. The app checks the standard vMix runtime location automatically.

The first source in a fresh app session automatically gets an initial set of field boxes. That pass runs once; afterward, boxes stay fixed until you edit them. Use **+ Draw a field** to drag out another box, or select one and click **Remove selected field**. Editing, deleting all fields, reconnecting, camera movement, and video looping never restart automatic placement. Loading a saved layout, including an empty one, skips initial detection. Pending results are discarded if you begin editing. To explicitly rescan later, click **Find fields**, inspect the amber suggestions, then click **Replace fields with suggestions**.

Setups remember a relative image or video path so they can restore the source. Media is referenced, not embedded: copy it along with the setup when moving computers. Loading a setup never starts outputs automatically. A paused video, or a video that reaches the end with Loop disabled, holds its last frame until you resume or restart.

The TMS PDF user guide covers the internal workflow. Source test fixtures in `samples` are developer checks and are not included in the application package.

## Combined field output

In **Outputs**, choose **Single Input**. Use **Load scorebug guide** to load a GTZIP or PNG/JPEG/BMP as an editor-only positioning guide. The GT title stays in vMix; guide artwork never enters the NDI feed. The supplied Tipoff title uses a 1920 × 1080 canvas with eight value targets. The static GT guide reader supports its rectangles, ellipses, embedded logos, and labels; it is not a complete GT animation/rendering engine. GT's [Data Hidden flag](https://help.vmix.com/graphics/2/Data.html) hides an object from the Title Editor, so those static labels remain visible in the guide.

New fields automatically receive an initial position on the combined canvas. Adding a field uses open space without rearranging existing boxes. Select a field on the left to edit it or choose a target in **Output position**. Deliberately unplaced fields stay unplaced across save/reload; **Place selected field** restores them. **Match field names to guide** explicitly places matching names (including Game clock → Period1 for Tipoff). Source crops remain unchanged. Drag a box to move it, drag any of its four round corner pins to resize it, use arrow keys to nudge, or adjust the live position/size sliders. Close-up changes only the editor view. New fields default to **Stretch to box**; **Keep proportions** is also available, and Digit appearance controls color in both output modes.

Only enabled, placed fields are included. **Unplace** removes a field from the combined canvas without deleting its source crop. **Bring to front** changes overlap order. Layouts save output mode, canvas size, each field's placement/fit/color, and a relative guide path. Missing guides do not discard saved positions. New saves use layout version 2 and require 0.6.0 or newer; version 1 layouts still load in legacy mode. Keep the referenced guide and footage with the setup when moving computers.

Add **Scoreboard - Combined** as one NDI input in vMix (still-image testing uses **Scoreboard STILL - Combined**). Layer the full canvas over your GT title with identical position/scale; do not crop the transparent borders independently. Clear the GT title's numeric placeholder text to prevent double digits. Your GT title and any edited copies are separate files, not included in the download. Individual fields remain editable in Scoreboard Studio; team artwork and other title content stay editable in vMix. The field overlay does not follow GT object animations; use a transition on the completed composition when both must move together.

Changing output mode or canvas size stops outputs for review. Live lock protects output placement. A real source outage clears the whole field canvas; stabilization uncertainty keeps current pixels flowing with the existing alignment warning. Choose **Separate Inputs** when an existing vMix preset still expects one input per field.

## Updates

Version 0.5.0 adds **Check for updates** in the bottom bar. Older versions need one final manual download: extract the new Windows ZIP and open its executable. This is a portable app; no uninstall or installer is required.

Stop NDI outputs, unlock editing, then choose **Check for updates → Download update → Install and restart**. The app checks this repository's published releases (including previews), verifies the ZIP's SHA-256 digest from GitHub, and stages the new files before closing. Unsaved changes trigger the normal save prompt. It replaces only program files in the existing folder, so existing shortcuts keep working and saved layouts/media remain untouched. The app restarts into your saved setup with outputs stopped; reconnect the live source and set stabilization again if needed.

Failed downloads leave the installed app unchanged. Installation backs up replaced files and rolls back ordinary write failures; the backup is removed after a successful restart. The folder must be writable, and other copies of the same app must be closed. Error details go to the Diagnostics folder described below. An interrupted process or power loss may require restoring the retained `.update-backup-*` folder manually.

Checks occur only when requested. The updater contacts GitHub to fetch release metadata and the Windows ZIP; it does not upload footage or setups, download sample video, or require a GitHub login. Network or rate-limit errors can be retried, and the update window also links to manual GitHub downloads. It uses GitHub's official [release listing](https://docs.github.com/en/rest/releases/releases#list-releases) and [release asset metadata](https://docs.github.com/en/rest/releases/assets#get-a-release-asset).

## Angled cameras

In **Fields**, expand **Camera setup** and click **Keystone Correction**, then drag the four numbered points to the scoreboard's corners in order: top left, top right, bottom right, bottom left. Choose the corrected image width and height to match the board's proportions, and click **Apply**. **Cancel** keeps the previous correction; **Reset correction** returns to the original camera geometry.

This is a four-point projective correction with bilinear pixel sampling. It straightens a flat scoreboard viewed at an angle before detection, cropping, and cleanup. It does not read any digits. Corners and corrected dimensions are saved with the layout. Invalid/crossed corners are rejected. Opening the corner editor stops NDI outputs. Applying or resetting correction automatically re-detects field positions and count once in the selected view. Matching fields retain their names, cleanup settings, and output placement. **Field detection > Undo correction** restores the previous view and boxes together. Cancel leaves them unchanged. Review the detected boxes, then start outputs explicitly; saving, live lock, and output startup wait for the rescan to finish. Correction resets stabilization, so enable it again after reviewing the new view. Playback and stabilization do not repeat detection.

The correction is fixed, not camera tracking. If the camera moves, realign it. It does not correct lens distortion or recover detail missing from the camera image.

The rescan replaces the field list with the displays detected in the selected area, so excluding a duplicate board also removes its fields. Detection is approximate: review the full digit areas, use **Remove selected field** for unwanted boxes and **+ Draw a field** for anything missed. Save the updated setup afterward.

The corner editor freezes the picture while you work. Drag a numbered dot or its label directly; no preliminary click is needed. Handles retain a usable size as the preview scales. Click a corner and use the arrow keys for one-pixel adjustments, or Shift plus arrows for ten pixels. Apply resumes the current picture.

## Camera stabilization and reliability

After framing and setting up the board, click **Stabilize camera**. This captures a fixed reference and corrects small shifts, rotation, and zoom before perspective correction and cropping. It does not move or recreate the field boxes. Review alignment and start outputs explicitly. The tracker requires visible detail around the board; it is intended for small camera disturbances, not following a moving camera around a venue.

If tracking is lost, live pixels continue through the last trusted correction and the app displays **CHECK ALIGNMENT**. NDI outputs stay connected; the app does not freeze an old clock image. Alignment is uncertain until tracking recovers, so review the scorebug and stop outputs if the crops are wrong. An initial reference without enough detail passes current video through without correction. Actual source loss still clears outputs normally.

The tracker searches around its last trusted position and uses a wider recovery search after larger shifts, retaining the original reference and fixed field boxes. It can recover when enough of the reference is visible again; blur, occlusion, severe rotation, or a board outside the view can still prevent alignment. For a deliberate reposition, stop stabilization, realign the perspective and fields, then enable it again. **Set reference** explicitly captures the current view and stops outputs for review. References are session-only; loading a setup or changing sources disables stabilization until the operator sets a new reference.

The app has standard Windows minimize/maximize/resize controls and additional minimize/restore buttons in its header. The app starts maximized; smaller windows scroll to keep controls reachable. Recorded playback reuses its render surface, previews reuse bitmap buffers, and unchanged frames reuse processed results.

Diagnostics are written to `%LOCALAPPDATA%\TMS Scoreboard Studio\Diagnostics\app.log`, with a bounded previous log. On a handled UI crash, the app attempts to save a timestamped recovered setup there before closing. Media files remain external. Native decoder or process termination failures may not allow setup recovery.

The app has two tabs: **Fields** for camera setup and source crops, and **Outputs** for appearance, placement, and NDI configuration. The left panel separates **NDI source**, **Media file**, and **Your fields**. In Fields, expand **Camera setup** for straightening and stabilization, or **Field detection** for detection options. In Outputs, expand **Before / After** below the output preview to compare the selected field's original crop and cleaned digits live; it works in both output modes and stacks vertically in narrow windows. The comparison shows cleanup before output sizing. **Output mode** sits beside the preview checkboxes and wraps below them in narrow windows. **Scorebug guide and canvas**, **Layout actions**, and **Connect to vMix** hold secondary tools and instructions. These groups start collapsed; controls wrap or scroll in smaller windows.

The right panel groups controls under **Field settings**, **Crop position**, **Output position**, **Background removal**, **Digit appearance**, and **Edges and detail**. All sections start collapsed. Click a heading to expand or collapse it. Output sections appear in Single Input mode on the Outputs tab. Open/closed choices stay in place when switching fields or tabs during the session.

**Apply cleanup to all fields** and **Reset cleanup** stay visible at the bottom of the right panel, outside the scrolling sections. Select a field to use them; Live lock disables edits.

Crop sliders show percentages; output-position sliders show pixels. Changes apply immediately. Arrow keys adjust crop by 0.1 percentage point or output position by 1 pixel; Page Up/Down use larger steps. Slider ranges keep the box within its image/canvas, preserving its size when moving. Dragging boxes and selecting fields refresh the sliders without changing saved geometry. Live lock disables edits.

## Color picker and editor layout

**Preview background** above the output canvas offers Checkerboard, Dark, Light, and Custom. **Choose color...** opens the color wheel. This viewing preference applies to the combined canvas, separate-input cards, and cleaned After preview for the current session. NDI output remains transparent.

Use **Choose digit color** for a color wheel, hue/saturation/lightness sliders, preset swatches and a preview. **Use color** applies the shade at 1x digit brightness so the chosen color appears in both previews. Cancel leaves settings unchanged. Raising brightness afterward can wash out lighter shades.

**Digit appearance** is the single color control for both output modes. Older setups ignore their separate output-color override and use the saved digit appearance. The inspector has live crop and output-position sliders, dark dropdowns and scrollbars, and adjusts for smaller windows.

## Bright digits on a glowing panel

Use **Bright colors** for saturated red/amber LEDs against a dark background. Use **White highlights** when digit cores are white but the surrounding panel has a strong colored glow, as in an overexposed scoreboard photo. Adjust cutoff and softness per field. Review each crop manually; automatic detection does not understand field meanings.

Automatic detection has its own **Look for** mode and cutoff. White-highlight mode first looks for red/amber display panels and separated digit groups, then falls back to bright-region grouping if too few panels are found. These are editable suggestions: labels, indicators, glare, or adjacent windows can still create unwanted/merged boxes. Accepted suggestions inherit the detection mode and cutoff.

For NDI input and output, an NDI runtime must be installed. The app checks the standard vMix installation directory, standard NDI runtime folders, and the `NDI_RUNTIME_DIR_V6` / `NDI_RUNTIME_DIR_V5` environment variables. For a custom installation, set `SCOREBOARD_NDI_RUNTIME` to the full 64-bit runtime DLL path and restart. The application does not copy or redistribute vMix's runtime.

## Use with vMix

1. Make the scoreboard camera feed available as an NDI output in vMix. The camera feed must already have reached the studio; this app does not transport the remote game's feed to the studio.
2. Choose that NDI source in the left sidebar and click **Connect**. Discovery refreshes periodically; **Refresh** scans immediately.
3. Review the initial automatic boxes. If the camera is angled, use **Keystone Correction**, then adjust the boxes. Add missing fields with **+ Draw a field**. For an explicit rescan, click **Find fields**; amber boxes are suggestions only. **Replace fields with suggestions** replaces the field list; **Undo detection** restores the previous list. Name each field yourself.
4. Move a field by dragging inside its box; resize from any of its four round corner pins. Exact crop positions are also editable as percentages. Leave room for every possible digit, including scores that gain a digit.
5. Select a field and open **Outputs > Before / After** to compare its original crop with the transparent result. Use the right-hand background removal, digit appearance, and edges sections to adjust cutoff, softness, brightness, color, thickness, smoothing, and speckle removal. Settings are independent per field.
6. Save your setup. Layouts include perspective, field names, normalized crops, cleanup settings, output enablement, and the source name or image/video path. They do not embed media or GTZIP artwork. Loading a saved NDI layout requires connecting the source again, and never starts outputs automatically.
7. In **Outputs**, review and adjust the automatically placed fields, then click **Start NDI outputs**. In vMix, add `Scoreboard - Combined` and layer its full canvas over your GT title. Legacy mode still sends each `Scoreboard - <field name>` independently. The machine name is added by NDI. Still-image outputs explicitly contain `STILL` in their names.
8. Use **Lock for live** to protect source/crop/cleanup controls. You can still stop outputs. Unlock to edit again.

If no new NDI frame arrives for 1.5 seconds, the app displays **NO SIGNAL**, dims the source preview, and sends transparent fields. It resumes when video returns. This detects missing frames, not a camera or upstream encoder repeatedly transmitting a frozen image. Stopping outputs sends a transparent frame before closing the senders.

## Current limits

- Early prototype, verified with the supplied real photo, synthetic geometry/video, and local NDI loopback. It still needs testing against a live game camera and the intended vMix scorebug.
- Detection groups bright pixels and warm display panels once on initial setup, or when explicitly requested afterward. It does not recognize field meanings. Reflections and labels can produce extra/merged boxes. Stabilization separately tracks a fixed camera reference; crops remain fixed until edited.
- Background removal uses either the brightest RGB channel or the dimmest channel for white-highlight isolation. Complex lighting may require manual adjustments. It cannot restore missing LED segments, lost detail, occlusions, or severe blur.
- Up to 24 fields in one combined canvas (up to 3840 × 2160), or independent legacy NDI streams, at a target of 30 fps. No audio. Throughput depends on canvas/crop sizes, processing controls, network, and available CPU. Source frames may be skipped to keep latency bounded.
- Output widths are padded to an even number of pixels for NDI compatibility. Alpha is straight, not premultiplied.
- GTZIP artwork stays in vMix. Basic GTZIP guides and target rectangles can be read for positioning, but no artwork or title animation is sent. There is no automatic vMix layer setup or remote venue transport.
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

The combined-output UI check uses `--composition-smoke-test <directory> --test-layout <photo-layout.json> --guide-file <Tipoff.gtzip>`, with optional `--video-file <clip.mp4> --ndi`. It checks the supplied Tipoff title's eight targets, independent output positioning, layout persistence, missing-guide recovery, and guide exclusion from the output. With NDI and video it also receives the combined feed and checks playback for 20 seconds. The video smoke test above requires a **video** layout, not a photo layout.

Updater checks run with `--update-exe <built-ScoreboardStudio.exe>` in the console harness (included by the release packaging script). The portable app supports `--update-smoke-test <empty-output-directory> --update-package <current-release.zip>` to verify a real GitHub download, helper shutdown/wait, installation, restart, setup restoration and locked-file rollback in a disposable app copy. This test downloads the current public release and launches temporary local app processes. Add `--update-ui-check` to the UI smoke test to check the update window and live-editing guard.

The `Core` project contains pixels, detection, layout persistence, the worker pipeline, and the NDI native bindings. The `App` project is the WPF desktop editor. `Checks` is a dependency-free console verification harness.

Native integration follows the official [NDI receive](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-recv), [send](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-send), [discovery](https://docs.ndi.video/all/developing-with-ndi/sdk/ndi-find), and [frame-format](https://docs.ndi.video/all/developing-with-ndi/sdk/frame-types) documentation. vMix documents [NDI transparency](https://download.vmix.com/help29/NDIAlphaChannel.html) and [input layers](https://www1.vmix.com/help29/InputSettingsMultiView.html).
