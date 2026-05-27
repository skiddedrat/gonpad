# GoonPad Soundboard

A modern, feature-rich C# WPF soundboard application with a sleek dark theme UI and powerful audio playback capabilities.

## Features

- **Import Multiple Audio Files**: Auto-generates playable pads for MP3, WAV, OGG, FLAC, AAC, M4A, and WMA files
- **Multi-Play Toggle**: Choose between Single mode (one sound at a time) or Layered mode (stack multiple sounds)
- **Concurrent Playback Limit**: Configurable from 1 to 100 simultaneous sounds
- **Output Device Selector**: Choose which audio output device to use
- **Microphone Selector**: Select input device for future mic-routing features
- **Bass & Treble Controls**: Per-playback EQ with low-shelf and high-shelf filters (-12 dB to +12 dB)
- **Save/Load Profiles**: Complete layout + settings saved as JSON
- **Stop-All Control**: Instantly clear all active sounds
- **Keyboard Shortcuts**: Auto-assigned F1-F12 and 1-9 keys, plus custom key binding
- **Modern Dark Theme**: Sleek UI with customizable sound pads

## Build & Run

### Prerequisites
- .NET 8.0 SDK
- Windows (WPF requires Windows)

### Commands
```bash
dotnet restore
dotnet run --project GoonPad.Soundboard/GoonPad.Soundboard.csproj
```

## Usage

1. **Import Sounds**: Click "📁 Import Sounds" to select audio files from your computer
2. **Play Sounds**: Click on pads or press assigned keyboard shortcuts (F1-F12, 1-9)
3. **Customize Pads**: Right-click any pad to:
   - Remove the pad
   - Rename it
   - Bind a custom keyboard shortcut
4. **Adjust EQ**: Use Bass and Treble sliders at the bottom to adjust audio output
5. **Save Profile**: Click "💾 Save Profile" to save your current setup as JSON
6. **Load Profile**: Click "📂 Load Profile" to restore a previously saved configuration
7. **Stop All**: Click "⏹ Stop All" to immediately stop all playing sounds

## Audio Notes

- Uses **NAudio** for decoding, playback, and endpoint discovery
- EQ is applied per playback stream using low-shelf/high-shelf BiQuad filters
- Mic selector included for UX/settings and future direct mic-routing expansion

## Project Structure

```
GoonPad.Soundboard/
├── App.xaml              # Application resources and styles
├── App.xaml.cs           # Application entry point
├── MainWindow.xaml       # Main UI layout
├── MainWindow.xaml.cs    # Main window logic
└── GoonPad.Soundboard.csproj  # Project file
```

## Technologies Used

- **WPF** (Windows Presentation Foundation)
- **.NET 8.0**
- **NAudio** - Audio processing library
- **System.Text.Json** - Profile serialization
