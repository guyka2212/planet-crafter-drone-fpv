# Drone FPV: a flyable camera drone for The Planet Crafter

Launch a small quadcopter from your hand and fly it in first person (or a chase cam) with its own drone HUD.
Your astronaut stays where you left them, still breathing, so keep an eye on the pilot vitals.

![drone](assets/drone_preview.png)

## Flight modes
- **ACRO (default)**: real freestyle flying. The sticks set rotation *rates*, throttle pushes along the drone's own
  up axis, gravity always pulls, and nothing levels the drone for you. Flips, rolls, power loops and dives all work.
  The FPV camera is hard-mounted with a 25° uptilt.
- **ANGLE**: self-levelling, easy mode. The drone holds its position when you let go.

Switch with **K** (or D-pad ←). When you go back to your body, a parked drone always levels out and hovers.

## Controls: ACRO

| Action | Keyboard | Controller |
|---|---|---|
| Throttle | **W / S** move a throttle lever that stays where you leave it | **RT** (or left stick in Mode 2, see config) |
| Punch 100% / motor cut | **Space** / **Left Ctrl** | **RB** / **LB** |
| Yaw | **A / D** | left stick X |
| Pitch (nose down / up) | **↑ / ↓** | right stick Y |
| Roll | **← / →** | right stick X |
| Full stick rate (flips) | hold **Shift** (keys push 45% otherwise) | |
| Camera uptilt | **PgUp / PgDn** (5° steps) | |

Hover is at about 40% throttle (the HUD shows THR %).

## Controls: ANGLE and general

| Action | Keyboard | Controller |
|---|---|---|
| Launch drone / back to body / re-enter drone | **V** | **L3 + R3** (both sticks pressed) |
| Move forward / back / strafe (ANGLE) | **W S A D** | left stick |
| Up / down (ANGLE) | **↑ / ↓** or **Space / Left Ctrl** | **RT / LT** |
| Turn (ANGLE) | **← / →** or mouse | right stick X |
| Boost / speed mode (ANGLE) | **Left Shift** / **N** | **A** / D-pad → |
| ACRO ↔ ANGLE | **K** | D-pad ← |
| FPV / chase camera | **B** | D-pad ↑ |
| Recall drone | **X** | D-pad ↓ |
| Show / hide help | **H** | |

All keys, rates (670°/s), expo, thrust-to-weight (4.5 g), drag, uptilt, range, battery time, mouse sensitivity and FOV can be changed in
`BepInEx/config/guy.planetcrafter.dronefpv.cfg` (created after the first launch).

## The drone HUD
While flying, the game's HUD and hand are hidden and the drone's OSD takes over:
- a heading tape and compass;
- an artificial horizon that rolls with the drone;
- speed and vertical speed;
- altitude above ground and relative to you;
- battery, link signal with static as you near max range (400 m), and a home arrow with distance;
- a REC timer, coordinates and the speed and camera modes;
- your astronaut's O₂ / HP / H₂O;
- warnings: low battery, weak signal, obstacle ahead, pilot vitals critical.

The battery drains as you fly, faster with boost, and recharges while the drone is recalled.
When you press V to go back to your body, the drone keeps hovering where you left it. Press V again to take it back, or X to pack it away.

## Install
**Easiest:** install [Planet Crafter Mod Menu](https://github.com/guyka2212/planet-crafter-mod-menu), open **Mods** in the
main menu, paste `guyka2212/planet-crafter-drone-fpv` and press Install.

**By hand:**
1. Install BepInEx 5 (x64, Mono) in the game folder.
2. Copy `DroneFPV.dll`, `drone.obj` and `drone.mtl` to `BepInEx/plugins/planet-crafter-drone-fpv/`.

Or run `./build.sh`, which compiles the plugin and installs it into the Steam copy.

## Remove
Delete `BepInEx/plugins/planet-crafter-drone-fpv`. To remove BepInEx too, delete `winhttp.dll`, `doorstop_config.ini`,
`changelog.txt` and `BepInEx/` from the game folder.

## Rebuilding the model
`tools/blender_build_drone.py` builds the drone in its own Blender scene ("DroneFPV") and the export step writes
`assets/drone.obj`. Objects named `prop*` spin in game; materials named `LED*` glow and blink.

Built with Claude Code (AI-assisted). Uses BepInEx. No game files are included.
