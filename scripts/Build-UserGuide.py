"""Build the TMS operator guide from the current app screenshots.
Requires reportlab; screenshots come from the application's --smoke-test mode.
"""
from pathlib import Path
from xml.sax.saxutils import escape
import argparse
import xml.etree.ElementTree as ET

from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor, white
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import Paragraph
from reportlab.lib.utils import ImageReader

ROOT = Path(__file__).resolve().parents[1]
VERSION = ET.parse(ROOT / 'src/ScoreboardStudio.App/ScoreboardStudio.App.csproj').findtext('.//Version')
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=Path.home() / 'Desktop' / 'TMS Scoreboard Studio - User Guide.pdf')
parser.add_argument('--screenshots', type=Path, default=ROOT / 'artifacts/tms-guide/screenshots')
args = parser.parse_args()
args.output.parent.mkdir(parents=True, exist_ok=True)
FONT = Path('C:/Windows/Fonts')
pdfmetrics.registerFont(TTFont('TMS', str(FONT / 'segoeui.ttf')))
pdfmetrics.registerFont(TTFont('TMS-Bold', str(FONT / 'segoeuib.ttf')))
pdfmetrics.registerFontFamily('TMS', normal='TMS', bold='TMS-Bold', italic='TMS', boldItalic='TMS-Bold')
NAVY = HexColor('#10151E')
BLUE = HexColor('#005BAB')
RED = HexColor('#ED1824')
INK = HexColor('#182332')
MUTED = HexColor('#516173')
PALE = HexColor('#EDF4FA')
LINE = HexColor('#D6E1EB')
LOGO = ROOT / 'src/ScoreboardStudio.App/Assets/TMS_logo.png'
SHOT = args.screenshots
c = canvas.Canvas(str(args.output), pagesize=(612, 792), pageCompression=1)
c.setTitle('TMS Scoreboard Studio | User Guide')
c.setAuthor('Total Media Sports')
c.setSubject(f'Presentation preview v{VERSION}: setup, live pixel cleanup, perspective correction, NDI and vMix')
c.setKeywords('TMS, Total Media Sports, Scoreboard Studio, user guide, vMix, NDI, scoreboard')
c.setViewerPreference('DisplayDocTitle', 'true')
pages = []

def para(text, x, top, width=516, size=10.5, color=INK, bold=False, leading=None):
    style = ParagraphStyle('body', fontName='TMS-Bold' if bold else 'TMS', fontSize=size,
                           leading=leading or size*1.45, textColor=color, spaceAfter=0)
    p = Paragraph(text, style)
    _, height = p.wrap(width, 1000)
    if top-height < 52:
        raise ValueError(f'Page overflow ({pages[-1]}): {text[:70]} -> {top-height}')
    p.drawOn(c, x, top-height)
    return top-height

def label(text, x, y, color=BLUE):
    c.setFillColor(color); c.setFont('TMS-Bold', 9); c.drawString(x, y, text)

def picture(path, x, top, width, max_height=None):
    image = ImageReader(str(path)); iw, ih = image.getSize()
    height = width*ih/iw
    if max_height and height > max_height:
        old_width = width
        width *= max_height/height; height = max_height
        x += (old_width-width)/2
    c.drawImage(image, x, top-height, width=width, height=height, mask='auto')
    return top-height

def begin(number, title, subtitle):
    if pages: c.showPage()
    pages.append(title)
    c.bookmarkPage(f'p{number}'); c.addOutlineEntry(title, f'p{number}', 0, False)
    c.setFillColor(NAVY); c.rect(0, 743, 612, 49, fill=1, stroke=0)
    c.setFillColor(RED); c.rect(0, 740, 612, 3, fill=1, stroke=0)
    c.setFont('TMS-Bold', 10); c.setFillColor(white); c.drawString(48, 763, 'TOTAL MEDIA SPORTS')
    c.setFont('TMS', 9); c.drawRightString(564, 763, 'SCOREBOARD STUDIO / USER GUIDE')
    label(f'{number:02d}', 48, 706, RED)
    para(title, 82, 723, 482, 25, bold=True, leading=29)
    y = para(subtitle, 48, 675, size=10.5, color=MUTED)
    c.setStrokeColor(LINE); c.line(48, 43, 564, 43)
    c.setFont('TMS', 8); c.setFillColor(MUTED)
    c.drawString(48, 28, f'TMS Scoreboard Studio  /  v{VERSION}  /  Internal use')
    c.drawRightString(564, 28, f'{number:02d} / 11')
    return y-18

