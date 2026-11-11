# uTinyRipper
[![Download uTinyRipper](https://a.fsdn.com/con/app/sf-download-button)](https://sourceforge.net/projects/utinyripper/files)

[![Build status](https://ci.appveyor.com/api/projects/status/yd78hqp83f7vjkwb?svg=true)](https://ci.appveyor.com/project/mafaca/utinyripper)
[![Gitter](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/UtinyRipper/Lobby?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

uTinyRipper is a tool for extracting assets from serialized files (*CAB-*\*, *\*.assets*, *\*.sharedAssets*, etc.) and assets bundles (*\*.unity3d*, *\*.assetbundle*, etc.) and converting them into native Engine format.

Important note: work on this project has been suspended. The Author is going to restart it from scratch, please redirect development and focus on other things. So don't waste your time on PRs, propositions or complex issues. The only exception is bug fixes.

Supported versions: 1.x to 2019.x (since development is suspended, 2020.x or greater won't be supported)

## Export features
* Scenes
* Prefabs (GameObjects with transform components)
* AnimationClips (legacy, generic, humanoid)
* Meshes
* Shaders (native listing)
* Textures
* Audio
* Fonts
* Movie textures
* Materials
* AnimatorControllers
* Avatars
* Terrains
* TextAssets
* Components:
  * MeshRenderer
  * SkinnedMeshRenderer
  * Animation
  * Animator
  * Canvas
  * Light
  * ParticleSystem
  * Colliders
  * Rigidbody
  * AudioSource
  * Camera
  * MonoBehaviour (Mono only)
  * MonoScript (Mono only)

## Structure

* *uTinyRipperCore*

   Core library. It's designed as an single module without any third party dependencies.
   
* *uTinyRipperGUI*

   Basic graphic interface application. It has some extra converters, so additionally it exports:
   * AudioClip .wav export
   * Texture2D .png export (with Sprites)
   * Shader DirectX blob export
   * References to build-in Engine assets
   
* *uTinyRipperConsole* and *uTinyRipperConsoleNETCore*

   Sample console application which is designed to test Core library functionality.   
   It's a command line application. Drag and drop resource file(s) or/and folder(s) onto the .exe to retrieve the assets. It will then automaticly try to find resource dependencies, create a 'Ripped' folder and extract all supported assets into the created directory.
   As this is a sample application, I'm not going to improve it in any way.



### Requirements:

If you want to build a solution, you'll need:

 \- .NET Framework 4.7.2 + .NET Core 2.0 SDK

 \- Compiler with C# 7.3 syntax support (Visual Studio 2017)


If you want to run binary files, you need to install:

 \- [.NET Framework 4.7.2](https://support.microsoft.com/en-us/help/4054530/microsoft-net-framework-4-7-2-offline-installer-for-windows)
 
 \- [Microsoft Visual C++ 2015](https://www.microsoft.com/en-us/download/details.aspx?id=53840) Redistributables

 \- [Unity 2017.3.0f3 or greater](https://unity3d.com/get-unity/download/archive) (NOTE: your editor version must be no less than the game's version)
 


## JDecompiler development

This fork is being extended with cross-platform command-line support and an IL2CPP recovery pipeline.

### Linux command-line build

The cross-platform console project targets .NET 8:

```bash
dotnet build uTinyRipperConsole/uTinyRipperConsoleNETCore.csproj -c Release
dotnet run --project uTinyRipperConsole/uTinyRipperConsoleNETCore.csproj -- /path/to/unity/game
```

The console application no longer waits for a keypress after processing, so it can run from a Linux terminal and in automated scripts.

The legacy WPF GUI remains Windows-only. Use the console project on Linux.

### IL2CPP recovery roadmap

The intended Script Level 3 pipeline is:

1. Detect IL2CPP builds from Unity metadata and native runtime files.
2. Invoke Cpp2IL to analyze the IL2CPP metadata and native binary.
3. Recover managed assemblies and decompile the recovered IL into C# source.
4. Export namespaces, types, fields, properties and methods into organized `.cs` files.
5. Place recovered scripts into a Unity-compatible project structure and report unresolved or approximate code.

**Current status:** Linux console modernization is in progress. Cpp2IL invocation, automatic IL2CPP detection, and Script Level 3 source export are not implemented yet. Recovered source may require manual fixes and cannot be guaranteed to match the original source or compile unchanged.


## IL2CPP Script Recovery (Script Level 3 - initial implementation)

The console now exposes an IL2CPP recovery command. It detects common Unity IL2CPP layouts by locating `global-metadata.dat` and one native runtime binary (`GameAssembly.dll`, `libil2cpp.so`, or `GameAssembly.dylib`).

### Requirements

- A Cpp2IL command-line build available on PATH, or its executable path.
- ILSpy's `ilspycmd` command available on PATH, or its executable path.
- .NET 8 to run the JDecompiler console project.

Install ILSpy's command-line tool with:

```bash
dotnet tool install --global ilspycmd
```

Cpp2IL builds are published at the official [Cpp2IL releases page](https://github.com/SamboyCoding/Cpp2IL/releases). ILSpy CLI documentation is available in the [official ILSpy repository](https://github.com/icsharpcode/ILSpy/tree/master/ICSharpCode.ILSpyCmd).

### Usage

```bash
dotnet run --project uTinyRipperConsole/uTinyRipperConsoleNETCore.csproj -- --recover-il2cpp "/path/to/game"
```

Specify custom executable paths and an output directory when needed:

```bash
dotnet run --project uTinyRipperConsole/uTinyRipperConsoleNETCore.csproj -- --recover-il2cpp "/path/to/game" --cpp2il "/path/to/Cpp2IL" --ilspy "/path/to/ilspycmd" --output "/path/to/recovered"
```

The command runs Cpp2IL with the `dll_il_recovery` output format, then invokes ILSpy for each recovered non-framework managed assembly. Output is organized as:

- `Cpp2IL/`: Cpp2IL's recovered managed assemblies and analysis output.
- `Scripts/<AssemblyName>/`: decompiled C# files and project files generated by ILSpy.
- `recovery-report.json`: detected IL2CPP files, assembly counts, and failures.

If no output directory is supplied, files are written under `Ripped/<GameName>/IL2CPP/`.

### Current limitations

This is the first end-to-end integration layer. It relies on external Cpp2IL and ILSpy command-line tools rather than embedding their libraries. It does not reconstruct the exact original source code: names, control flow, generics, async methods, and some Unity-specific constructs may be incomplete or approximate. Some recovered C# projects may need references and manual fixes before they compile in Unity. The legacy uTinyRipper asset export pipeline remains separate from this IL2CPP script pipeline.
