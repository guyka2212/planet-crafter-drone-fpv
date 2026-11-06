# MODLOG: Drone FPV for The Planet Crafter

## Intake
- Game: **The Planet Crafter Demo** (Steam app 1754850, build 21132660). The full game (1284190) is not in this Steam library.
- Idea: a flyable FPV drone (keyboard WASD + arrows, or a controller), with a Blender-made 3D model and its own HUD while flying.
- Single-player, no anti-cheat found by `um scan`.

## Recon
- Unity 6000.0.59f2, Mono backend, URP. `Planet Crafter_Data/Managed/Assembly-CSharp.dll`, namespace `SpaceCraft`.
- Saves/logs: `C:\Users\User\AppData\LocalLow\MijuGames\Planet Crafter` (backed up: `C:\Users\User\.universal-modder\backups\planetcrafter-demo-saves\20261106-120742.zip`).
- Input: new Input System (`PlayerInputDispatcher` with an `InputActionAsset`).

## Route
BepInEx 5.4.23.6 (Mono) plugin. Copied from the user's existing install in `Gunk` (no download).
Added to the game folder: `winhttp.dll`, `doorstop_config.ini`, `changelog.txt`, `BepInEx/`.
**Uninstall:** delete those four things.

## Game classes used (read with a Mono.Cecil dumper, `~/planetcrafter-decomp`)
- `Managers.GetManager<PlayersManager>().GetActivePlayerController()` returns the local `PlayerMainController`.
- `PlayerMainController.GetPlayerLookable()`; `PlayerLookable.m_Camera` (private field) is the player camera transform.
- `PlayerCanAct.SetCameraBlocked(true)` is the photo-mode freeze: it blocks move, look, aim and action, and keeps the cursor locked.
  `GetCanMove()` is checked by PlayerMovable, PlayerLookable, PlayerActionController, PlayerAimController and PlayerMultitool.
- `Managers.GetManager<VisualsToggler>()`: `SetUi(bool show)` and `SetHand(bool show)` hide or show the HUD and the first-person hand.
- `Managers.GetManager<WindowsHandler>().GetHasUiOpen()` reports whether an inventory or menu window is open.
- `PlayerGaugesHandler.GetPlayerOxygenValue/HealthValue/ThirstValue()` feed the pilot vitals on the drone HUD.

## Build
- No .NET SDK on this PC, and downloading its installer was blocked. Built with `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
  (C# 5), using `-nostdlib -noconfig` and the game's own `mscorlib`/`netstandard`. See `build.sh` (a response file handles the paths with spaces).

## Blender
- Two MCP add-ons were both bound to 9876 (Blender Lab `mcp` extension plus the community `blender_mcp.py`). The Claude MCP server timed out.
- Worked around it by talking to the Lab add-on directly: send `{"type":"execute","code":...,"strict_json":false}` as NUL-terminated JSON.
- The model is built by `tools/blender_build_drone.py` in its own scene "DroneFPV" and exported to OBJ with Y up / -Z forward. The nose is at Blender -Y, which becomes Unity +Z.
- The OBJ loader mirrors X and reverses the triangle winding. Each `o` object's pivot is its bounds centre. `prop*` objects spin; `LED*` materials blink.

## Verified in game (2026-11-06)
- BepInEx loads the plugin, `drone.obj` loads (53 parts, 14698 verts, 4 props), and the shader is `Universal Render Pipeline/Lit`. 0 exceptions in Player.log.
- The drone HUD renders: heading tape, horizon, speed and altitude, battery, link, home arrow, REC, pilot vitals and help.
- It flies: the log shows ~12 m/s forward, climbing, yaw, and analog gamepad input. Switching between drone and body works, and the body walks normally while the drone is parked.

## Gotchas
1. **F8 is the game's Feedback key.** Toggle moved to **V**. The plugin logs the game's keyboard bindings once and warns about clashes:
   R = RotateObject, F9 = Steam wishlist, C = BlockView. Defaults are now Recall X, Camera B, Speed N, Help H.
2. `PlayerMainController.transform` is NOT the body. Use the camera's original parent ("Player 0 (local player)").
3. Rigidbody added before the transform was moved stayed at the world origin, so the drone spawned at (0,0,0).
   Set the position first, then `AddComponent<Rigidbody>()`, then `rb.position`.
4. `SetChase` ran before `state = Flying` and returned early, so the camera kept the player's +1.88 m head offset. Fixed by setting state first; this fix is built but not yet tested in game.
5. The game DLL is locked while the game runs, so close the game before `build.sh`.
6. Unfree keyboard keys (from the dump): WASD, arrows, Space, Shift, Ctrl, Y, U, Tab, Q, Esc, R, T, F, F1, F2, F4, F8, F9, C, M, Enter, G, 1-9.
7. The game font has no subscript glyphs (₂ renders as a box), so the HUD writes O2 / H2O.
