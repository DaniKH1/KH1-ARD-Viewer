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
- Expand or collapse each archive's model list, then select a model for viewing.
- Render MDLS and MFA geometry in a native OpenGL viewport.
- Orbit, zoom, and pan around the model, with a ground grid for scale and
  orientation.
- View mesh, vertex, triangle, and texture counts for the selected model.
- Browse decoded model textures and open a larger texture preview.
- Export the selected model to a glTF 2.0 file with separate PNG textures.
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

The viewer does not automatically choose a model. Select an individual MDLS or
MFA entry to activate the viewport, statistics, texture gallery, and Export
button. On later launches, it automatically scans and loads ARD files from the
last selected folder.

## Controls

| Action | Control |
| --- | --- |
| Orbit around the model | Drag with the mouse |
| Zoom in or out | Mouse wheel |
| Pan the view | Hold Shift while dragging, or drag with the middle mouse button |
| Show or hide an archive's models | Select the arrow beside the archive name |
| Restore the default view | Select **Reset camera** |
| Inspect a texture | Select a texture thumbnail |
| Close the enlarged texture preview | Press Escape or click outside the preview |

## Exporting

Select a model and choose **Export**. KH1 ARD Viewer writes a glTF 2.0 JSON file
under the application directory:

```text
Extract\<model>_extracted\<model>.gltf
```

The geometry buffer is embedded in the `.gltf` file. Each decoded texture is
saved as a separate PNG beside it, and referenced by a relative filename in
the glTF. Model names are sanitized before they are used as directory and file
names.

The application directory must be writable to export there. If Windows
restricts writes to the folder where the viewer was installed, move the
complete folder to a user-writable location before exporting.

## Supported archive data

KH1 ARD Viewer targets Kingdom Hearts 1 ARD archives and their supported model
resources:

- **MDLS**: model geometry, decoded textures, mesh texture assignments, and
  available joint transforms.
- **MFA**: geometry, vertex colors, and decoded PlayStation 2 GS textures,
  using the MFA parsing and texture-decoding logic refined against the KH1
  Unity importer.

The two formats use different geometry and texture conventions. The viewer
keeps their UV handling separate: MDLS textures receive the row orientation
needed by the renderer, while MFA UV coordinates and decoded texture rows are
preserved. Unsupported or malformed resources may be skipped; an archive can
open successfully even if it contains no usable MDLS or MFA models.

## Build from source

The complete source project and icon assets are in `Data\`. Building requires
the .NET 10 SDK for Windows x64.

To build the viewer without launching it, run:

```text
Build.bat
```

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

## GitHub Actions builds and releases

To build and publish a release, open the repository's **Actions** tab, select
**Release Manual Executable**, and choose **Run workflow**. Optionally enter a
version tag beginning with `v` (for example, `v1.0.0`); if left empty, the
workflow uses the current date. The executable is built first, then the release
is published automatically by the second job.

The workflow also keeps a downloadable build artifact on the Actions run. The
published `KH1 ARD Viewer.exe` is framework-dependent and requires the .NET 10
Desktop Runtime for Windows x64.

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
