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
    c.drawString(48, 28, f'TMS Scoreboard Studio  /  v{VERSION} presentation preview')
    c.drawRightString(564, 28, f'{number:02d} / 09')
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
contents=[('02','Launch & find your way'),('03','Connect & separate fields'),('04','Straighten an angled camera'),('05','Clean up the digits'),('06','Build the vMix scorebug'),('07','Use the recorded-clock clip'),('08','Save, rehearse & go live'),('09','Troubleshooting')]
for i,(num,title) in enumerate(contents):
    x=48+(i%2)*270; y=203-(i//2)*27
    label(num,x,y,HexColor('#72BBFF'));para(title,x+29,y+10,225,9.5,white)
    c.linkRect('',f'p{int(num)}',(x,y-5,x+253,y+15),relative=0,thickness=0)
para(f'PRESENTATION PREVIEW  /  v{VERSION}  /  NOVEMBER 2026',48,73,516,8.5,HexColor('#AABDCF'))

# 02 / first run and UI
y=begin(2,'Launch & find your way','Connect your scoreboard, then move through Fields, Cleanup and Outputs.')
y=para('<b>Open ScoreboardStudio.exe</b> in the portable app folder. Keep the <b>samples</b> folder beside it. The Windows x64 build includes .NET. NDI input/output needs an installed 64-bit NDI runtime; a standard vMix installation can supply it.',48,y)-14
y=picture(SHOT/'startup.png',48,y,516)-8
y=para('The app starts with no source or fields. Connect a feed or load your saved setup.',48,y,516,8.5,MUTED)-16
left=y; right=y
left=heading('1 / Source and field list',left,48,245)
left=para('Choose a source, connect, or open a still image. Select the field you want to edit in <b>Your fields</b>.',48,left,245,10)-12
left=heading('2 / Three work areas',left,48,245)
left=para('<b>Fields:</b> place boxes. <b>Cleanup:</b> compare pixels. <b>Outputs:</b> review the NDI field streams.',48,left,245,10)
right=heading('3 / Field settings',right,319,245)
right=para('Name, crop position and cleanup settings apply to the selected field. Scroll for additional controls.',319,right,245,10)-12
right=heading('4 / Top and bottom bars',right,319,245)
right=para('Save or load a setup, lock editing and start outputs. The bottom bar shows NDI availability and output status.',319,right,245,10)

# 03 / connect and crop
y=begin(3,'Connect & separate fields','Keep the camera fixed and frame the entire scoreboard clearly.')
y=step(1,'Make the camera available over NDI','Bring the scoreboard feed into the studio and expose it as an NDI source. This app handles the feed after it arrives; it does not transport video from the venue.',y)
y=step(2,'Select the source and connect','Choose it in the source dropdown. Click <b>Refresh</b> if needed, then <b>Connect</b>. Confirm the source preview is live and shows the correct board.',y)
y=step(3,'Correct the angle first','If the board is tilted or trapezoidal, use <b>Straighten camera</b> before placing boxes. See page 4. Field coordinates refer to the corrected image.',y)
y=step(4,'Draw one box per useful field','Click <b>+ Draw a field</b>, then drag around the full clock, score, period or other field. Drag inside a box to move it; drag its lower-right handle to resize. For exact positions, edit the percentages and click <b>Apply crop</b>.',y)
y=step(5,'Name and review each field','Use names such as Game Clock, Home Score and Period. Leave room for every possible digit, including a third score digit and a clock switching to tenths. Check <b>Include in NDI outputs</b> for the fields you want to send.',y)
y=note('Want help placing boxes?','<b>Auto-detect fields</b> proposes boxes based on bright pixels. Choose <b>Look for</b> and a <b>Cutoff</b>, inspect the suggestions, then choose <b>Use suggestions</b>. Accepting replaces the field list; <b>Undo detection</b> restores the previous list. Name and adjust the accepted fields yourself.',y)
para('Automatic detection does not identify scores or field meanings. Glare, labels and neighboring displays can create extra or merged boxes. Manual setup is a reliable starting point for a presentation.',48,y,size=10,color=MUTED)

# 04 / perspective
y=begin(4,'Straighten an angled camera','Four corner points turn the selected scoreboard plane into a rectangle.')
y=picture(SHOT/'perspective-editor.png',48,y,516,275)-10
y=para('The numbered handles are ordered around the board: top left, top right, bottom right, bottom left.',48,y,516,9,MUTED)-15
y=step(1,'Choose Fields > Straighten camera','Drag each numbered point to the matching corner of the scoreboard. Keep the points in order and do not cross the edges.',y)
y=step(2,'Set the corrected size and apply','Choose width and height that match the board\'s proportions, then click <b>Apply</b>. Review and reposition the field boxes afterward. <b>Cancel</b> keeps the previous correction; <b>Reset correction</b> returns to the original geometry.',y)
note('Review before restarting outputs','Opening this editor stops NDI outputs. Finish or cancel the edit, review the fields, and start outputs explicitly. Correction is fixed: if the camera moves, realign it. It cannot recover missing detail or correct lens distortion.',y)

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

# 07 / known working recorded clip
y=begin(7,'Use the recorded-clock clip','A prepared local recording demonstrates moving digits and a change to tenths.')
y=picture(ROOT/'artifacts/recording-test/preview-30s.png',48,y,516,260)-8
y=para('Rendered comparison: recorded source, corrected right-hand display and two cleaned fields.',48,y,516,8.5,MUTED)-15
y=para('Local files: <b>artifacts / recording-test</b>. The download includes the setup in <b>samples / recorded-clock</b>; bring your video separately.',48,y,size=10)-8
y=row('Playback video','basketball-camera-720p.mp4 — a 31.7-second 720p/30 fps copy of your capture.',y,146)
y=row('Saved field setup','basketball-clocks.scoreboard.json — right-hand display correction, Game Clock and Shot Clock crops, and cleanup settings.',y,146)
y=para('<b>Rehearse:</b> Loop the playback video in vMix and send its full 16:9 frame over NDI. Load the saved setup in Scoreboard Studio, select that NDI source and click Connect. Review both fields, start outputs, then add them to your vMix scorebug.',48,y-14,size=10)-12
y=para('<b>For an offline explanation:</b> play clock-cleanup-preview.mp4. Its checkerboard illustrates transparency; the comparison video itself is opaque. It is a rendered pipeline test, not a recording of the app interface.',48,y,size=10)-12
para('<b>Verified:</b> 951 frames processed without either cleaned field becoming blank. Local NDI output retained transparency and cleared on source loss. The app does not directly open MP4 files. The loop restarts the clock at the beginning of the recording.',48,y,size=9.5,color=MUTED)

# 08 / live use
y=begin(8,'Save, rehearse & go live','Make the full workflow repeatable before the audience arrives.')
y=heading('Save the venue setup',y)
y=para('Use <b>Save setup</b> or <b>Save as</b> to store field names, crop boxes, cleanup, output enablement and perspective correction in a JSON file. Image setups reference the photo file; keep that file available when moving a setup. A video file or graphic is not embedded.',48,y)-17
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
    ('No NDI runtime','Install the 64-bit NDI runtime or use a standard vMix installation. Restart the app. Photo setup works without NDI. For a custom runtime location, set SCOREBOARD_NDI_RUNTIME to the full DLL path before launching.'),
    ('Source list is empty','Confirm NDI is enabled at the source, then click Refresh. Verify that both apps can communicate on the intended network and that firewall access is allowed.'),
    ('NO SIGNAL','Check camera playback and the sending NDI output. Select the right source and reconnect if needed. Cleared fields are intentional when frames stop arriving.'),
    ('Glow or panel remains','Check Remove background. Increase Background cutoff. Try White highlights for bright white cores in colored glow, or Bright colors for saturated LEDs.'),
    ('Digits or decimal vanish','Lower the cutoff, reduce excessive softness, and check Remove isolated speckles. Enlarge the crop if a digit is clipped. Inspect the original: cleanup cannot restore missing pixels.'),
    ('Boxes drift off the digits','The camera moved or its zoom/framing changed. Stop outputs, unlock, realign the corner correction and review every crop. Crops do not track motion.'),
    ('A field is missing in vMix','Check Include in NDI outputs and Start NDI outputs. Look for the computer name and any STILL prefix. Check layer visibility and order in the vMix composition.'),
    ('Photo or video file missing','Keep the samples folder with the app. The recorded clip files are local project artifacts, separate from the public release. MP4 playback is supplied by vMix, not by the app.')]:
    y=row(title,body,y,138)
y-=16
y=heading('Project & further help',y)
y=para('<link href="https://github.com/BitFaced2/scoreboard-studio" color="#005BAB">github.com/BitFaced2/scoreboard-studio</link><br/>The project README describes current limits and runtime configuration. Download the Windows ZIP from Releases and keep its samples folder with the app.',48,y,size=9.5)-10
para(f'Prepared for Total Media Sports · November 2026<br/>Screenshots show the branded v{VERSION} preview. Recorded-camera checks use your supplied capture. No OCR, cloud account or model download is required.',48,y,size=8.5,color=MUTED)

assert len(pages)==9
c.save()
print(f'Created {args.output} ({len(pages)} pages)')
