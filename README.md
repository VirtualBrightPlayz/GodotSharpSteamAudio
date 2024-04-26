# Godot C# Steam Audio

A Steam Audio integration for Godot 4.2 using C#.

`LICENSE.md` applies to:

- `addons/DynamicAudioBus/AudioBus.cs`
- `addons/gd_steam_audio/GDSteamAudio.cs`
- `addons/gd_steam_audio/SteamAudioCollider.cs`
- `addons/gd_steam_audio/SteamAudioPlayer.cs`


## Pros/Cons

### Pros

- It works on Godot Engine 4.2 Stable, with no engine patches or forks
- It supports C# nicely

### Cons

- It requires the Mono/.NET builds of Godot 4.2
- Relatively high audio latency (possibly, not fully tested)
- Not 100% stable
- Supports only static geometry (as of now)
- Allocates huge amounts of memory (I think)
- As of now, only one attenuation function works
- As of now, there is only the standard/stock Steam Audio Material Presets

## Before you Install

Just know that...

- This modifies the Audio Bus Layout for each `SteamAudioPlayer`. Use names and not indexes on your Audio Buses or use the `AudioBus` class provided in the source code.
- This addon *can* sometimes glitch and get loud for short periods of time (less than 1s, but still loud)
- Pausing an audio source is not supported (yet)
- Custom bus outputs are not well tested
- This software likely has some bugs

## Install

- Copy the `addons` folder from this repository into your project
- Install the binary from [Steam Audio Releases](https://github.com/ValveSoftware/steam-audio/releases) into `addons` (don't put the `dll` or `so` in a subfolder; use 64-bit binaries when Godot Engine is 64-bit)
- - **When exporting a project, these binaries need to be placed next the game executable**
- Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to your `.csproj` in the right place
- Under Project -> Project Settings
- - Enable Advanced settings
- - Under Audio -> Driver
- - - Set Mix Rate to `48000`
- - - Save and Restart

## Scene Setup

- Add a `Node` in your project which has the script `res://addons/gd_steam_audio/GDSteamAudio.cs`
- - **This node should exist at all times, and there should only be one at any given time**
- - Fill out the `camera` property from the inspector or script
- - - If this is not filled out, Steam Audio won't work.
- Add a `Node` with the script `res://addons/gd_steam_audio/SteamAudioCollider.cs` as a child of your `CollisionShape3D` with `shape` being a `ConcavePolygonShape3D`
- Add a `Node` with the script `res://addons/gd_steam_audio/SteamAudioPlayer.cs` as a child of your `AudioStreamPlayer3D`

## Credits

[Valve Software's Steam Audio](https://valvesoftware.github.io/steam-audio/)

[Mirsario's SteamAudio.NET wrapper](https://github.com/Mirsario/SteamAudio.NET)

["Unity" by Kevin MacLeod](https://incompetech.com/)

```
"Unity" Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0 License
http://creativecommons.org/licenses/by/4.0/
```
