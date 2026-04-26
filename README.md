# Voxta Modules by Aios

This repository contains experimental Voxta chat augmentation modules.

## Modules

### Spotify

Adds Spotify playback control to Voxta chats. It can connect to Spotify through OAuth and lets the character respond to user requests such as playing music, pausing or resuming playback, queueing tracks, changing volume, seeking, skipping, toggling shuffle or repeat, liking the current track, listing playlists/devices, adding the current track to a playlist, and transferring playback to another device.

Configuration requires a Spotify Developer application with a Client ID, Client Secret, and Redirect URI. The default redirect URI is:

```text
http://127.0.0.1:5384/api/extensions/spotify/oauth2/callback
```

### OpenWeather

Adds weather and air quality lookups to Voxta chats using OpenWeather. It can fetch current weather, weather forecasts, current air pollution, air pollution forecasts, and generated weather map images for global, continent, or country views.

Configuration requires an OpenWeather API key. You can also set a default location, preferred units, which weather and pollution details should be included, and the tile cache path used for generated map images.

### Philips Hue

Adds Philips Hue smart lighting control to Voxta chats. It can turn lights, smart plugs, rooms, zones, or groups on and off, change light colors, change brightness, activate scenes, list available Hue targets, and optionally let a character control a configured light based on emotion or scene context.

Configuration stores Hue authentication data locally and can target a character-controlled light, room, zone, or group.

## Requirements

- Voxta Server with module support.
- .NET SDK 10.0 or newer, matching the projects' `net10.0` target framework.
- Any .NET-compatible editor or IDE. Visual Studio is not required; the examples below use the .NET CLI.
- Service-specific accounts or hardware:
  - Spotify: Spotify account and Spotify Developer application.
  - OpenWeather: OpenWeather API key.
  - Philips Hue: Philips Hue Bridge and lights/plugs on the same network as the Voxta Server.

Check your installed SDKs with:

```powershell
dotnet --list-sdks
```

## Build

Clone the repository:

```powershell
git clone <repository-url>
cd voxta-providers
```

You can build all modules at once with the solution file. The `.sln` file is only a convenient project grouping file; it is not required to build the modules.

```powershell
dotnet restore .\vx-aios-providers.sln
dotnet build .\vx-aios-providers.sln -c Release
```

If your tool does not use `.sln` files, or if you only want one module, build the module project directly:

```powershell
dotnet build .\Voxta.Modules.Aios.Spotify\Voxta.Modules.Aios.Spotify.csproj -c Release
dotnet build .\Voxta.Modules.Aios.OpenWeather\Voxta.Modules.Aios.OpenWeather.csproj -c Release
dotnet build .\Voxta.Modules.Aios.PhilipsHue\Voxta.Modules.Aios.PhilipsHue.csproj -c Release
```

The build output for each module is under:

```text
<ModuleProject>\bin\Release\net10.0\
```

For example:

```text
Voxta.Modules.Aios.Spotify\bin\Release\net10.0\
```

## Install

Stop Voxta Server before copying module files.

### Recommended: automatic copy while building

Set the `VoxtaModulesPath` environment variable to your Voxta Server `Modules` folder, then build:

```powershell
$env:VoxtaModulesPath = "C:\Path\To\Voxta Server\Modules"
dotnet build .\vx-aios-providers.sln -c Release
```

The projects copy their runtime files into `VoxtaModulesPath` after build.

### Manual copy

If you copy files manually, do not copy only `Voxta.Modules.Aios.<Module>.dll`. These modules can also require a `.deps.json` file and additional dependency DLLs.

Copy the complete runtime output for each module you want to install from:

```text
<ModuleProject>\bin\Release\net10.0\
```

to:

```text
<Voxta Server installation>\Modules\
```

At minimum, copy:

- The module DLL, for example `Voxta.Modules.Aios.Spotify.dll`.
- The module `.deps.json` file if it exists, for example `Voxta.Modules.Aios.Spotify.deps.json`.
- All dependency DLLs produced next to the module DLL, for example `SpotifyAPI.Web.dll`, `SixLabors.ImageSharp.dll`, `SixLabors.ImageSharp.Drawing.dll`, `SixLabors.Fonts.dll`, `HueApi.dll`, and `HueApi.ColorConverters.dll`.

PowerShell example for one module:

```powershell
$module = "Voxta.Modules.Aios.OpenWeather"
$voxtaModules = "C:\Path\To\Voxta Server\Modules"
Copy-Item ".\$module\bin\Release\net10.0\*" $voxtaModules -Recurse -Force
```

Repeat that copy for every module you want to install.

## Enable in Voxta

1. Start Voxta Server.
2. Open the Voxta `Modules` menu.
3. Add the installed module.
4. Configure the module settings under `Chat Augmentations`
5. Assign the augmentation to your character in the `Configuration` tab when inside the `Character edit` view before starting a chat 

## Troubleshooting

- If a module does not appear, confirm all files from the module output folder were copied to the Voxta `Modules` folder.
- If Voxta reports a missing assembly, rebuild the module and copy the dependency DLLs from the same `bin\Release\net10.0` folder.
- If Spotify authentication fails, verify that the Redirect URI in the Spotify Developer Dashboard exactly matches the value configured in Voxta.
- If OpenWeather requests fail, verify the API key is active. New OpenWeather keys can take some time before they work.
- If Philips Hue requests fail, verify the Hue Bridge is reachable from the Voxta Server machine and that authentication has completed.
