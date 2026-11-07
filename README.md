### AI Assistance Disclosure

This project was developed with the assistance of AI tools, which were used throughout the development process for tasks such as code generation, debugging, problem-solving, and technical research.

The overall concept, project direction, feature requirements, testing, and final implementation decisions were guided and reviewed by the developer. AI served as a development aid, while responsibility for the project, its design, and its final results remains with the author.

# KH1 ARD Viewer

A lightweight Windows desktop application for browsing and viewing 3D models
stored in Kingdom Hearts 1 `.ard` archives. KH1 ARD Viewer reads supported MDLS
and MFA resources, renders the selected model with native OpenGL, previews its
textures, and exports it as a glTF 2.0 file with PNG textures alongside it.

<img width="1919" height="998" alt="image" src="https://github.com/user-attachments/assets/258a8016-5856-4f44-bcf6-a2efc8e06497" />

## Features

- Open one or more `.ard` files, or recursively load ARD files from a folder.
- Show a live `Files Loaded` count while multiple archives are loading.
- Remember and automatically reload ARD files from the last selected folder
  when the viewer starts.
- Group recognized archives by world when multiple ARDs are loaded; category
  headings are bold, expandable, and keep `prize.ard` under **Items** at the
  bottom of the tree.
- Expand or collapse each category and archive's model list, then select a
  model for viewing.
- Render MDLS and MFA geometry in a native OpenGL viewport.
- Export rigged MDLS models with their armature, bones, and vertex weights.
- Orbit, zoom, and pan around the model, with a ground grid for scale and
  orientation; right- and middle-button drags pan without rotating.
- View mesh, vertex, triangle, and texture counts for the selected model.
- Browse embedded MSET animations in a fixed, title-bar-free list and scrub
  their playback.
- Browse decoded model textures and open a larger texture preview.
- Export the selected model to a glTF 2.0 file with separate PNG textures.
- Export one or more selected MSET animations as separate `.mset` files.
- Show a temporary green confirmation after successful model or MSET exports.
- Start without loading or selecting an archive; the Export button appears
  only after a model is selected.

## Requirements

### To run the viewer

- Windows x64.
- The **.NET 10 Desktop Runtime for Windows x64** (including the Windows
  Desktop runtime). The executable is framework-dependent and does not bundle
  .NET.
- A graphics adapter and driver that support OpenGL.
- No administrator rights, browser, or internet connection are required for
  normal offline use.

### To build from source

- Windows x64.
- The **.NET 10 SDK for Windows x64**. It includes the `dotnet` CLI and
  Windows desktop targeting support used by the build script.
- Internet access may be needed the first time NuGet restores the SDK's
  project dependencies.

The distributed application is a single-file, framework-dependent publish.
Its application assembly and .NET dependency/runtime configuration files are
bundled inside `KH1 ARD Viewer.exe`; the .NET 10 Desktop Runtime itself is not
included. The app remembers the last folder in
`%LOCALAPPDATA%\KH1 ARD Viewer\settings.json`.

## Quick start

1. Download `KH1 ARD Viewer.exe` from the latest GitHub Release, or build it
   from source using the instructions below.
2. Install the .NET 10 Desktop Runtime for Windows x64 if it is not already
   installed.
3. Run `KH1 ARD Viewer.exe`.
4. Choose **Open .ard** to load archive files, or **Open folder...** to search
   a folder and its subfolders.
5. Expand an archive using the arrow beside its name, then select a model to
   display it.

When multiple ARD files are loaded together, recognized archive filename
prefixes are grouped into world categories: `al` (Agrabah), `aw` (Wonderland),
`dc` (Disney Castle), `dh` (Dive into the Heart), `di` (Destiny Islands),
`ew` (End of the World), `he` (Olympus Coliseum), `lm` (Atlantica), `nm`
(Halloween Town), `pc` (Hollow Bastion), `pi` (Monstro), `po` (100th Acre
Wood), `pp` (Neverland), `tw` (Traverse Town), and `tz` (Deep Jungle).
`prize.ard` is placed in **Items**, which remains at the bottom of the tree.
Unrecognized archives remain ungrouped.

The viewer does not automatically choose a model. Select an individual MDLS or
MFA entry to activate the viewport, statistics, texture gallery, and Export
button. On later launches, it automatically scans and loads ARD files from the
last selected folder.

## Controls

| Action | Control |
| --- | --- |
| Orbit around the model | Drag with the mouse |
| Zoom in or out | Mouse wheel |
| Pan the view | Drag with the right or middle mouse button, or hold Shift while dragging |
| Show or hide a category's archives or an archive's models | Select its arrow |
| Restore the default view | Select **Reset camera** |
| Open or close the fixed MSET animation list | Select **Animations**; select it again to close |
| Select multiple animations for export | Use Ctrl-click or Shift-click in the animation list |
| Play, pause, or seek a single animation | Select exactly one animation, then use **Play/Pause** and the timeline slider |
| Automatically play a selected animation | Enable **Auto Play** in the animation list |
| Stop at the end or repeat the current animation | Toggle **Loop** in the animation list |
| Show or hide the skeleton | Select **Show bones** / **Hide bones** |
| Inspect a texture | Select a texture thumbnail |
| Close the enlarged texture preview | Press Escape or click outside the preview |