def heading(text, y, x=48, width=516):
    return para(text, x, y, width, 13, BLUE, True)-7

def step(n, title, text, y):
    c.setFillColor(BLUE); c.roundRect(48, y-24, 24, 24, 5, fill=1, stroke=0)
    c.setFont('TMS-Bold', 10); c.setFillColor(white); c.drawCentredString(60, y-16, str(n))
    y = para(title, 84, y, 480, 11, INK, True)-3
    return para(text, 84, y, 480, 10.5)-16

def note(title, text, top):
    style = ParagraphStyle('note', fontName='TMS', fontSize=10, leading=14.3, textColor=INK)
    p = Paragraph(text, style); _, h = p.wrap(482, 1000)
    boxh = h+47
    if top-boxh < 55: raise ValueError(f'Note overflow on {pages[-1]}')
    c.setFillColor(PALE); c.roundRect(48, top-boxh, 516, boxh, 7, fill=1, stroke=0)
    c.setFillColor(RED); c.rect(48, top-boxh, 3, boxh, fill=1, stroke=0)
    para(title, 64, top-10, 482, 10.5, BLUE, True)
    p.drawOn(c, 64, top-34-h)
    return top-boxh-18

def row(left, right, y, left_width=130):
    bottom1 = para(left, 58, y-9, left_width-16, 10, BLUE, True)
    bottom2 = para(right, 48+left_width+10, y-9, 516-left_width-20, 10)
    bottom = min(bottom1,bottom2)-10
    c.setStrokeColor(LINE); c.line(48, bottom, 564, bottom)
    return bottom

