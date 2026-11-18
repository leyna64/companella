[![Build and Release](https://github.com/Leinadix/companella/actions/workflows/build-release.yml/badge.svg?branch=live)](https://github.com/Leinadix/companella/actions/workflows/build-release.yml)
[![CodeQL Advanced](https://github.com/Leinadix/companella/actions/workflows/codeql.yml/badge.svg?event=push)](https://github.com/Leinadix/companella/actions/workflows/codeql.yml)

## Website: https://www.erdbee.re/

---

# Important Update Notice

## Mandatory Full Re-download for Versions **below v5.69**

If you are currently running **any version lower than v5.69**, **incremental or delta updates are not supported**.

### What this means
- Your existing installation **cannot be updated safely**
- Continuing to use the built-in updater **will fail or cause issues**
- A **full re-download and reinstall is required**

### Required action
1. **Uninstall** the current application
2. **Download the latest release** from the official source
3. **Install fresh**

### Why this is necessary
Versions prior to **v5.69** use an outdated packaging and update layout that is **incompatible with the current update system**. This is a hard technical limitation.

**There is no workaround.**

---

![Companella](https://raw.githubusercontent.com/Leinadix/companella/refs/heads/live/Companella/icon.ico) 
> Credits to [Aracium](https://www.pixiv.net/en/users/94716338) for designing the icon!
# Companella

A powerful companion tool for osu!mania players and mappers. Track your progress, get personalized practice recommendations, create rate-changed beatmaps, and build marathon maps effortlessly.

> **Disclaimer:** Dan ratings are experimental and should not be considered accurate or trustworthy. They are still in development and require more data to be reliable.

---

## Download

**[Download the Installer](https://github.com/Leinadix/companella/releases/latest/download/CompanellaSetup.exe)**

Just download and run - the installer handles everything for you.

---

## Features

### Rate Changer

Create rate-modified versions of any beatmap instantly. Adjust the playback speed and pitch of maps for practice or challenge.

**Single Rate Mode:**
- Apply a specific rate multiplier (e.g., 0.9x, 1.1x, 1.5x)
- Customize the difficulty name format with variables like `{rate}`, `{bpm}`, `{original}`
- Instantly generates the rate-changed `.osu` and audio files

**Bulk Rate Mode:**
- Generate multiple rate versions in one click
- Set minimum rate, maximum rate, and step size (e.g., 0.8x to 1.3x in 0.05 steps)
- Quick presets for common ranges
- Preview all rates that will be generated before applying

![Rate Changer](Readme/companella-screen-rate-changer.png)

---

### Marathon Creator

Combine multiple beatmaps into a single marathon beatmap with professional-quality results.

**Map Management:**
- Add maps from your current selection
- Insert pause/break sections with customizable duration
- Reorder maps by dragging or using move buttons
- Set individual playback rates for each map
- View MSD (MinaCalc difficulty) values for each entry

**Custom Background Generation:**
- Creates a unique composite background from all included maps
- Each map's background appears as a radial "shard" in a pie-chart arrangement
- Add a center symbol using Greek letters (uppercase/lowercase) or special symbols (arrows, infinity, etc.)
- Apply glitch effects with adjustable intensity:
  - **RGB Shift** - Chromatic aberration effect
  - **Scanlines** - Retro CRT-style horizontal lines
  - **Wave Distortion** - Horizontal wave displacement
  - **Block Glitches** - Random displaced rectangular regions with optional color tinting

**Live Background Preview:**
- See exactly how the final background will look before creating
- Preview updates automatically when adding/removing maps
- Glitch effects and center symbol are shown in real-time
- Refresh button for manual preview updates

**Advanced Features:**
- Storyboard integration with per-map highlight transitions
- Audio crossfading between maps for smooth transitions
- SV (Scroll Velocity) normalization across different BPM sections
- Generates a detailed structure document listing all maps and timing

---

### Skills Analysis

Visualize your improvement over time with detailed skill trend graphs.

**Time Region Selection:**
- Choose analysis period: 24 hours, 7 days, 30 days, 90 days, or all time
- See how your skills have evolved over the selected period

**Skillset Tracking:**
- Track 8 distinct skillsets: Overall, Stream, Jumpstream, Handstream, Stamina, Jackspeed, Chordjack, Technical
- Color-coded charts for easy identification
- Current skill level display with trend indicators

**Trend Analysis:**
- See improvement or regression for each skillset
- Identify your strongest and weakest areas
- Get statistical summaries of your performance

![Skills Analysis](Readme/companella-screen-skill%20-analysis.png)

---

### Session Tracker

Monitor your practice sessions in real-time with detailed statistics.

**Live Tracking:**
- Automatic detection of completed plays
- Records accuracy, MSD difficulty, and timing for each play
- Start/stop button with session duration display

**Session Charts:**
- Real-time visualization of MSD and accuracy throughout your session
- See patterns in your performance over time
- Track total plays and average statistics

**Session History:**
- Review past practice sessions
- Compare performance across different days
- Identify your most productive practice times

---

### Smart Map Recommendations

Get personalized beatmap recommendations based on your current skill level and goals.

**Focus Modes:**
- **Push** - Maps slightly above your comfort zone to challenge yourself and improve
- **Consistency** - Maps at your current level to build reliable accuracy and confidence
- **Deficit Fixing** - Maps targeting your weaker skillsets to balance your skills
- **Skillset Focus** - Practice specific patterns or techniques (Stream, JS, HS, Stamina, Jack, CJ, Tech)

**Recommendation Features:**
- Analyzes your skill levels from session data
- Filters maps from your local collection
- Shows MSD values and dominant skillset for each recommendation
- One-click to load recommended map in osu!
- Quick restart button to reload the last played recommendation

---

### Session Planner

Plan structured practice sessions for maximum improvement.

**Session Modes:**
- **From Analysis** - Automatically generate sessions based on your skill analysis data
- **Manual Input** - Specify target skillset and difficulty level manually

**Session Structure:**
- **Warmup Phase** - Easier maps to get started
- **Ramp-Up Phase** - Progressive difficulty increase
- **Main Practice** - Maps at your target difficulty
- **Cooldown Phase** - Easier maps to finish

**Features:**
- Generates session plans with appropriate map selections
- Shows estimated session duration
- Preview maps before committing to the session
- Creates osu! collections for easy access to session maps

---

### Map Database & Indexing

Build a comprehensive database of your local beatmaps for recommendations and analysis.

**Indexing Options:**
- **Quick Index** - Index new maps only
- **Full Reindex** - Recalculate all map data
- **Refresh** - Update the database with latest changes

**Data Collected:**
- MSD (MinaCalc difficulty) values for all 4K mania maps
- Pattern analysis and classification
- Metadata (title, artist, creator, difficulty name)
- File paths for quick access

---

### Pattern Display

Visual analysis of beatmap patterns in real-time.

**Pattern Types Detected:**
- Stream, Jumpstream, Handstream
- Jack, Minijack, Chordjack
- Trill, Roll, Bracket
- Jump, Hand, Quad patterns

**Display Features:**
- Color-coded pattern visualization
- Distribution breakdown showing percentage of each pattern type
- BPM information for pattern context

---

### Dan Level Training

Practice for specific dan levels with targeted difficulty settings.

**20 Dan Levels:**
- Numeric dans: 1-10
- Greek letter dans: Alpha through Kappa

**Features:**
- See MSD requirements for each dan level
- Filter recommendations to match dan difficulty
- Track your progress toward dan goals

---

### Overlay Mode

Seamlessly integrates with your osu! gameplay experience.

**Window Attachment:**
- Automatically detects and attaches to osu! window
- Stays visible while playing
- Hides when switching to other applications

**Hotkey Control:**
- Toggle visibility with customizable hotkey (default: ALT+Q)
- Quick access without leaving osu!

**Positioning:**
- Adjustable overlay position (left, right, or detached)
- Remembers your preferred position

---

### Mapping Tools

Tools for beatmap creators and advanced users.

**BPM Analysis:**
- Automatically analyze audio to detect BPM
- Generate timing points from audio analysis
- Useful for timing new maps or verifying existing timing

**SV Normalization:**
- Normalize scroll velocity across BPM changes
- Makes maps with multiple BPM sections play more consistently
- Preserves intentional SV effects while fixing BPM-related issues

**Offset Adjustment:**
- Apply timing offset to all timing points
- Quick adjustment for map synchronization
- Precise millisecond control

---

### Settings & Customization

Personalize Companella to your preferences.

**UI Scale:**
- Adjustable interface scaling
- Support for different screen sizes and DPI settings

**Keybind Configuration:**
- Customize the overlay toggle hotkey
- Set preferred key combinations

**Analytics Settings:**
- Optional anonymous usage analytics
- Help improve Companella with aggregated data

**Session Settings:**
- Auto-start session tracking when osu! launches
- Configure default behavior

---

### Auto-Updates

Companella keeps itself up to date automatically.

**Update Features:**
- Checks for updates on startup
- Notifies you when new versions are available
- One-click update installation
- Seamless background updates

---

## Requirements

- Windows 10 or later
- osu! (stable) installed
- FFmpeg (included with installer, required for audio processing)

---

## Getting Started

1. Download and run the [Installer](https://github.com/Leinadix/companella/releases/latest/download/CompanellaSetup.exe)
2. Launch Companella
3. Open osu! - the tool will automatically connect and start detecting beatmaps
4. Index your map database for recommendations (Settings > Map Database)
5. Start practicing and watch your skills improve!

---

## Linux (osu-winello)

Companella! runs inside [osu-winello](https://github.com/NelloKudo/osu-winello)'s Wine prefix. One-time setup:

```bash
osu-wine --wine ~/Downloads/CompanellaSetup.exe
osu-wine --wine ~/.local/share/wineprefixes/osu-wineprefix/drive_c/users/"$USER"/AppData/Local/Companella/'Companella!.exe' --osu-winello
```

After that, start **Companella! (osu-winello)** from your app menu (or run `bash ~/.local/share/companella/osu-winello.sh`). It checks for Companella! updates, starts osu! through `osu-wine` if it is not running, then opens Companella! next to it.

Known limitations:
- With tosu/gosumemory enabled in osu-winello, closing osu! also closes Companella!.
- The tray icon and splash logo are missing (GDI+ does not load in the osu-winello prefix).

---

## Tips for Best Results

1. **Index your maps** - Run a full index to enable recommendations and analysis
2. **Track your sessions** - Start the session tracker before practicing for accurate skill tracking
3. **Use recommendations** - Let Companella guide your practice with smart map suggestions
4. **Check your trends** - Review skills analysis regularly to see your improvement
5. **Plan sessions** - Use the session planner for structured, effective practice

---

![Viewcounter](https://count.getloli.com/get/@leinadixosu)

---

## License

This project is provided as-is for personal use.
