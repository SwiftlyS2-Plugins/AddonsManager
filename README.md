<div align="center">
  <img src="https://pan.samyyc.dev/s/VYmMXE" />
  <h2><strong>Addons Manager</strong></h2>
  <h3>No description.</h3>
</div>

<p align="center">
  <img src="https://img.shields.io/badge/build-passing-brightgreen" alt="Build Status">
  <img src="https://img.shields.io/github/downloads/SwiftlyS2-Plugins/AddonsManager/total" alt="Downloads">
  <img src="https://img.shields.io/github/stars/SwiftlyS2-Plugins/AddonsManager?style=flat&logo=github" alt="Stars">
  <img src="https://img.shields.io/github/license/SwiftlyS2-Plugins/AddonsManager" alt="License">
</p>

## Building

- Open the project in your preferred .NET IDE (e.g., Visual Studio, Rider, VS Code).
- Build the project. The output DLL and resources will be placed in the `build/` directory.
- The publish process will also create a zip file for easy distribution.

## Publishing

- Use the `dotnet publish -c Release` command to build and package your plugin.
- Distribute the generated zip file or the contents of the `build/publish` directory.

## Commands

```
sw_downloadaddon <workshop_id> # Download a workshop addon via command
```

## Adding Addons

- To add an addon for players to download, modify `addons/swiftlys2/configs/plugins/AddonsManager/config.jsonc` at key `Main.Addons` with Workshop ID's:

```jsonc
{
  "Main": {
    "Addons": [
      "WORKSHOP_ID1",
      "WORKSHOP_ID2", 
      // ...
    ],
    // ...
  }
}
```

## Acknowledgements

This plugin is a port of Source2ZE's MultiAddonManager for SwiftlyS2. It is released under GPL with credits given to the original code writers.