# 01 / Cover and navigation
pages.append('Live digits. Your graphics.')
c.bookmarkPage('p1'); c.addOutlineEntry('Start here', 'p1', 0, False)
c.setFillColor(NAVY); c.rect(0,0,612,792,fill=1,stroke=0)
c.setFillColor(BLUE); c.rect(0,0,612,15,fill=1,stroke=0)
c.setFillColor(RED); c.rect(48,496,62,5,fill=1,stroke=0)
picture(LOGO, 48, 747, 340)
para('SCOREBOARD<br/>STUDIO',48,472,516,43,white,True,leading=46)
para('Live digits. Your graphics.',48,363,516,22,HexColor('#9FD1FF'))
para('USER GUIDE',48,305,516,11,white,True)
para('A practical guide to turning a scoreboard camera into clean,<br/>independent video fields for your vMix scorebug.',48,278,516,11,HexColor('#D4DFEA'))
contents=[('02','Launch & find your way'),('03','Connect & separate fields'),('04','Straighten an angled camera'),('05','Clean up the digits'),('06','Build the vMix scorebug'),('07','Load your own recording'),('08','Save, rehearse & go live'),('09','Troubleshooting'),('10','Stabilization & reliability'),('11','Updating the app')]
for i,(num,title) in enumerate(contents):
    x=48+(i%2)*270; y=203-(i//2)*27
    label(num,x,y,HexColor('#72BBFF'));para(title,x+29,y+10,225,9.5,white)
    c.linkRect('',f'p{int(num)}',(x,y-5,x+253,y+15),relative=0,thickness=0)
para(f'INTERNAL USE  /  v{VERSION}  /  NOVEMBER 2026',48,73,516,8.5,HexColor('#AABDCF'))

# 02 / first run and UI
y=begin(2,'Launch & find your way','Connect your scoreboard, then move through Fields, Cleanup and Outputs.')
y=para('<b>Open ScoreboardStudio.exe</b> in the portable app folder. The Windows x64 build includes .NET. Use <b>Load image or video</b> for your own footage. NDI input/output needs an installed 64-bit runtime; a standard vMix installation can supply it.',48,y)-14
y=picture(SHOT/'startup.png',48,y,516)-8
y=para('The app starts with no source or fields. Connect a feed or load your saved setup.',48,y,516,8.5,MUTED)-16
left=y; right=y
left=heading('1 / Source and field list',left,48,245)
left=para('Connect an NDI source, or use <b>Load image or video</b>. Select the field you want to edit in <b>Your fields</b>.',48,left,245,10)-12
left=heading('2 / Three work areas',left,48,245)
left=para('<b>Fields:</b> place boxes. <b>Cleanup:</b> compare pixels. <b>Outputs:</b> review the NDI field streams.',48,left,245,10)
right=heading('3 / Field settings',right,319,245)
right=para('Name, crop position and cleanup settings apply to the selected field. Scroll for additional controls.',319,right,245,10)-12
right=heading('4 / Top and bottom bars',right,319,245)
right=para('Save or load a setup, lock editing and start outputs. The bottom bar shows NDI availability and output status.',319,right,245,10)

# 03 / connect and crop
y=begin(3,'Connect & separate fields','Keep the camera fixed and frame the entire scoreboard clearly.')
y=step(1,'Make the camera available over NDI','The dropdown lists <b>NDI feeds only</b>. Add a USB camera or capture card in vMix and enable NDI for its feed. Direct camera-device capture is not supported in this version.',y)
y=step(2,'Select the source and connect','Click <b>Refresh</b>, choose the NDI feed and click <b>Connect</b>. For a local file, use <b>Load image or video</b>; an empty NDI dropdown is normal and does not prevent playback.',y)
y=step(3,'Correct the angle first','If the board is tilted or trapezoidal, use <b>Straighten camera</b> before placing boxes. See page 4. Field coordinates refer to the corrected image.',y)
y=step(4,'Review the initial boxes','The first source in a fresh app session gets automatic boxes once. After that, they stay fixed. Click <b>+ Draw a field</b> and drag a rectangle to add one. Drag inside an existing box to move it, or its lower-right handle to resize.',y)
y=step(5,'Name and review each field','Use names such as Game Clock, Home Score and Period. Leave room for every possible digit, including a third score digit and a clock switching to tenths. Check <b>Include in NDI outputs</b> for the fields you want to send.',y)
y=note('Automatic only at the start','Edits, deleting all fields, camera motion and looping never restart placement. Saved layouts skip initial detection. For an explicit rescan, use <b>Find fields → Replace fields with suggestions</b>. <b>Undo detection</b> restores the previous list.',y)
para('To delete a field, select it under <b>Your fields</b> and click <b>Remove selected field</b> at the bottom left. Repeat as needed, then save. Detection creates editable boxes; it does not identify field meanings.',48,y,size=10,color=MUTED)

# 04 / perspective
y=begin(4,'Straighten an angled camera','Four corner points turn the selected scoreboard plane into a rectangle.')
y=picture(SHOT/'perspective-editor.png',48,y,516,230)-10
y=para('The numbered handles are ordered around the board: top left, top right, bottom right, bottom left.',48,y,516,9,MUTED)-15
y=step(1,'Choose Fields > Straighten camera','The picture freezes while you edit. Drag a numbered dot or its label directly; no preliminary click is needed. Select a corner and use arrow keys for one-pixel adjustments, or Shift plus arrows for ten pixels. Keep the corners in order.',y)
y=step(2,'Set the corrected size and apply','Choose width and height matching the board\'s proportions, then click <b>Apply</b>. Existing boxes are retained. Remove unwanted fields and resize the rest, or use <b>Find fields → Replace fields with suggestions</b>. <b>Cancel</b> keeps the previous correction; <b>Reset correction</b> restores the original geometry.',y)
note('Review before restarting outputs','Opening this editor stops NDI outputs. Finish or cancel the edit, review the fields, and start outputs explicitly. Use stabilization for small camera disturbances (page 10); larger moves need realignment. Correction cannot recover detail or fix lens distortion.',y)

# 05 / cleanup
y=begin(5,'Clean up the digits','Select a field and open Cleanup. Compare the original with the transparent preview.')
# Detail image is from the actual current app; it is placed without editing.
y=picture(SHOT/'real-scoreboard-cleanup.png',48,y,516,245)-9
y=para('The real-photo example uses White highlights to isolate white digit cores from red glow.',48,y,516,9,MUTED)-13
y=row('Isolation mode','Use <b>Bright colors</b> for saturated red or amber LEDs on dark panels. Use <b>White highlights</b> for white digit cores surrounded by colored glow.',y)
y=row('Background cutoff','Raise it until the panel disappears. If digit segments fade, lower it. Adjust each field separately.',y)
y=row('Edge softness','Controls how gradually pixels become opaque above the cutoff. Too much can make weak digits faint.',y)
y=row('Brightness & color','Brightness changes visible digit color, not the background mask. Choose White, Original or Custom. The physical digit shape stays the same.',y)
y=row('Thickness & smoothing','Use small thickness changes and Smooth edges for readability. Remove isolated speckles suppresses small dots; check that decimal points survive.',y)
y=para('<b>Check at broadcast size.</b> Try the Dark and Light preview backgrounds as well as Checkerboard. <b>Apply cleanup to all fields</b> copies the selected settings; use it only when the other panels need the same treatment.',48,y-14,516,10)

# 06 / vmix
y=begin(6,'Build the vMix scorebug','Each enabled field becomes an independent NDI video source with transparency.')
for i,(title,body) in enumerate([
('CAMERA','Original feed'),('TMS STUDIO','Crop + clean'),('VMIX','Layer over artwork')]):
    x=48+i*178;c.setFillColor(NAVY);c.roundRect(x,y-62,160,62,6,fill=1,stroke=0)
    para(title,x+13,y-11,134,10,white,True);para(body,x+13,y-32,134,9,HexColor('#A8D6FF'))
    if i<2: para('›',x+162,y-20,16,19,RED,True)
y-=84
y=step(1,'Send the original camera input','In vMix Settings, use the <b>Outputs / NDI / OMT / SRT</b> options to enable NDI for the intended feed. Available routing depends on your vMix edition. Keep the camera feed separate from the composed scorebug so you do not create a feedback loop.',y)
y=step(2,'Start the cleaned field outputs','In TMS Scoreboard Studio, confirm the source and crops, then click <b>Start NDI outputs</b>. In Outputs, review each field and its published source name.',y)
y=step(3,'Add the fields in vMix','Use <b>Add Input > NDI</b> and select each <b>Scoreboard - &lt;field name&gt;</b> source. NDI also includes your computer name. Still-image streams include <b>STILL</b>.',y)
y=step(4,'Position fields over your graphic','Keep the GT title or scorebug artwork in vMix. Use the composition input\'s <b>Layers</b> settings to place and size each field above it. vMix detects NDI transparency automatically. This app sends straight alpha; leave premultiplied-alpha handling off for these inputs.',y)
y=note('Two setups to save','Save the field crops and cleanup in Scoreboard Studio. Save the scorebug layout and layer positions in your vMix preset. The app does not import GTZIP files or automatically build the vMix composition.',y)
para('vMix 29 references: <link href="https://www.vmix.com/help29/SettingsOutputs.html" color="#005BAB">Output routing</link> · <link href="https://www.vmix.com/help29/NDIAlphaChannel.html" color="#005BAB">NDI alpha</link> · <link href="https://www.vmix.com/help29/InputSettingsMultiView.html" color="#005BAB">Layers</link>. Menu labels can differ by version.',48,y,516,8.5,MUTED)

# 07 / operator-supplied recording
y=begin(7,'Load your own recording','Open a clip directly in Scoreboard Studio. No vMix input is needed for local playback.')
y=picture(SHOT/'video-fields.png',48,y,516,245)-8
y=para('A recorded scoreboard with four initial automatic boxes. They remain fixed during playback.',48,y,516,8.5,MUTED)-15
y=step(1,'Load image or video','Select your own video file. Playback begins automatically with audio muted. H.264 MP4 is recommended; other offered containers depend on the codecs installed in Windows.',y)
y=step(2,'Control playback','Use <b>Pause / Play</b> to hold or resume the picture, and <b>Restart</b> to return to the beginning. <b>Loop video</b> repeats the recording. With looping off, playback holds the final frame.',y)
y=step(3,'Review and save your fields','Review the initial automatic boxes, name them and adjust cleanup. Add missing boxes with <b>+ Draw a field</b>. A saved setup remembers the video path and field settings. Loading a layout keeps its boxes; playback does not replace them.',y)
note('Use your own footage','No sample files are bundled. Copy your recording and saved layout to the studio computer together. The app fits video within 1280 × 720 for processing while preserving its aspect ratio. Start NDI outputs when you want to send the cleaned fields to vMix.',y)

# 08 / live use
y=begin(8,'Save, rehearse & go live','Make the full workflow repeatable before the audience arrives.')
y=heading('Save the venue setup',y)
y=para('Use <b>Save setup</b> or <b>Save as</b> to store field names, crop boxes, cleanup, output enablement and perspective correction in a JSON file. Image and video setups reference their source file; keep it available when moving a setup. Media is not embedded.',48,y)-17
y=heading('Restore deliberately',y)
y=para('Use <b>Load setup</b>, reconnect the intended NDI source, and review every crop before starting outputs. A saved NDI source name may differ on another computer. Loading a setup leaves outputs stopped.',48,y)-20
for n,title,body in [
    (1,'Check the whole number range','Watch the countdown, tenths and shot-clock resets. Allow space for larger scores. Confirm decimal points and thin LED segments remain readable.'),
    (2,'Inspect the finished scorebug','View it at the actual output size and on the intended background. Check layer placement, transparency and timing with the camera source.'),
    (3,'Lock for live','Once ready, click Lock for live to protect source, crop and cleanup controls. You can still stop outputs. Unlock before making changes.'),
    (4,'Keep a clear fallback','Keep the recorded-clock clip or a real-photo setup available for explanation. Identify recorded material clearly when presenting it.')]:
    y=step(n,title,body,y)
y=note('When video stops','After roughly 1.5 seconds without a new NDI frame, the app reports <b>NO SIGNAL</b> and clears the fields to transparency. It resumes when frames return. A camera or encoder repeatedly sending a frozen picture will not trigger this check.',y)
para('Preview scope: fixed crops, local pixel processing and up to 24 fields. Camera movement, lost LED segments, occlusion and severe blur need source-side correction. Rehearse the actual venue feed and intended vMix scorebug before live use.',48,y,size=9.5,color=MUTED)

# 09 / troubleshooting and references
y=begin(9,'Troubleshooting','Start with the source preview, then the cleaned field, then the vMix layer.')
for title,body in [
    ('No NDI runtime','Install the 64-bit NDI runtime or use a standard vMix installation. Restart the app. Local image/video processing works without NDI. For a custom runtime location, set SCOREBOARD_NDI_RUNTIME to the full DLL path before launching.'),
    ('Source list is empty','This list is NDI-only. Local images and videos work with an empty list. For a camera or capture card, enable its NDI feed in vMix, then Refresh. Check network and firewall access if discovery fails.'),
    ('NO SIGNAL','Check camera playback and the sending NDI output. Select the right source and reconnect if needed. Cleared fields are intentional when frames stop arriving.'),
    ('Glow or panel remains','Check Remove background. Increase Background cutoff. Try White highlights for bright white cores in colored glow, or Bright colors for saturated LEDs.'),
    ('Digits or decimal vanish','Lower the cutoff, reduce excessive softness, and check Remove isolated speckles. Enlarge the crop if a digit is clipped. Inspect the original: cleanup cannot restore missing pixels.'),
    ('Boxes drift off the digits','The camera moved or its zoom/framing changed. Stop outputs, unlock, realign the corner correction and review every crop. Crops do not track motion.'),
    ('A field is missing in vMix','Check Include in NDI outputs and Start NDI outputs. Look for the computer name and any STILL prefix. Check layer visibility and order in the vMix composition.'),
    ('Media file missing or unsupported','Use Load image or video to locate your media, then save the setup. If video will not open, try an H.264 MP4 supported by Windows. Source files are not embedded in saved layouts.')]:
    y=row(title,body,y,138)
y-=16
y=heading('Project & further help',y)
y=para('<link href="https://github.com/BitFaced2/scoreboard-studio/releases" color="#005BAB">GitHub releases and downloads</link><br/>This guide covers v'+VERSION+'. Download the matching Windows ZIP and extract it before launch. Bring your own media and saved setup to the studio.',48,y,size=9.5)-10
para(f'Prepared for Total Media Sports · November 2026<br/>Screenshots show the branded v{VERSION} preview. Recorded-camera checks use your supplied capture. No OCR, cloud account or model download is required.',48,y,size=8.5,color=MUTED)

y=begin(10,'Stabilization & reliability','Correct small camera disturbances while keeping your field boxes fixed.')
y=step(1,'Set up, then stabilize','Frame the board, straighten it if needed and place your fields. Click <b>Stabilize camera</b>. The app captures a reference from the current view and corrects small shifts, rotation and zoom before cropping. Review alignment, then start outputs.',y)
y=step(2,'Watch the tracking status','Tracking needs visible detail around the board. If it is lost, outputs become transparent. Returning the camera to the reference view allows recovery. Stabilization never redetects or moves your field boxes.',y)
y=step(3,'Realign after a larger move','Stop stabilization, realign the perspective and fields, then enable stabilization again. <b>Set reference</b> explicitly captures a new view and stops outputs for review. References last only for this session; loading a setup or changing sources turns stabilization off.',y)
y=heading('Resize or minimize the window',y)
y=para('Use the Windows title-bar controls or the minimize and restore buttons in the app header. Drag a window edge to resize. Startup fits the available screen area; small windows scroll so controls remain reachable.',48,y)-18
y=heading('If the app closes unexpectedly',y)
y=para('Error logs are stored in <b>%LOCALAPPDATA%\\TMS Scoreboard Studio\\Diagnostics</b>. Paste that path into File Explorer. On a handled UI error, the app attempts to save a timestamped recovered setup there before closing. Native decoder or process termination failures may not allow recovery.',48,y)-16
note('Playback checks and limits','Playback reuses rendering resources and skips unchanged-frame processing. Small-disturbance tracking has bounded search; it cannot follow a camera around the venue or restore missing detail. Save the setup regularly and test your actual camera and vMix composition.',y)

y=begin(11,'Updating the app','Check for a new release when you are ready to stop and restart the app.')
y=picture(SHOT/'update-window.png',48,y,516,175)-12
y=step(1,'Stop outputs and unlock editing','Finish any camera-corner edit. Click <b>Check for updates</b> in the bottom bar. Internet access is needed to check GitHub; no account is required. Published preview releases are included.',y)
y=step(2,'Download and verify','When a newer version is available, click <b>Download update</b>. The app verifies the package against GitHub\'s checksum before offering to install. Downloading does not change your installed app.',y)
y=step(3,'Install and restart','Click <b>Install and restart</b>, then save unsaved setup changes when prompted. Keep the app folder writable and close other copies. Program files update in place; shortcuts, saved setups and recordings remain in place.',y)
y=step(4,'Review your restored setup','The app reopens your saved setup with outputs stopped. Reconnect a live source and set stabilization again if needed. Check the picture and fields before restarting outputs.',y)
note('Getting the updater for the first time','Versions before 0.5.0 need one final manual GitHub ZIP download. Extract it and open the new executable; no uninstall is needed. Later updates use this window. Failed downloads leave the app unchanged, and ordinary installation write failures restore the previous files. Use <b>GitHub downloads</b> in the update window for a manual fallback.',y)

assert len(pages)==11
c.save()
print(f'Created {args.output} ({len(pages)} pages)')