## Exporting

Select a model and choose **Export**. KH1 ARD Viewer writes a glTF 2.0 JSON file
under the application directory:

```text
Extract\<ARD name>\<model>_extracted\<model>.gltf
```

The geometry buffer is embedded in the `.gltf` file. Each decoded texture is
saved as a separate PNG beside it, and referenced by a relative filename in
the glTF. Model names are sanitized before they are used as directory and file
names.

For MDLS models with embedded MSET data, open **Animations** and select one or
more entries. **Export selected MSETS...** writes each selected animation as
its own `.mset` file in the same model-specific extraction folder as the glTF
export. Each file retains the original MMTN data and isolates its corresponding
animation reference. The export button appears when one or more animations are
selected; **Play** appears only when exactly one is selected. Successful model
and MSET exports show a green confirmation near the top of the viewer, which
fades after five seconds.
The animation list identifies entries by index and MMTN offset; no internal
animation name is currently decoded from these blocks. The list is fixed above
the **Animations** button, has no title bar or close button, and closes when
that button is selected again. The playback slider, frame counter, and Play
button are positioned beside **Animations**.

When importing a skinned export in Blender, its glTF importer may create a
hidden `_not_exported` collection with icospheres to display custom bone shapes.
These are generated by Blender, not included in the exported glTF. Enable
**Disable Bone Shape** in Blender's glTF import options to prevent them while
keeping the armature and bones.

The application directory must be writable to export there. If Windows
restricts writes to the folder where the viewer was installed, move the
complete folder to a user-writable location before exporting.

## Supported archive data

KH1 ARD Viewer targets Kingdom Hearts 1 ARD archives and their supported model
resources:

- **MDLS**: model geometry, decoded textures, mesh texture assignments, and
  available joint transforms. Skinned glTF exports preserve the armature,
  bones, and vertex weights in the bind pose.
- **MFA**: geometry, vertex colors, and decoded PlayStation 2 GS textures,
  using the MFA parsing and texture-decoding logic refined against the KH1
  Unity importer.

The two formats use different geometry and texture conventions. The viewer
keeps their UV handling separate: MDLS textures receive the row orientation
needed by the renderer, while MFA UV coordinates and decoded texture rows are
preserved. Unsupported or malformed resources may be skipped; an archive can
open successfully even if it contains no usable MDLS or MFA models.

Texture alpha uses a 0.5 alpha-clip cutoff rather than blended transparency.
Meshes with UV coordinates slightly outside the `[0, 1]` range (no more than
`0.05`) use clamped texture sampling; other meshes retain each texture's
configured repeat or clamp behavior. glTF exports preserve the same alpha and
per-mesh sampler settings.

The viewer decodes embedded MMTN references against the containing MDLS
skeleton. Key headers contain a signed time in 1/256-frame units and an
interpolation byte: hold, linear, or cubic Hermite. Tangents use the same time
units. Each curve preserves its hold, linear, or repeat behavior at the edges;
support keys beyond a clip's playback end are retained. **Play/Pause**
evaluates those keyframes over the clip's actual timeline; the floating
**Animations** list selects a clip, and the timeline slider scrubs its current
frame. Playback resumes from the selected frame; it does not cycle or blend
between unrelated static poses. Joint curves and helper curves use
separate index spaces. Motion flags drive fixed joints, helper positions and
orientations, and one- or two-bone IK chains, with the same evaluated skeleton
used by the mesh, bone overlay, and export. Clips animated only through helpers
are also playable. Unsupported curve
edge modes are skipped and reported. **Show bones** toggles the model skeleton in
the preview, including its animated joint positions. Exports preserve the selected
pose and armature, including the current animation frame when exporting during
playback. Poses are evaluated independently from the clip's initial state;
runtime actor placement, gameplay look-at targets and ground collision IK are
not simulated.

The animation list supports multiple selection for export. Playback requires
exactly one selected clip. The camera's far clipping distance is extended for
long-range viewing; the mouse wheel controls zoom.

