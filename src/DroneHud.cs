// The drone's own on-screen display, shown instead of the game's HUD while flying.
using System;
using System.Text;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DroneFPV
{
    public class DroneHud : MonoBehaviour
    {
        static readonly Color Green = new Color(0.36f, 1f, 0.6f, 1f);
        static readonly Color Cyan = new Color(0.35f, 0.9f, 1f, 1f);
        static readonly Color Amber = new Color(1f, 0.72f, 0.2f, 1f);
        static readonly Color Red = new Color(1f, 0.25f, 0.2f, 1f);
        static readonly Color Dim = new Color(0.36f, 1f, 0.6f, 0.35f);

        DroneController d;
        RectTransform root;
        TMP_FontAsset font;

        TextMeshProUGUI tTopLeft, tRec, tBattery, tLink, tCompass, tHeading, tSpeed, tAlt, tHome, tMode, tPilot, tWarn, tHelp, tTimer, tCoords;
        RectTransform horizon, homeArrow, speedFill, altFill;
        Image batteryFill;
        Image[] signalBars = new Image[5];
        RawImage noise;
        Texture2D noiseTex;
        float helpUntil;
        bool helpPinned;

        public static DroneHud Create(DroneController d)
        {
            var go = new GameObject("DroneFPV_HUD");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var hud = go.AddComponent<DroneHud>();
            hud.d = d;
            hud.Build();
            return hud;
        }

        public void ToggleHelp() { helpPinned = !helpPinned; helpUntil = 0; }
        public void ShowHelpFor(float s) { helpUntil = Time.unscaledTime + s; }

        // ---------- building ----------

        void Build()
        {
            root = (RectTransform)transform;
            font = FindFont();

            // overlays: vignette + scanlines, then signal noise
            var ov = NewRaw("overlay", root, MakeOverlayTex());
            Stretch(ov.rectTransform);
            ov.raycastTarget = false;
            noiseTex = MakeNoiseTex();
            noise = NewRaw("noise", root, noiseTex);
            Stretch(noise.rectTransform);
            noise.color = new Color(1, 1, 1, 0);

            // corner brackets
            Bracket(new Vector2(0, 1), new Vector2(60, -60), 1, -1);
            Bracket(new Vector2(1, 1), new Vector2(-60, -60), -1, -1);
            Bracket(new Vector2(0, 0), new Vector2(60, 60), 1, 1);
            Bracket(new Vector2(1, 0), new Vector2(-60, 60), -1, 1);

            // centre reticle
            var c = NewRect("reticle", root, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Bar(c, new Vector2(-34, 0), new Vector2(26, 3), Green);
            Bar(c, new Vector2(34, 0), new Vector2(26, 3), Green);
            Bar(c, new Vector2(0, -26), new Vector2(3, 18), Green);
            Bar(c, new Vector2(0, 0), new Vector2(5, 5), Green);

            // artificial horizon: a split line that rolls and slides with pitch
            horizon = NewRect("horizon", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            Bar(horizon, new Vector2(-230, 0), new Vector2(260, 2), Dim);
            Bar(horizon, new Vector2(230, 0), new Vector2(260, 2), Dim);
            Bar(horizon, new Vector2(-358, -8), new Vector2(2, 18), Dim);
            Bar(horizon, new Vector2(358, -8), new Vector2(2, 18), Dim);

            // top bar
            tTopLeft = Txt("title", new Vector2(0, 1), new Vector2(90, -78), 26, TextAlignmentOptions.TopLeft, Green);
            tTopLeft.text = "<b>PC-DRONE</b> <size=18><color=#9fffd0>MK1 • FPV LINK</color></size>";
            tRec = Txt("rec", new Vector2(0, 1), new Vector2(90, -114), 22, TextAlignmentOptions.TopLeft, Red);
            tTimer = Txt("timer", new Vector2(0, 1), new Vector2(190, -114), 22, TextAlignmentOptions.TopLeft, Green);
            tCoords = Txt("coords", new Vector2(0, 1), new Vector2(90, -146), 18, TextAlignmentOptions.TopLeft, Dim);

            tCompass = Txt("compass", new Vector2(0.5f, 1), new Vector2(0, -60), 24, TextAlignmentOptions.Top, Green);
            tCompass.rectTransform.sizeDelta = new Vector2(900, 40);
            tHeading = Txt("heading", new Vector2(0.5f, 1), new Vector2(0, -98), 28, TextAlignmentOptions.Top, Cyan);
            tHeading.rectTransform.sizeDelta = new Vector2(300, 40);
            Bar(NewRect("hdgTick", root, new Vector2(0.5f, 1), new Vector2(0, -52), Vector2.zero), Vector2.zero, new Vector2(3, 14), Cyan);

            // battery (top right)
            var batt = NewRect("battery", root, new Vector2(1, 1), new Vector2(-200, -90), new Vector2(110, 40));
            Bar(batt, new Vector2(0, 0), new Vector2(110, 40), Green).color = new Color(0, 0, 0, 0.35f);
            Outline(batt, new Vector2(110, 40), 2, Green);
            Bar(batt, new Vector2(60, 0), new Vector2(8, 16), Green);
            var fillRt = NewRect("fill", batt, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(98, 28));
            fillRt.pivot = new Vector2(0, 0.5f);
            batteryFill = fillRt.gameObject.AddComponent<Image>();
            batteryFill.color = Green;
            tBattery = Txt("battPct", new Vector2(1, 1), new Vector2(-270, -78), 26, TextAlignmentOptions.TopRight, Green);

            // signal bars (under battery)
            var sig = NewRect("signal", root, new Vector2(1, 1), new Vector2(-230, -150), Vector2.zero);
            for (int i = 0; i < 5; i++)
            {
                float h = 8 + i * 6;
                var b = Bar(sig, new Vector2(i * 12, h * 0.5f), new Vector2(8, h), Green);
                signalBars[i] = b;
            }
            tLink = Txt("link", new Vector2(1, 1), new Vector2(-270, -132), 20, TextAlignmentOptions.TopRight, Green);

            // speed tape (left) and altitude tape (right)
            var sp = NewRect("speedTape", root, new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(10, 260));
            Outline(sp, new Vector2(10, 260), 2, Dim);
            speedFill = NewRect("fill", sp, new Vector2(0.5f, 0), Vector2.zero, new Vector2(6, 0));
            speedFill.pivot = new Vector2(0.5f, 0);
            speedFill.gameObject.AddComponent<Image>().color = Green;
            tSpeed = Txt("speed", new Vector2(0, 0.5f), new Vector2(145, 10), 30, TextAlignmentOptions.Left, Green);

            var al = NewRect("altTape", root, new Vector2(1, 0.5f), new Vector2(-120, 0), new Vector2(10, 260));
            Outline(al, new Vector2(10, 260), 2, Dim);
            altFill = NewRect("fill", al, new Vector2(0.5f, 0), Vector2.zero, new Vector2(6, 0));
            altFill.pivot = new Vector2(0.5f, 0);
            altFill.gameObject.AddComponent<Image>().color = Cyan;
            tAlt = Txt("alt", new Vector2(1, 0.5f), new Vector2(-145, 10), 30, TextAlignmentOptions.Right, Cyan);

            // home arrow (bottom left)
            var home = NewRect("home", root, new Vector2(0, 0), new Vector2(150, 170), new Vector2(64, 64));
            Outline(home, new Vector2(80, 80), 2, Dim);
            homeArrow = NewRect("arrow", home, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
            var ai = homeArrow.gameObject.AddComponent<RawImage>();
            ai.texture = MakeArrowTex();
            ai.color = Amber;
            tHome = Txt("homeTxt", new Vector2(0, 0), new Vector2(205, 190), 24, TextAlignmentOptions.Left, Amber);

            // bottom centre: modes; bottom right: pilot vitals
            tMode = Txt("mode", new Vector2(0.5f, 0), new Vector2(0, 70), 24, TextAlignmentOptions.Bottom, Green);
            tMode.rectTransform.sizeDelta = new Vector2(1000, 40);
            tPilot = Txt("pilot", new Vector2(1, 0), new Vector2(-90, 80), 22, TextAlignmentOptions.BottomRight, Green);

            // warnings + help
            tWarn = Txt("warn", new Vector2(0.5f, 0.5f), new Vector2(0, 150), 40, TextAlignmentOptions.Center, Red);
            tWarn.rectTransform.sizeDelta = new Vector2(900, 60);
            tHelp = Txt("help", new Vector2(0.5f, 0.5f), new Vector2(0, -190), 21, TextAlignmentOptions.Top, new Color(1, 1, 1, 0.9f));
            tHelp.rectTransform.sizeDelta = new Vector2(1100, 260);
            tHelp.text =
                "<color=#5cff99><b>DRONE CONTROLS</b></color>\n" +
                "<b>W A S D</b> move   <b>↑ / ↓</b> or <b>Space / Ctrl</b> up / down   <b>← / →</b> or <b>mouse</b> turn   <b>PgUp / PgDn</b> camera tilt\n" +
                "<b>Shift</b> boost   <b>" + Plugin.KeySpeed.Value + "</b> speed mode   <b>" + Plugin.KeyCamera.Value + "</b> FPV / chase cam   <b>" + Plugin.KeyToggle.Value + "</b> back to body   <b>" + Plugin.KeyRecall.Value + "</b> recall   <b>" + Plugin.KeyHelp.Value + "</b> help\n" +
                "<color=#5cff99><b>CONTROLLER</b></color>  left stick move   RT / LT up / down   right stick turn + tilt   A boost\n" +
                "D-pad →/← speed   D-pad ↑ camera   D-pad ↓ recall   L3 + R3 back to body";
        }

        // ---------- per frame ----------

        void LateUpdate()
        {
            Transform t = d.DroneTransform;
            if (t == null) return;
            Vector3 v = d.Velocity;
            float kmh = v.magnitude * 3.6f;
            float now = Time.unscaledTime;

            // heading + compass tape
            float hdg = Mathf.Repeat(d.yaw, 360f);
            tHeading.text = "<b>" + ((int)hdg).ToString("000") + "°</b>";
            tCompass.text = CompassTape(hdg);

            // attitude
            Vector3 be = d.BodyTransform.localEulerAngles;
            float pitch = Mathf.DeltaAngle(0, be.x), roll = Mathf.DeltaAngle(0, be.z);
            horizon.localRotation = Quaternion.Euler(0, 0, roll);
            horizon.anchoredPosition = new Vector2(0, -(pitch - d.camTilt) * 6f);

            // speed and altitude
            tSpeed.text = "<b>" + kmh.ToString("0") + "</b><size=18> km/h</size>\n<size=18><color=#9fffd0>V/S " + v.y.ToString("+0.0;-0.0") + "</color></size>";
            speedFill.sizeDelta = new Vector2(6, 256 * Mathf.Clamp01(kmh / 170f));
            float agl = 0;
            RaycastHit hit;
            if (Physics.Raycast(t.position, Vector3.down, out hit, 500f, ~0, QueryTriggerInteraction.Ignore)) agl = hit.distance - 0.1f;
            else agl = 500f;
            float rel = d.Player != null ? t.position.y - d.PilotPos.y : 0;
            tAlt.text = "<b>" + agl.ToString("0.0") + "</b><size=18> m AGL</size>\n<size=18><color=#a8f0ff>REL " + rel.ToString("+0;-0") + " m</color></size>";
            altFill.sizeDelta = new Vector2(6, 256 * Mathf.Clamp01(agl / 120f));

            // home / signal
            float dist = 0;
            if (d.Player != null)
            {
                Vector3 to = d.PilotPos - t.position;
                dist = to.magnitude;
                Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
                float ang = Vector3.SignedAngle(Quaternion.Euler(0, d.yaw, 0) * Vector3.forward, flat, Vector3.up);
                homeArrow.localRotation = Quaternion.Euler(0, 0, -ang);
            }
            tHome.text = "<b>HOME</b> " + dist.ToString("0") + " m";
            float max = Plugin.MaxRange.Value;
            float signal = Mathf.Clamp01(1f - Mathf.InverseLerp(max * 0.55f, max, dist));
            int bars = Mathf.CeilToInt(signal * 5f);
            for (int i = 0; i < 5; i++) signalBars[i].color = i < bars ? (signal > 0.4f ? Green : Amber) : Dim;
            tLink.text = "LINK " + (signal * 100f).ToString("0") + "%";
            float loss = 1f - signal;
            noise.color = new Color(1, 1, 1, loss * loss * 0.35f);
            noise.uvRect = new Rect(UnityEngine.Random.value, UnityEngine.Random.value, 1.5f, 1.5f);

            // battery
            float b = d.battery;
            Color bc = b > 0.4f ? Green : (b > 0.2f ? Amber : Red);
            batteryFill.color = bc;
            batteryFill.rectTransform.sizeDelta = new Vector2(98 * b, 28);
            tBattery.color = bc;
            tBattery.text = "<b>" + (b * 100f).ToString("0") + "%</b>";

            // recording + timer + coordinates
            tRec.text = Mathf.Repeat(now, 1.2f) < 0.7f ? "● REC" : "  REC";
            int secs = (int)d.flightTime;
            tTimer.text = (secs / 60).ToString("00") + ":" + (secs % 60).ToString("00");
            Vector3 p = t.position;
            tCoords.text = "X " + p.x.ToString("0") + "  Y " + p.y.ToString("0") + "  Z " + p.z.ToString("0");

            tMode.text = "MODE <b>ANGLE</b>   |   SPD <b>" + DroneController.SpeedNames[d.speedMode] + "</b>   |   CAM <b>" + (d.chaseCam ? "CHASE" : "FPV") + "</b>";

            // the pilot's body is still out there breathing
            string pilot = "";
            bool pilotDanger = false;
            try
            {
                var g = d.Player != null ? d.Player.GetGaugesHandler() : null;
                if (g != null)
                {
                    float o2 = g.GetPlayerOxygenValue(), hp = g.GetPlayerHealthValue(), h2o = g.GetPlayerThirstValue();
                    pilotDanger = o2 < 25f || hp < 25f || h2o < 15f;
                    pilot = "<color=#9fffd0>PILOT</color>   O2 " + Gauge(o2) + "   HP " + Gauge(hp) + "   H2O " + Gauge(h2o);
                }
            }
            catch { }
            tPilot.text = pilot;

            // warnings (most important first)
            string warn = "";
            bool flash = Mathf.Repeat(now, 0.8f) < 0.5f;
            if (b <= 0f) warn = "BATTERY DEPLETED";
            else if (b < 0.15f) warn = "LOW BATTERY — RETURN HOME";
            else if (pilotDanger) warn = "PILOT VITALS CRITICAL";
            else if (signal < 0.35f) warn = "SIGNAL WEAK";
            else if (Physics.SphereCast(t.position, 0.3f, v.sqrMagnitude > 1f ? v.normalized : t.forward, out hit, Mathf.Max(2f, v.magnitude * 0.6f), ~0, QueryTriggerInteraction.Ignore) && v.magnitude > 3f)
                warn = "OBSTACLE";
            tWarn.text = flash ? warn : "";

            bool showHelp = helpPinned || now < helpUntil;
            tHelp.gameObject.SetActive(showHelp);
        }

        static string Gauge(float v)
        {
            string col = v < 25f ? "#ff4a3a" : (v < 50f ? "#ffb833" : "#5cff99");
            return "<color=" + col + ">" + v.ToString("0") + "%</color>";
        }

        static string CompassTape(float hdg)
        {
            var sb = new StringBuilder();
            int start = Mathf.FloorToInt((hdg - 60f) / 15f) * 15;
            for (int a = start; a <= hdg + 60f; a += 15)
            {
                int n = (int)Mathf.Repeat(a, 360);
                string lab;
                switch (n)
                {
                    case 0: lab = "<color=#ff5a4a><b>N</b></color>"; break;
                    case 90: lab = "<b>E</b>"; break;
                    case 180: lab = "<b>S</b>"; break;
                    case 270: lab = "<b>W</b>"; break;
                    default: lab = (n % 45 == 0) ? n.ToString("000") : "·"; break;
                }
                sb.Append(lab).Append("    ");
            }
            return sb.ToString();
        }

        // ---------- helpers ----------

        TMP_FontAsset FindFont()
        {
            TMP_FontAsset f = null;
            try { f = TMP_Settings.defaultFontAsset; } catch { }
            if (f == null)
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                    if (t.font != null) { f = t.font; break; }
            }
            return f;
        }

        TextMeshProUGUI Txt(string name, Vector2 anchor, Vector2 pos, float size, TextAlignmentOptions align, Color c)
        {
            var rt = NewRect(name, root, anchor, pos, new Vector2(520, 80));
            rt.pivot = anchor;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = c;
            t.richText = true;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.outlineWidth = 0.18f;
            t.outlineColor = new Color32(0, 0, 0, 200);
            return t;
        }

        RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Image Bar(RectTransform parent, Vector2 pos, Vector2 size, Color c)
        {
            var rt = NewRect("bar", parent, new Vector2(0.5f, 0.5f), pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        void Outline(RectTransform parent, Vector2 size, float w, Color c)
        {
            Bar(parent, new Vector2(0, size.y * 0.5f), new Vector2(size.x, w), c);
            Bar(parent, new Vector2(0, -size.y * 0.5f), new Vector2(size.x, w), c);
            Bar(parent, new Vector2(size.x * 0.5f, 0), new Vector2(w, size.y), c);
            Bar(parent, new Vector2(-size.x * 0.5f, 0), new Vector2(w, size.y), c);
        }

        void Bracket(Vector2 anchor, Vector2 pos, float sx, float sy)
        {
            var rt = NewRect("bracket", root, anchor, pos, Vector2.zero);
            Bar(rt, new Vector2(sx * 30, 0), new Vector2(60, 3), Green);
            Bar(rt, new Vector2(0, sy * 30), new Vector2(3, 60), Green);
        }

        RawImage NewRaw(string name, Transform parent, Texture tex)
        {
            var rt = NewRect(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var r = rt.gameObject.AddComponent<RawImage>();
            r.texture = tex;
            r.raycastTarget = false;
            return r;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static Texture2D MakeOverlayTex()
        {
            const int W = 256, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)(W - 1) * 2 - 1, v = y / (float)(H - 1) * 2 - 1;
                    float r = Mathf.Sqrt(u * u * 0.8f + v * v);
                    float vig = Mathf.SmoothStep(0.55f, 1.35f, r) * 0.85f;
                    float scan = (y % 2 == 0) ? 0.07f : 0f;
                    float a = Mathf.Clamp01(vig + scan);
                    px[y * W + x] = new Color32(0, 10, 4, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Texture2D MakeNoiseTex()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            var px = new Color32[S * S];
            var rnd = new System.Random(7);
            for (int i = 0; i < px.Length; i++)
            {
                byte g = (byte)rnd.Next(0, 256);
                px[i] = new Color32(g, g, g, (byte)(rnd.Next(0, 2) == 0 ? 0 : 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Texture2D MakeArrowTex()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // upward-pointing chevron
                    float u = (x - S / 2f) / (S / 2f), v = y / (float)S;
                    bool inTri = v > 0.1f && v < 0.95f && Mathf.Abs(u) < (0.95f - v) * 0.9f;
                    bool notch = v < 0.4f && Mathf.Abs(u) < (0.4f - v) * 0.9f;
                    px[y * S + x] = (inTri && !notch) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
