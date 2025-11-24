# 🎵 Spotify Playlist to M3U8 Converter  
![Platform](https://img.shields.io/badge/Platform-Windows-blue) ![.NET](https://img.shields.io/badge/.NET-8.0+-purple) ![License](https://img.shields.io/badge/License-MIT-green) ![Downloads](https://img.shields.io/github/downloads/TypNull/SpotifyToM3U/total)

**Seamlessly bridge Spotify and your local music library!**  
This tool converts Spotify playlists (public or private) into **M3U8** playlist files, letting you enjoy your curated music offline in any player that supports M3U playlists (like VLC, Foobar2000, or Winamp).  

### 🔍 Why Use This Tool?  
- **Own your playlists**: Save Spotify playlists as local files, even if tracks get removed from Spotify.  
- **Works offline**: M3U8 files reference your existing local music files—no streaming required.  
- **Perfect for**:  
  - 🎧 Audiophiles with high-quality local copies of songs.  
  - 🚴 Offline listeners (travel, gym, etc.).  
  - 🗄️ Archivists preserving playlist orders.  

---

## ✨ Why SpotifyToM3U?

🎯 **Perfect for music enthusiasts who want the best of both worlds**

| Problem | Solution |
|---------|----------|
| 😰 **Spotify removes songs** | ✅ Keep local backups as M3U playlists |
| 🎧 **Want better audio quality** | ✅ Use your FLAC/high-bitrate files |
| ✈️ **Going offline** | ✅ M3U works without internet |
| 🎛️ **Prefer other players** | ✅ Works with VLC, foobar2000, Winamp, etc. |
| 📱 **Own your music** | ✅ No subscription needed to play |

---

<img align="center" src="https://github.com/user-attachments/assets/999c08ea-78ed-4be9-bf4f-dd60bfe4641e" alt="App Preview" width="600"/>

---

## 📥 Installation  

1. **Ensure [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) is installed**.  
2. Download the latest release from the [Releases page](#).  
3. Run the installer and follow the prompts.  

---

## 🚀 Usage  

1. **Launch the app** and log in (optional for private playlists).  
2. **Add your music folder** (where local files are stored).  
3. **Paste a Spotify playlist URL** (public or private).  
4. Click **"Convert"** to generate the M3U8 file.  
5. **Export and play** in VLC, Foobar2000, or any M3U8-compatible player.  

<img align="center" src="https://github.com/user-attachments/assets/cfed990d-bc6d-4b25-a68c-8b9536281d2c" alt="Conversion Preview" width="600"/>

---

## 📂 Supported Library Structures

The app uses a **smart matching algorithm** that works with multiple library organization styles:

| Structure | Example | Support |
|-----------|---------|---------|
| **ID3 Tags** (recommended) | Any filename with proper metadata | ✅ Primary method |
| **Artist - Title** filename | `Pink Floyd - Comfortably Numb.mp3` | ✅ Fully supported |
| **Artist/Album/Track** folders | `Pink Floyd/The Wall/01 Comfortably Numb.mp3` | ✅ Fully supported |
| **Track Number + Title** | `01 Comfortably Numb.mp3` | ✅ Supported with folder context |

The matching algorithm prioritizes embedded ID3 tags but automatically falls back to folder/filename parsing when tags are missing.

---

## 🔧 Troubleshooting

- **Missing tracks?** The app matches using ID3 metadata first, then folder structure. Ensure your files have either:
  - Proper ID3 tags (Title, Artist, Album), OR
  - Organized folder structure like `Artist/Album/## Title.mp3`
- **Login issues?** Re-authenticate via Spotify's OAuth prompt.
- **File not found?** Check your selected music folder path.
- **Low confidence matches?** Files with both good ID3 tags AND organized folders get the highest match scores.

Report bugs on the [Issues page](https://github.com/TypNull/SpotifyToM3U/issues).  

---


## 📜 License & Credits

**MIT License** © 2025 [TypNull](https://github.com/TypNull)

---

<div align="center">

**Made with ❤️ for music lovers everywhere** 🎶

[⬆️ Back to Top](#-spotifytom3u-converter) • [📥 Download](https://typnull.github.io/SpotifyToM3U/SpotifyToM3U.application) • [🐛 Report Issue](https://github.com/TypNull/SpotifyToM3U/issues) • [💬 Discussions](../../discussions)

</div>