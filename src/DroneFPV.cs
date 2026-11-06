// Drone FPV for The Planet Crafter (BepInEx 5 plugin).
// Written in C# 5 so it builds with the csc.exe that ships with Windows (.NET Framework 4.x).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DroneFPV
{
    [BepInPlugin("guy.planetcrafter.dronefpv", "Drone FPV", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string Folder;
        internal static ConfigEntry<Key> KeyToggle, KeyRecall, KeyCamera, KeySpeed, KeyHelp;
        internal static ConfigEntry<float> MaxRange, BatteryMinutes, MouseSensitivity, Fov;
        internal static ConfigEntry<bool> DebugLog;

        void Awake()
        {
            Log = Logger;
            Folder = Path.GetDirectoryName(Info.Location);
            KeyToggle = Config.Bind("Keys", "ToggleDrone", Key.V, "Launch the drone / switch between the drone and your body. (F8 is the game's feedback key.)");
            KeyRecall = Config.Bind("Keys", "RecallDrone", Key.X, "Recall (pack away) the drone.");
            KeyCamera = Config.Bind("Keys", "CameraMode", Key.B, "Switch FPV / chase camera while flying.");
            KeySpeed = Config.Bind("Keys", "SpeedMode", Key.N, "Cycle speed mode (CINE / NORMAL / SPORT).");
            KeyHelp = Config.Bind("Keys", "ToggleHelp", Key.H, "Show or hide the control hints.");
            MaxRange = Config.Bind("Drone", "MaxRangeMeters", 400f, "How far the drone can fly from you.");
            BatteryMinutes = Config.Bind("Drone", "BatteryMinutes", 8f, "Flight time on a full battery at normal speed.");
            MouseSensitivity = Config.Bind("Drone", "MouseSensitivity", 0.12f, "Mouse yaw / camera tilt sensitivity.");
            Fov = Config.Bind("Drone", "FpvFieldOfView", 95f, "Camera field of view while in the drone.");
            DebugLog = Config.Bind("Debug", "LogFlightEverySecond", false, "Write drone position/velocity/input to the BepInEx log once a second.");

            var go = new GameObject("DroneFPV_Controller");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<DroneController>();
            Log.LogInfo("Drone FPV loaded. Press " + KeyToggle.Value + " in game.");
        }
    }

    enum DroneState { Off, Flying, Parked }

    public class DroneController : MonoBehaviour
    {
        DroneState state = DroneState.Off;
        PlayerMainController player;
        Transform cam;
        Camera camComp;
        Transform camOldParent;
        Vector3 camOldLocalPos;
        Quaternion camOldLocalRot;
        float camOldFov;
        PlayerLookable lookable;
        Behaviour shake;

        GameObject drone;
        Rigidbody rb;
        Transform body, camMount;
        readonly List<Transform> props = new List<Transform>();
        readonly List<Material> ledMats = new List<Material>();
        DroneHud hud;

        public float yaw, camTilt, battery = 1f, flightTime;
        public bool chaseCam;
        public int speedMode = 1;
        public static readonly string[] SpeedNames = { "CINE", "NORMAL", "SPORT" };
        static readonly float[] SpeedValues = { 5f, 13f, 26f };
        float propSpin;
        bool comboLatch;
        Vector3 lastVel;

        public Vector3 Velocity { get { return rb != null ? rb.linearVelocity : Vector3.zero; } }
        public Transform DroneTransform { get { return drone != null ? drone.transform : null; } }
        public Transform BodyTransform { get { return body; } }
        public PlayerMainController Player { get { return player; } }
        // The astronaut's head: the camera's original parent follows the body, while
        // PlayerMainController's own transform does not.
        public Vector3 PilotPos
        {
            get
            {
                if (camOldParent != null) return camOldParent.position;
                if (lookable != null) return lookable.transform.position;
                return player != null ? player.transform.position : Vector3.zero;
            }
        }

        void Update()
        {
            try { Tick(); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Drone error: " + e);
                SafeReset();
            }
        }

        void Tick()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            if (state != DroneState.Off && (player == null || cam == null)) { SafeReset(); return; }
            if (!bindingsDumped) DumpGameBindings();

            bool toggle = kb != null && kb[Plugin.KeyToggle.Value].wasPressedThisFrame;
            bool recall = kb != null && kb[Plugin.KeyRecall.Value].wasPressedThisFrame;
            if (gp != null)
            {
                // L3 + R3 together toggles; D-pad down while flying recalls.
                bool combo = gp.leftStickButton.isPressed && gp.rightStickButton.isPressed;
                if (combo && !comboLatch) toggle = true;
                comboLatch = combo;
                if (state == DroneState.Flying && gp.dpad.down.wasPressedThisFrame) recall = true;
            }

            if (toggle)
            {
                if (state == DroneState.Off) Launch();
                else if (state == DroneState.Flying) ExitToBody();
                else if (state == DroneState.Parked) EnterDrone();
            }
            else if (recall && state != DroneState.Off)
            {
                Recall();
            }

            if (state == DroneState.Off)
            {
                battery = Mathf.Min(1f, battery + Time.deltaTime / 40f); // recharges in the backpack
                return;
            }

            if (state == DroneState.Flying)
            {
                if (kb != null && kb[Plugin.KeyCamera.Value].wasPressedThisFrame) SetChase(!chaseCam);
                if (gp != null && gp.dpad.up.wasPressedThisFrame) SetChase(!chaseCam);
                if ((kb != null && kb[Plugin.KeySpeed.Value].wasPressedThisFrame) || (gp != null && gp.dpad.right.wasPressedThisFrame))
                    speedMode = (speedMode + 1) % 3;
                if (gp != null && gp.dpad.left.wasPressedThisFrame) speedMode = (speedMode + 2) % 3;
                if (kb != null && kb[Plugin.KeyHelp.Value].wasPressedThisFrame && hud != null) hud.ToggleHelp();
                flightTime += Time.deltaTime;
            }

            AnimateDrone();
        }

        float nextDebug;

        static string PathOf(Transform t)
        {
            if (t == null) return "null";
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        bool bindingsDumped;
        float nextDumpTry;

        // Logs the game's own keyboard bindings once, and warns if one of ours collides.
        void DumpGameBindings()
        {
            if (Time.unscaledTime < nextDumpTry) return;
            nextDumpTry = Time.unscaledTime + 3f;
            var disp = UnityEngine.Object.FindAnyObjectByType<PlayerInputDispatcher>();
            if (disp == null || disp.inputMap == null) return;
            bindingsDumped = true;
            var ours = new Dictionary<string, string>();
            foreach (var e in new[] { Plugin.KeyToggle, Plugin.KeyRecall, Plugin.KeyCamera, Plugin.KeySpeed, Plugin.KeyHelp })
                ours["<keyboard>/" + e.Value.ToString().ToLowerInvariant()] = e.Definition.Key;
            var sb = new System.Text.StringBuilder("Game keyboard bindings:");
            foreach (var map in disp.inputMap.actionMaps)
                foreach (var act in map.actions)
                    foreach (var b in act.bindings)
                    {
                        string p = (b.effectivePath ?? "").ToLowerInvariant();
                        if (!p.StartsWith("<keyboard>")) continue;
                        sb.Append(" ").Append(map.name).Append("/").Append(act.name).Append("=").Append(p.Substring(11));
                        string mine;
                        if (ours.TryGetValue(p, out mine))
                            Plugin.Log.LogWarning("Drone key " + mine + " (" + p + ") is also the game's " + map.name + "/" + act.name);
                    }
            Plugin.Log.LogInfo(sb.ToString());
        }

        // ---------- state changes ----------

        bool FindPlayer()
        {
            PlayersManager pm = null;
            player = null;
            try { pm = Managers.GetManager<PlayersManager>(); } catch (Exception e) { Plugin.Log.LogWarning("GetManager<PlayersManager> failed: " + e.Message); }
            if (pm == null) { try { pm = UnityEngine.Object.FindAnyObjectByType<PlayersManager>(); } catch { } }
            if (pm != null) { try { player = pm.GetActivePlayerController(); } catch { } }
            if (player == null)
            {
                // fall back to the owned player object in the scene
                var all = UnityEngine.Object.FindObjectsByType<PlayerMainController>(FindObjectsSortMode.None);
                foreach (var p in all) { if (p.IsOwner || all.Length == 1) { player = p; break; } }
                Plugin.Log.LogInfo("PlayersManager " + (pm == null ? "missing" : "found") + ", " + all.Length + " PlayerMainController in scene.");
            }
            if (player == null) return false;
            lookable = player.GetPlayerLookable();
            cam = lookable != null ? (Transform)typeof(PlayerLookable).GetField("m_Camera", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(lookable) : null;
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam == null) return false;
            camComp = cam.GetComponent<Camera>();
            var cs = player.GetPlayerCameraShake();
            shake = cs;
            return true;
        }

        void Launch()
        {
            WindowsHandler wh = null;
            try { wh = Managers.GetManager<WindowsHandler>(); } catch { }
            if (wh != null && wh.GetHasUiOpen()) return;
            if (!FindPlayer()) { Plugin.Log.LogWarning("No local player yet."); return; }
            if (battery < 0.05f) { Plugin.Log.LogInfo("Drone battery empty, wait for it to recharge."); return; }

            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = player.transform.forward;
            yaw = Quaternion.LookRotation(fwd).eulerAngles.y;
            Vector3 spawn = cam.position + fwd * 1.2f;
            // don't spawn inside a wall: back off along the ray if something is in the way
            RaycastHit wall;
            if (Physics.SphereCast(cam.position, 0.4f, fwd, out wall, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                spawn = cam.position + fwd * Mathf.Max(0.3f, wall.distance - 0.1f);
            BuildDrone(spawn, Quaternion.Euler(0, yaw, 0));
            camTilt = 0;
            flightTime = 0;
            EnterDrone();
            Plugin.Log.LogInfo("Drone launched. cam=" + PathOf(cam) + " camPos=" + cam.position + " oldParent=" + PathOf(camOldParent)
                + " parentPos=" + (camOldParent != null ? camOldParent.position.ToString() : "-")
                + " main=" + (Camera.main != null ? PathOf(Camera.main.transform) + " " + Camera.main.transform.position : "null")
                + " player=" + player.transform.position + " lookable=" + (lookable != null ? lookable.transform.position.ToString() : "-")
                + " drone=" + drone.transform.position);
        }

        void EnterDrone()
        {
            if (drone == null || cam == null) return;
            camOldParent = cam.parent;
            camOldLocalPos = cam.localPosition;
            camOldLocalRot = cam.localRotation;
            if (camComp != null) { camOldFov = camComp.fieldOfView; camComp.fieldOfView = Plugin.Fov.Value; }

            var canAct = player.GetPlayerCanAct();
            if (canAct != null) canAct.SetCameraBlocked(true);
            if (lookable != null) lookable.enabled = false;
            if (shake != null) shake.enabled = false;
            SetGameUi(false);

            state = DroneState.Flying;
            cam.SetParent(camMount, false);
            SetChase(chaseCam);
            if (hud == null) hud = DroneHud.Create(this);
            hud.gameObject.SetActive(true);
            hud.ShowHelpFor(10f);
            rb.isKinematic = false;
        }

        void ExitToBody()
        {
            RestoreCamera();
            state = DroneState.Parked; // the drone keeps hovering where you left it
        }

        void Recall()
        {
            if (state == DroneState.Flying) RestoreCamera();
            if (drone != null) Destroy(drone);
            drone = null;
            state = DroneState.Off;
            Plugin.Log.LogInfo("Drone recalled.");
        }

        void RestoreCamera()
        {
            if (cam != null)
            {
                cam.SetParent(camOldParent, false);
                cam.localPosition = camOldLocalPos;
                cam.localRotation = camOldLocalRot;
                if (camComp != null && camOldFov > 1f) camComp.fieldOfView = camOldFov;
            }
            if (player != null)
            {
                var canAct = player.GetPlayerCanAct();
                if (canAct != null) canAct.SetCameraBlocked(false);
            }
            if (lookable != null) lookable.enabled = true;
            if (shake != null) shake.enabled = true;
            SetGameUi(true);
            if (hud != null) hud.gameObject.SetActive(false);
        }

        void SafeReset()
        {
            try { if (state == DroneState.Flying) RestoreCamera(); } catch { }
            if (drone != null) Destroy(drone);
            drone = null;
            if (hud != null) hud.gameObject.SetActive(false);
            state = DroneState.Off;
        }

        void SetGameUi(bool visible)
        {
            try
            {
                var vt = Managers.GetManager<VisualsToggler>();
                if (vt != null) { vt.SetUi(visible); vt.SetHand(visible); }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not toggle game UI: " + e.Message); }
        }

        void SetChase(bool chase)
        {
            chaseCam = chase;
            if (cam == null || state == DroneState.Off) return;
            if (cam.parent != camMount) return;
            cam.localPosition = chase ? new Vector3(0, 0.55f, -2.2f) : new Vector3(0, 0.0f, -0.03f);
            cam.localRotation = Quaternion.identity;
            if (body != null) SetLayerVisible(!chase ? false : true);
        }

        void SetLayerVisible(bool visible)
        {
            // In FPV mode the hull would block the lens, so hide it; props stay visible at the edge of the view.
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.transform.name;
                bool isProp = n.StartsWith("prop", StringComparison.OrdinalIgnoreCase) || n.StartsWith("guard_", StringComparison.OrdinalIgnoreCase);
                r.enabled = visible || isProp;
            }
        }

        // ---------- flight ----------

        void FixedUpdate()
        {
            if (drone == null || rb == null) return;
            float dt = Time.fixedDeltaTime;
            Vector3 input = Vector3.zero;
            float yawIn = 0, tiltIn = 0;
            bool boost = false;

            if (state == DroneState.Flying && battery > 0f)
            {
                var kb = Keyboard.current;
                var gp = Gamepad.current;
                var mouse = Mouse.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed) input.z += 1;
                    if (kb.sKey.isPressed) input.z -= 1;
                    if (kb.dKey.isPressed) input.x += 1;
                    if (kb.aKey.isPressed) input.x -= 1;
                    if (kb.upArrowKey.isPressed || kb.spaceKey.isPressed) input.y += 1;
                    if (kb.downArrowKey.isPressed || kb.leftCtrlKey.isPressed) input.y -= 1;
                    if (kb.rightArrowKey.isPressed) yawIn += 1;
                    if (kb.leftArrowKey.isPressed) yawIn -= 1;
                    if (kb.pageUpKey.isPressed) tiltIn += 1;
                    if (kb.pageDownKey.isPressed) tiltIn -= 1;
                    boost = kb.leftShiftKey.isPressed;
                }
                if (gp != null)
                {
                    Vector2 ls = gp.leftStick.ReadValue();
                    Vector2 rs = gp.rightStick.ReadValue();
                    input.x += ls.x; input.z += ls.y;
                    input.y += gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
                    if (gp.rightShoulder.isPressed) input.y += 1;
                    if (gp.leftShoulder.isPressed) input.y -= 1;
                    yawIn += rs.x;
                    tiltIn += rs.y;
                    boost |= gp.buttonSouth.isPressed;
                }
                if (mouse != null)
                {
                    Vector2 md = mouse.delta.ReadValue() * Plugin.MouseSensitivity.Value;
                    yaw += md.x;
                    camTilt = Mathf.Clamp(camTilt - md.y, -80f, 60f);
                }
            }
            input.x = Mathf.Clamp(input.x, -1, 1);
            input.y = Mathf.Clamp(input.y, -1, 1);
            input.z = Mathf.Clamp(input.z, -1, 1);

            yaw += yawIn * 110f * dt;
            camTilt = Mathf.Clamp(camTilt - tiltIn * 70f * dt, -80f, 60f);
            Quaternion yawRot = Quaternion.Euler(0, yaw, 0);
            rb.MoveRotation(yawRot);

            float speed = SpeedValues[speedMode] * (boost ? 1.8f : 1f);
            Vector3 target = yawRot * new Vector3(input.x * speed, input.y * speed * 0.6f, input.z * speed);

            if (battery <= 0f) target = Vector3.down * 3f; // dead battery: it sinks
            if (player != null)
            {
                Vector3 toDrone = drone.transform.position - PilotPos;
                float max = Plugin.MaxRange.Value;
                if (toDrone.magnitude > max)
                    target += -toDrone.normalized * (toDrone.magnitude - max) * 4f; // signal fence pulls it back
            }

            float accel = input.sqrMagnitude > 0.01f ? 4.5f : 3f;
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, target, 1f - Mathf.Exp(-accel * dt));

            if (Plugin.DebugLog.Value && Time.time > nextDebug)
            {
                nextDebug = Time.time + 1f;
                Plugin.Log.LogInfo("[dbg] state=" + state + " pos=" + drone.transform.position + " vel=" + rb.linearVelocity + " in=" + input
                    + " kin=" + rb.isKinematic + " sim=" + Physics.simulationMode + " pilot=" + PilotPos + " camParent=" + PathOf(cam != null ? cam.parent : null)
                    + " main=" + (Camera.main != null ? Camera.main.name + " " + Camera.main.transform.position : "null"));
            }

            if (state == DroneState.Flying)
            {
                float load = 0.4f + 0.6f * Mathf.Clamp01(rb.linearVelocity.magnitude / 13f) + (boost ? 0.5f : 0f);
                battery = Mathf.Max(0f, battery - dt * load / (Plugin.BatteryMinutes.Value * 60f));
            }
        }

        void AnimateDrone()
        {
            if (drone == null) return;
            Vector3 v = rb.linearVelocity;
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0, yaw, 0)) * v;
            Vector3 acc = (v - lastVel) / Mathf.Max(Time.deltaTime, 0.001f);
            lastVel = v;
            float pitch = Mathf.Clamp(local.z * 1.4f, -28, 28);
            float roll = Mathf.Clamp(-local.x * 1.4f, -28, 28);
            Quaternion want = Quaternion.Euler(pitch, 0, roll);
            body.localRotation = Quaternion.Slerp(body.localRotation, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            // The FPV camera follows part of the body tilt, like a real hard-mounted camera.
            Vector3 be = body.localEulerAngles;
            float bp = Mathf.DeltaAngle(0, be.x), br = Mathf.DeltaAngle(0, be.z);
            camMount.localRotation = Quaternion.Euler(camTilt + bp * 0.35f, 0, br * 0.5f);

            float spinRate = state == DroneState.Off ? 0 : (battery > 0 ? 2200f + v.magnitude * 60f : 300f);
            propSpin += spinRate * Time.deltaTime;
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i] == null) continue;
                props[i].localRotation = Quaternion.Euler(0, (i % 2 == 0 ? propSpin : -propSpin), 0);
            }
            bool blink = Mathf.Repeat(Time.time, 1f) < 0.15f || (battery < 0.2f && Mathf.Repeat(Time.time, 0.3f) < 0.15f);
            foreach (var m in ledMats)
            {
                Color c = m.name.IndexOf("Red", StringComparison.OrdinalIgnoreCase) >= 0 ? new Color(1f, 0.1f, 0.05f) : new Color(0.1f, 1f, 0.4f);
                SetEmission(m, blink ? c * 4f : c * 0.4f);
            }
        }

        // ---------- model ----------

        void BuildDrone(Vector3 pos, Quaternion rot)
        {
            drone = new GameObject("DroneFPV_Drone");
            drone.layer = 0;
            drone.transform.SetPositionAndRotation(pos, rot);
            rb = drone.AddComponent<Rigidbody>();
            rb.position = pos;
            rb.rotation = rot;
            rb.useGravity = false;
            rb.mass = 1.2f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = drone.AddComponent<SphereCollider>();
            col.radius = 0.38f;

            body = new GameObject("Body").transform;
            body.SetParent(drone.transform, false);
            camMount = new GameObject("CamMount").transform;
            camMount.SetParent(drone.transform, false);
            camMount.localPosition = new Vector3(0, 0.03f, 0.22f);

            props.Clear();
            ledMats.Clear();
            string obj = Path.Combine(Plugin.Folder, "drone.obj");
            bool ok = false;
            if (File.Exists(obj))
            {
                try { ok = ObjLoader.Load(obj, body, props, ledMats); }
                catch (Exception e) { Plugin.Log.LogError("drone.obj failed to load: " + e); }
            }
            if (!ok) BuildFallbackModel();
            foreach (var c in body.GetComponentsInChildren<Collider>(true)) Destroy(c);
        }

        void BuildFallbackModel()
        {
            Plugin.Log.LogWarning("Using built-in fallback drone model (drone.obj not found).");
            Material dark = Mats.Make(new Color(0.12f, 0.13f, 0.15f), "Body");
            Material accent = Mats.Make(new Color(1f, 0.45f, 0.05f), "Accent");
            Material propM = Mats.Make(new Color(0.08f, 0.08f, 0.09f), "Prop");
            Prim(PrimitiveType.Cube, "hull", new Vector3(0, 0, 0), new Vector3(0.22f, 0.07f, 0.34f), dark);
            Prim(PrimitiveType.Cube, "canopy", new Vector3(0, 0.06f, 0.02f), new Vector3(0.15f, 0.06f, 0.22f), accent);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? 1 : -1, sz = (i < 2) ? 1 : -1;
                Vector3 p = new Vector3(sx * 0.24f, 0, sz * 0.24f);
                var arm = Prim(PrimitiveType.Cube, "arm", p * 0.5f, new Vector3(0.04f, 0.025f, 0.36f), dark);
                arm.localRotation = Quaternion.Euler(0, sx * sz * 45f, 0);
                Prim(PrimitiveType.Cylinder, "motor", p + Vector3.up * 0.02f, new Vector3(0.05f, 0.025f, 0.05f), accent);
                var pv = new GameObject("prop" + i).transform;
                pv.SetParent(body, false);
                pv.localPosition = p + Vector3.up * 0.05f;
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                blade.name = "propblade";
                blade.SetParent(pv, false);
                blade.localScale = new Vector3(0.24f, 0.004f, 0.025f);
                blade.GetComponent<Renderer>().sharedMaterial = propM;
                props.Add(pv);
            }
            Material ledG = Mats.Make(new Color(0.1f, 1f, 0.4f), "LED_Green", true);
            Material ledR = Mats.Make(new Color(1f, 0.1f, 0.05f), "LED_Red", true);
            Prim(PrimitiveType.Sphere, "led", new Vector3(0.06f, -0.02f, -0.17f), Vector3.one * 0.025f, ledR);
            Prim(PrimitiveType.Sphere, "led", new Vector3(-0.06f, -0.02f, -0.17f), Vector3.one * 0.025f, ledG);
            ledMats.Add(ledG); ledMats.Add(ledR);
        }

        Transform Prim(PrimitiveType t, string n, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t).transform;
            g.name = n;
            g.SetParent(body, false);
            g.localPosition = pos;
            g.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        internal static void SetEmission(Material m, Color c)
        {
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c);
        }
    }

    static class Mats
    {
        static Shader shader;
        static bool searched;

        static Shader Find()
        {
            if (searched) return shader;
            searched = true;
            string[] names = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit", "Standard" };
            foreach (var n in names)
            {
                shader = Shader.Find(n);
                if (shader != null) { Plugin.Log.LogInfo("Drone material shader: " + n); return shader; }
            }
            Plugin.Log.LogWarning("No URP shader found by name, copying one from the scene.");
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.HasProperty("_BaseColor"))
                { shader = r.sharedMaterial.shader; break; }
            }
            return shader;
        }

        public static Material Make(Color c, string name, bool emissive = false)
        {
            var m = new Material(Find());
            m.name = name;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", name.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.8f : 0.1f);
            if (emissive) DroneController.SetEmission(m, c * 2f);
            return m;
        }
    }

    // Minimal OBJ + MTL reader. Expects Blender's OBJ export with "Y up / -Z forward".
    // Each "o" object becomes a child GameObject, pivot at its bounds centre.
    // Objects named prop* spin; materials named LED* glow and blink.
    static class ObjLoader
    {
        class Part { public string name; public Dictionary<string, List<int>> subs = new Dictionary<string, List<int>>(); public List<string> order = new List<string>(); }

        public static bool Load(string path, Transform parent, List<Transform> props, List<Material> leds)
        {
            var inv = CultureInfo.InvariantCulture;
            var pos = new List<Vector3>(); var nor = new List<Vector3>();
            var vP = new List<Vector3>(); var vN = new List<Vector3>();
            var parts = new List<Part>();
            var cache = new Dictionary<string, int>();
            Part cur = null; string mat = "Default";
            var mtl = new Dictionary<string, Color>();
            string dir = Path.GetDirectoryName(path);

            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                switch (t[0])
                {
                    case "mtllib": ReadMtl(Path.Combine(dir, line.Substring(7).Trim()), mtl); break;
                    case "o": case "g":
                        cur = new Part(); cur.name = t.Length > 1 ? t[1] : "part"; parts.Add(cur); cache.Clear(); break;
                    case "v": pos.Add(new Vector3(-float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv))); break;
                    case "vn": nor.Add(new Vector3(-float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv))); break;
                    case "usemtl": mat = t.Length > 1 ? t[1] : "Default"; break;
                    case "f":
                        if (cur == null) { cur = new Part(); cur.name = "part"; parts.Add(cur); }
                        List<int> idx;
                        if (!cur.subs.TryGetValue(mat, out idx)) { idx = new List<int>(); cur.subs[mat] = idx; cur.order.Add(mat); }
                        var face = new List<int>();
                        for (int i = 1; i < t.Length; i++)
                        {
                            string key = cur.name + "|" + t[i];
                            int vi;
                            if (!cache.TryGetValue(key, out vi))
                            {
                                string[] c = t[i].Split('/');
                                int pi = int.Parse(c[0]); pi = pi < 0 ? pos.Count + pi : pi - 1;
                                vi = vP.Count;
                                vP.Add(pos[pi]);
                                if (c.Length > 2 && c[2].Length > 0) { int ni = int.Parse(c[2]); ni = ni < 0 ? nor.Count + ni : ni - 1; vN.Add(nor[ni]); }
                                else vN.Add(Vector3.zero);
                                cache[key] = vi;
                            }
                            face.Add(vi);
                        }
                        // fan triangulate, reversed winding for the X mirror
                        for (int i = 1; i + 1 < face.Count; i++) { idx.Add(face[0]); idx.Add(face[i + 1]); idx.Add(face[i]); }
                        break;
                }
            }
            if (vP.Count == 0) return false;

            var mats = new Dictionary<string, Material>();
            foreach (var p in parts)
            {
                if (p.order.Count == 0) continue;
                // gather the vertices this part uses
                var remap = new Dictionary<int, int>();
                var lp = new List<Vector3>(); var ln = new List<Vector3>();
                var tris = new List<int[]>();
                foreach (var m in p.order)
                {
                    var src = p.subs[m]; var dst = new int[src.Count];
                    for (int i = 0; i < src.Count; i++)
                    {
                        int r;
                        if (!remap.TryGetValue(src[i], out r)) { r = lp.Count; remap[src[i]] = r; lp.Add(vP[src[i]]); ln.Add(vN[src[i]]); }
                        dst[i] = r;
                    }
                    tris.Add(dst);
                }
                Vector3 min = lp[0], max = lp[0];
                foreach (var v in lp) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
                Vector3 center = (min + max) * 0.5f;
                for (int i = 0; i < lp.Count; i++) lp[i] -= center;

                var mesh = new Mesh();
                mesh.name = p.name;
                if (lp.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(lp);
                bool hasN = false; foreach (var n in ln) if (n != Vector3.zero) { hasN = true; break; }
                if (hasN) mesh.SetNormals(ln);
                mesh.subMeshCount = tris.Count;
                for (int i = 0; i < tris.Count; i++) mesh.SetTriangles(tris[i], i);
                if (!hasN) mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var go = new GameObject(p.name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = center;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var arr = new Material[p.order.Count];
                for (int i = 0; i < arr.Length; i++)
                {
                    string mn = p.order[i];
                    Material mm;
                    if (!mats.TryGetValue(mn, out mm))
                    {
                        Color col; if (!mtl.TryGetValue(mn, out col)) col = Color.gray;
                        bool led = mn.StartsWith("LED", StringComparison.OrdinalIgnoreCase);
                        mm = Mats.Make(col, mn, led);
                        if (led) leds.Add(mm);
                        mats[mn] = mm;
                    }
                    arr[i] = mm;
                }
                mr.sharedMaterials = arr;
                if (p.name.StartsWith("prop", StringComparison.OrdinalIgnoreCase))
                {
                    // spin around a pivot so the blade rotates in place
                    var pivot = new GameObject(p.name + "_pivot").transform;
                    pivot.SetParent(parent, false);
                    pivot.localPosition = center;
                    go.transform.SetParent(pivot, false);
                    go.transform.localPosition = Vector3.zero;
                    props.Add(pivot);
                }
            }
            Plugin.Log.LogInfo("Loaded drone.obj: " + parts.Count + " parts, " + vP.Count + " verts, " + props.Count + " props.");
            return true;
        }

        static void ReadMtl(string path, Dictionary<string, Color> mtl)
        {
            if (!File.Exists(path)) return;
            var inv = CultureInfo.InvariantCulture;
            string cur = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                string[] t = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                if (t[0] == "newmtl") cur = t[1];
                else if (t[0] == "Kd" && cur != null && t.Length >= 4)
                {
                    // MTL colours are linear; the game renders in linear space.
                    Color c = new Color(float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv));
                    mtl[cur] = c.gamma;
                }
            }
        }
    }
}