The MMTN key layout and skeleton flag semantics were cross-checked against
[KH1-EVDL-TOOLS's animation implementation](https://github.com/gaithern/KH1-EVDL-TOOLS/tree/5df63d2/anim).

### Animation regression checks

Run the dependency-free regression executable with the .NET 10 SDK:

```powershell
dotnet run --project Tests/AnimationRegression.csproj -c Release
```

The synthetic checks cover packed key headers, signed and fractional times,
hold/linear/Hermite interpolation, tangent units, edge modes, separate helper
indices, helper orientations, and two-bone IK. To parse and sample every ARD
in a local game-data folder without opening the UI:

```powershell
dotnet run --project Tests/AnimationRegression.csproj -c Release -- --scan "C:\path\to\kh1"
```

No game assets are included in the tests.

The native playback regression also invokes the production Play, timer tick,
and Pause handlers on a hidden form. It verifies that a helper-only clip keeps
playing, advances its frame, and moves viewport vertices without requiring an
OpenGL context. The reference run repeats this check with `nm01.ard / nm_6105`.

To audit every playable clip through those native handlers, including its key
times, interval midpoints and timeline samples, write a JSON playback report:

```powershell
dotnet run --project Tests/AnimationRegression.csproj -c Release -- --playback-scan "C:\path\to\kh1" "C:\temp\playback-report.json"
```

The audit checks clip selection, frame progression, timer continuity, Pause,
and finite vertex/bone data. It records curve, skeleton and vertex movement
separately, so a clip with changing but unused helpers is visible in the
report without being mistaken for a stopped timer. Poses with no duration
are excluded using the same eligibility rules as Play.

For an independent comparison, clone the linked KH1-EVDL-TOOLS reference at
commit `5df63d2`, then generate private fixtures from local game assets:

```powershell
python Tests/generate_reference.py "C:\path\to\kh1" "C:\path\to\KH1-EVDL-TOOLS" "C:\temp\poses.json"
dotnet run --project Tests/AnimationRegression.csproj -c Release -- --reference "C:\temp\poses.json"
```

This compares every world-matrix component at five times per clip across
seven archives, including helper orientation, aim, one- and two-bone IK,
flipped chains, root-relative rotation and keep-position flags.

## Build from source

The complete source project and icon assets are in `Data\`. Building requires
the .NET 10 SDK for Windows x64.

To build the viewer without launching it, run:

```text
Build.bat
```

The script forces .NET CLI and compiler messages to English and requests
detailed MSBuild verbosity so restore, compile, and publish task progress is
visible. It publishes the framework-dependent single-file executable to a
temporary build folder, copies it to the repository root, and removes that
temporary folder.

Alternatively, to build into a separate `_publish` folder, open a terminal in
this folder and run:

```powershell
dotnet publish .\Data\KH1ARDViewer.csproj `
  -c Release `
  -r win-x64 `
  --no-self-contained `
  -p:SelfContained=false `
  -p:PublishSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\_publish
```

This framework-dependent build creates a single `KH1 ARD Viewer.exe` in the
`_publish` folder. It bundles the application's DLL and JSON configuration
files but does not bundle the .NET Desktop Runtime. The project embeds
`Data\KH1 ARD Viewer.ico` into the executable; the transparent PNG source is
`Data\KH1 ARD Viewer.png`.

## How it works

1. **Archive discovery** - The reader validates the ARD offset table and uses
   the archive's resource names and model data regions to locate supported
   resources.
2. **Model decoding** - MDLS resources are parsed by the MDLS format reader.
   MFA resources use the dedicated MFA parser, including the KH1 importer
   refinements for PS2 GS texture data.
3. **Scene preparation** - Geometry, texture pixels, colors, UVs, and relevant
   MDLS joint transforms are converted into the viewer's scene representation.
   The ground grid is placed just below the model's lowest vertices.
4. **Rendering** - The native WinForms interface hosts an OpenGL viewport.
   Selecting a model updates the scene and the related model and texture
   information without opening a browser or starting a local web server.
5. **Export** - The selected scene is converted to glTF 2.0, with its geometry
   data embedded in the resulting JSON file and decoded textures written as
   adjacent PNG files.

## Project layout

```text
ARD Viewer/
|-- Build.bat                      Build script
|-- README.md                      This guide
|-- .gitignore                     Build and local-output exclusions
|-- .github/
|   `-- workflows/
|       `-- release.yml            Build and publish tagged releases
`-- Data/
    |-- KH1ARDViewer.csproj
    |-- NativeViewerForm.cs        Native UI and OpenGL viewport
    |-- ArdViewer.cs               ARD model discovery and scene conversion
    |-- Kh1ArdFormat.cs            ARD container parsing
    |-- Kh1MdlsFormat.cs           MDLS parsing
    |-- Kh1MfaFormat.cs            MFA and texture parsing
    |-- WebGlViewer.cs             WebGL viewer implementation
    |-- KH1 ARD Viewer.ico         Embedded Windows icon
    `-- KH1 ARD Viewer.png         Transparent icon source image
```

## Troubleshooting

### Windows reports that the .NET runtime is missing

Install the **.NET 10 Desktop Runtime for Windows x64**. Running the viewer
does not require the SDK; the SDK is required only to build it from source.

### The viewport cannot create an OpenGL context

Update or reinstall the graphics driver, then launch the viewer again. Remote
desktop sessions and virtual machines may expose limited OpenGL support.

### An archive opens but no models are listed

The archive may not contain model resources supported by this viewer, or its
model entries may be malformed. Try another KH1 ARD archive and check that the
file is complete.

### Export fails

Confirm that the application folder is writable. The output is created in the
`Extract\` directory beside the executable.

## Scope

KH1 ARD Viewer is a model inspection and export tool. It does not edit ARD
archives, modify game data, or provide a full game scene or animation editor.

## License

Original code and documentation are licensed under the BSD 3-Clause License.
This does not grant rights to Kingdom Hearts game content or trademarks. See
`LICENSE` for the license terms and third-party intellectual property notice.
