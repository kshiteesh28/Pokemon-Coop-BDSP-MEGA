# PKLumiHex for macOS

Native macOS desktop UI (Avalonia + .NET 8) built directly on [`TalonSabre/PKLumiHex`](https://github.com/TalonSabre/PKLumiHex)'s cross-platform `PKHeX.Core`.

## Features
- Open and save Luminescent Platinum `SaveData.bin` files (automatically writes a `.bak` backup on save).
- **Party & Boxes Editor**: Edit nickname, species, form, level, held item (with quick buttons for Charizardite X `660` and Charizardite Y `678`), ability, shiny flag, moves, IVs, and EVs.
- **Bag Editor**: View/edit pockets and quantities, plus a one-click **Grant Mega Kit** button (adds Key Stone `773`, Charizardite X `660`, and Charizardite Y `678`).
- **Trainer Info**: Edit OT name, money, and language.

## Build via GitHub Actions (No Local .NET SDK Required)
Push to `feature/pklumihex-mac` or run the **Build PKLumiHex macOS App** workflow from the Actions tab, then download the **`PKLumiHex-macOS-arm64`** artifact and unzip `PKLumiHex.app`.

## Build Locally on macOS
```bash
brew install --cask dotnet-sdk
chmod +x tools/PKLumiHex.Mac/build-mac-app.sh
./tools/PKLumiHex.Mac/build-mac-app.sh osx-arm64
```
The bundled app is written to `tools/PKLumiHex.Mac/dist/PKLumiHex.app`.
