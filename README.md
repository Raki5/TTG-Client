# TTG Client — .NET / WPF Phase 1

TTG Client is a Windows desktop Minecraft mod browser built with **C# + .NET 10 + WPF**. Phase 1 focuses on finding and inspecting mods from Modrinth and CurseForge. It is not yet a Minecraft launcher and does not download/install mods into instances.

## Included in Phase 1

- Dark TTG Client desktop layout and persistent navigation.
- Browse source tabs: All sources, Modrinth, and CurseForge.
- Search terms, pagination, Minecraft version, loader, category, environment, and sort filters.
- Modrinth API v2 project search with server-side facets.
- CurseForge REST API mod search with game version, loader, sort and mapped category filters.
- Project detail window with metadata and a button that opens the official project page.
- Settings page for a CurseForge API key.
- Local key storage protected with Windows DPAPI for the current Windows user.
- `CURSEFORGE_API_KEY` environment variable support; it takes priority over the saved key.
- Windows x64 self-contained publish script.

## Requirements

- Windows 10 or Windows 11 (64-bit).
- .NET 10 SDK for running/building the source project.
- Internet access for API calls.
- Your own CurseForge API key if you want to search CurseForge. Modrinth public search works without a key.

## Run the app from source

1. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
2. Extract this folder on Windows.
3. Double-click `run.bat`, or run:

   ```powershell
   dotnet run --project .\TTGClient.csproj
   ```

On first run, NuGet restores any project dependencies. The Windows-targeted .NET 10 framework provides the DPAPI APIs used to protect the saved key, so no separate ProtectedData package reference is needed.

## Set up CurseForge

1. Obtain an API key using the CurseForge developer process: https://docs.curseforge.com/rest-api/
2. Open **Settings** in TTG Client.
3. Paste the key and choose **Save key**.

The key is encrypted using Windows DPAPI and saved under `%LOCALAPPDATA%\TTGClient\settings.json`. It can be decrypted only in the same Windows user context. Alternatively, set the `CURSEFORGE_API_KEY` environment variable before starting the application. The environment variable overrides the saved key.

Do not publish your API key or commit it to source control. This client-side key setup is intended for personal use. A public multi-user release would require a carefully designed backend and review of the API provider's terms and key-sharing rules.

## Publish as a standalone Windows x64 app

Run `publish-win-x64.bat`. It invokes `dotnet publish` to create a self-contained Windows x64 app under `publish\win-x64\`. The first publish needs an internet connection for NuGet restore and may take longer than launching the source project.

The output folder contains the published application. Test it on a clean Windows machine before distributing it. Because it is self-contained, recipients do not need to separately install the .NET runtime, but a Windows x64 environment is required.

## Important API behavior

- Modrinth's search endpoint supports project search facets, including project type, categories/loaders, game versions, and environments. Supported sort options are used as provided by the API.
- CurseForge's search requires `x-api-key`, uses Minecraft's game ID and the mod class ID, and supports game-version and loader filtering. Its loader filter requires a selected game version. Categories are resolved against CurseForge's category endpoint.
- The CurseForge mod-search endpoint does not expose an equivalent environment filter; the UI shows a warning rather than claiming that filter was applied to CurseForge results.
- “All sources” requests both APIs and combines their results. Relevance ranking isn't directly comparable across different providers, so the combined relevance view keeps the provider result order; selectable sort options are combined locally when applicable.
- Rate limits, API key permissions, transient network problems, and provider-side response changes can affect results. Error messages are shown in the app.

## Planned future phases

- **Phase 2:** Create/manage instances, choose game version and loader version, detect Java, and configure RAM.
- **Phase 3:** In-app downloads, installation, dependency resolution and per-instance installed-mod lists.
- **Phase 4:** Microsoft account authentication, launch Minecraft, diagnostics and client updates.

## Project layout

```text
TTGClient.csproj
App.xaml
MainWindow.xaml
MainWindow.xaml.cs
Models/
  AppSettings.cs
  ModProject.cs
Services/
  SettingsService.cs
  ModrinthService.cs
  CurseForgeService.cs
Converters/
  ImageUrlConverter.cs
Views/
  ProjectDetailsWindow.xaml
  ProjectDetailsWindow.xaml.cs
run.bat
publish-win-x64.bat
```
