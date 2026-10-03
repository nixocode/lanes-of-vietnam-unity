using System.Runtime.InteropServices;
using LanesOfVietnam.Sim;
using UnityEngine;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The authored camera: side-on, a long lens from a long way back. It pans
    /// along the line and dollies a little; it never orbits. An orbit control
    /// lets the player break the composition in one drag, and the composition
    /// is half of what the reference is.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>Pan position along the line, metres.</summary>
        public float X;

        /// <summary>0 at the authored distance, 1 at the closest dolly.</summary>
        [Range(0, 1)] public float Dolly;

        /// <summary>
        /// How far the dolly brings the camera in, metres, and the lens it ends
        /// on: together about 2.2x closer at full zoom. 16 m stops the camera
        /// before the foreground plants (sim z 22), so it never zooms into a
        /// fern. The owner asked for a zoom that is felt; 9 m alone was not.
        /// </summary>
        public const float DollyRange = 16f;
        public const float ZoomFov = 12f;

        /// <summary>Pan limit: the playfield plus enough to see the firebase and the jungle edge whole.</summary>
        public static float PanLimit => (float)Tune.HalfLength + 8f;

        public float PanSpeed = 16f;

        /// <summary>
        /// The pointer at the left or right edge of the view pans the camera:
        /// one finger on a trackpad, no button. Only over the scene itself (the
        /// bars and the deck keep their corners), and never while dragging.
        /// </summary>
        public bool EdgePan = true;
        /// <summary>The share of the view's width at each side that pans, and the band of its height that counts.</summary>
        public const float EdgeZone = 0.045f, EdgeBottom = 0.17f, EdgeTop = 0.84f;

#if UNITY_WEBGL && !UNITY_EDITOR
        // The browser's wheel and pointer, read by LovInput.jslib (why: there).
        [DllImport("__Internal")] private static extern void LovInput_Init();
        [DllImport("__Internal")] private static extern float LovInput_Zoom();
        [DllImport("__Internal")] private static extern float LovInput_Pan();
        [DllImport("__Internal")] private static extern float LovInput_Drag();
        [DllImport("__Internal")] private static extern float LovInput_PointerX();
        [DllImport("__Internal")] private static extern float LovInput_PointerY();
        [DllImport("__Internal")] private static extern int LovInput_Dragging();
        [DllImport("__Internal")] private static extern float LovInput_CanvasWidth();
        [DllImport("__Internal")] private static extern float LovInput_SteerX();
        [DllImport("__Internal")] private static extern float LovInput_SteerY();
        [DllImport("__Internal")] private static extern int LovInput_Steering();
        private bool _browser;
#endif
        private float _trauma;
        /// <summary>A blast nearby: shake the camera, 0..1. It adds up and dies away in half a second.</summary>
        public void Shake(float amount) { if (CaptureSettings.Active == null) _trauma = Mathf.Clamp01(_trauma + amount); }

        /// <summary>In a browser: the pointer is dragging the camera (so the release is not a click).</summary>
        public bool BrowserDragging { get; private set; }

        /// <summary>
        /// Field glasses (PLAN §12.7): held, the lens narrows from 19 degrees to
        /// about 7 toward the cursor. The aim moves by a bounded amount and comes
        /// back on release — never an orbit, so the composition holds. This is
        /// where brief §6's "readable at 400 px" is actually seen.
        /// </summary>
        public bool FieldGlasses;
        public const float GlassesFov = 7f;

        /// <summary>
        /// Where the zoom points: degrees up (+) or down from the lanes, toward what was under the pointer
        /// when the zoom went in, or where Option (or Cmd) and the mouse have steered it. The owner, playtest 8:
        /// "camera zooms only into one spot ... you should be able to zoom into whatever you want by holding
        /// down a key and moving the mouse". At the authored view it is none (the composition holds); it
        /// may be more the further in the zoom is.
        /// </summary>
        public float AimUp { get; private set; }
        private float _targetAimUp;
        /// <summary>The most the zoom may point off the lanes, degrees, at full zoom; and how far a pixel of steering turns it.</summary>
        public const float MaxAimUp = 7f, SteerDegreesPerPixel = 0.03f;
        public float Zoom { get; private set; }          // 0 = authored lens, 1 = glasses
        private Vector2 _glassesAim;                      // viewport offset from centre, -0.5..0.5

        private Camera _cam;
        private float _targetX, _targetDolly;
        private bool _dragging;
        private Vector3 _dragFrom;
        private float _dragX;

        public Camera Camera => _cam != null ? _cam : (_cam = GetComponent<Camera>());

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.fieldOfView = Coords.Camera.Fov;
            _cam.nearClipPlane = 0.5f;
            // The mountains reach 22 km (MountainView). A 0.5 m near plane still
            // leaves WebGL's 24-bit depth about a millimetre of precision at 100 m.
            _cam.farClipPlane = 24000f;
            _targetX = X;
            _targetDolly = Dolly;
            Apply();
        }

        /// <summary>Jump there now (capture, selection), or glide there.</summary>
        public void Focus(float x, bool instant = false)
        {
            _targetX = Mathf.Clamp(x, -PanLimit, PanLimit);
            if (instant) { X = _targetX; Apply(); }
        }

        public void SetDolly(float d, bool instant = false)
        {
            _targetDolly = Mathf.Clamp01(d);
            if (instant) { Dolly = _targetDolly; Apply(); }
        }

        private void LateUpdate()
        {
            if (CaptureSettings.Active == null) HandleInput(Time.unscaledDeltaTime);
            // Glasses come up quickly and go down a little slower, like hands.
            // Where they point is set by whoever raised them (AimGlasses): the
            // rig reading the mouse itself overrode every other caller — UIAudit
            // aimed right and the camera turned left, toward a headless mouse
            // parked at (0, 0).
            Zoom = Mathf.MoveTowards(Zoom, FieldGlasses ? 1f : 0f, Time.unscaledDeltaTime * (FieldGlasses ? 5f : 3.5f));
            // Critically damped glide toward the target: responsive, never overshoots.
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f);
            X = Mathf.Lerp(X, _targetX, k);
            Dolly = Mathf.Lerp(Dolly, _targetDolly, k);
            _targetAimUp = Mathf.Clamp(_targetAimUp, -MaxAimUp * _targetDolly, MaxAimUp * _targetDolly);
            AimUp = Mathf.Lerp(AimUp, _targetAimUp, k);
            Apply();
        }

        private void HandleInput(float dt)
        {
            float axis = 0;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) axis -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) axis += 1;
            if (axis != 0) _targetX = Mathf.Clamp(_targetX + axis * PanSpeed * dt, -PanLimit, PanLimit);

            // Two fingers (or the wheel) up and down zoom, sideways pan; one finger
            // with the button down drags the line, and at the view's edge pans.
            float zoom, sideways, drag = 0, px = -1, py = -1, cssToScreen = 1, steerX = 0, steerY = 0;
            bool steering;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_browser) { LovInput_Init(); _browser = true; }
            zoom = LovInput_Zoom(); sideways = LovInput_Pan(); drag = LovInput_Drag();
            px = LovInput_PointerX(); py = LovInput_PointerY();
            BrowserDragging = LovInput_Dragging() != 0;
            cssToScreen = Screen.width / Mathf.Max(1f, LovInput_CanvasWidth());
            steerX = LovInput_SteerX(); steerY = LovInput_SteerY(); steering = LovInput_Steering() != 0;
#else
            var wheel = Input.mouseScrollDelta;
            zoom = wheel.y * 0.12f;
            sideways = -wheel.x * 30f;
            var mouse = Input.mousePosition;
            if (Application.isFocused && mouse.x >= 0 && mouse.x <= Screen.width && mouse.y >= 0 && mouse.y <= Screen.height)
            { px = mouse.x / Screen.width; py = mouse.y / Screen.height; }
            steering = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (steering && _lastMouse.x >= 0) { steerX = mouse.x - _lastMouse.x; steerY = _lastMouse.y - mouse.y; }
            _lastMouse = mouse;
#endif
            // The zoom goes in toward what is under the pointer, not the middle of the picture.
            if (zoom != 0) ZoomToward(Mathf.Clamp(zoom, -0.6f, 0.6f), px, py);
            float mpp = MetresPerPixel() * cssToScreen;
            if (sideways != 0) PanBy(sideways * mpp);
            if (drag != 0) PanBy(-drag * mpp);                 // the ground follows the finger
            // Option (or Cmd) held: the mouse steers where the zoom points, along the line and up and down.
            if (steering)
            {
                if (steerX != 0) PanBy(steerX * mpp);
                if (steerY != 0) _targetAimUp -= steerY * cssToScreen * SteerDegreesPerPixel;
            }
            if (EdgePan && !steering && px >= 0 && py > EdgeBottom && py < EdgeTop && !_dragging && !BrowserDragging && !Input.GetMouseButton(0))
            {
                float push = px < EdgeZone ? -(1f - px / EdgeZone) : px > 1f - EdgeZone ? (px - (1f - EdgeZone)) / EdgeZone : 0f;
                if (push != 0) _targetX = Mathf.Clamp(_targetX + push * PanSpeed * 1.4f * dt, -PanLimit, PanLimit);
            }

            // Drag with the right or middle button (the left is Commander's: it
            // starts a drag only when the press was not a click on a man).
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) BeginDrag(Input.mousePosition);
            if (_dragging && !(Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))) _dragging = false;
            if (_dragging) DragTo(Input.mousePosition);
        }

        /// <summary>Pan by some metres along the line.</summary>
        public void PanBy(float metres) => _targetX = Mathf.Clamp(_targetX + metres, -PanLimit, PanLimit);

        /// <summary>Zoom in (+) or out (-): 1 is the whole range.</summary>
        public void ZoomBy(float amount) => _targetDolly = Mathf.Clamp01(_targetDolly + amount);

        private Vector3 _lastMouse = new Vector3(-1, -1, 0);

        /// <summary>
        /// Zoom, keeping what is under the pointer (viewport 0..1; -1, the middle) under it: the line pans as
        /// the view narrows, and the aim tilts toward it.
        /// </summary>
        public void ZoomToward(float amount, float px, float py)
        {
            float before = _targetDolly;
            ZoomBy(amount);
            if (px < 0 || py < 0) return;
            float aspect = Camera != null ? Camera.aspect : 16f / 9f;
            float Width(float d) => 2f * (Coords.Camera.SimZ - d * DollyRange - (float)Tune.Lanes[0])
                                    * Mathf.Tan(Mathf.Lerp(Coords.Camera.Fov, ZoomFov, d) * 0.5f * Mathf.Deg2Rad) * aspect;
            _targetX = Mathf.Clamp(_targetX + (px - 0.5f) * (Width(before) - Width(_targetDolly)), -PanLimit, PanLimit);
            _targetAimUp += (py - 0.5f) * (Mathf.Lerp(Coords.Camera.Fov, ZoomFov, before) - Mathf.Lerp(Coords.Camera.Fov, ZoomFov, _targetDolly));
        }

        /// <summary>Start a drag at a screen point: the ground under it stays under it.</summary>
        public void BeginDrag(Vector2 screen)
        {
            _dragging = true;
            _dragFrom = screen;
            _dragX = _targetX;
        }

        public void DragTo(Vector2 screen)
        {
            if (!_dragging) return;
            _targetX = Mathf.Clamp(_dragX - (screen.x - _dragFrom.x) * MetresPerPixel(), -PanLimit, PanLimit);
        }

        /// <summary>Metres a screen pixel spans at the near lane's distance, through the lens as it is now.</summary>
        public float MetresPerPixel()
        {
            float dist = Coords.Camera.SimZ - Dolly * DollyRange - (float)Tune.Lanes[0];
            float fov = Camera != null ? Camera.fieldOfView : Coords.Camera.Fov;
            return 2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
        }

        public void EndDrag() => _dragging = false;

        /// <summary>Point the glasses at a viewport position (0..1), as a click would. For UIAudit.</summary>
        public void AimGlasses(Vector2 viewport)
            => _glassesAim = new Vector2(Mathf.Clamp(viewport.x - 0.5f, -0.5f, 0.5f), Mathf.Clamp(viewport.y - 0.5f, -0.5f, 0.5f));

        /// <summary>A man's chest between the two lanes: what the zoom keeps in frame.</summary>
        private const float AnchorY = 1.0f;
        private static float AnchorSimZ => (float)(Tune.Lanes[0] + Tune.Lanes[1]) * 0.5f;

        /// <summary>Degrees below the horizontal, from a camera at this depth, to the lanes' anchor.</summary>
        private static float AnchorDrop(float camSimZ)
            => Mathf.Atan2(Coords.Camera.Height - AnchorY, camSimZ - AnchorSimZ) * Mathf.Rad2Deg;

        /// <summary>
        /// The extra pitch down that keeps the lanes where the authored view has
        /// them on the screen (~23% up), however far the dolly and the lens have
        /// zoomed. Without it the lanes left the bottom of the frame: the
        /// authored camera looks nearly level, and zoomed to 12 degrees from 16 m
        /// closer, the men were below the picture. 0 at the authored view.
        /// </summary>
        public static float ZoomPitch(float camSimZ, float lens)
        {
            var look = Coords.Camera.Rotation * Vector3.forward;
            float basePitch = -Mathf.Asin(look.y) * Mathf.Rad2Deg;
            float frac = Mathf.Tan((AnchorDrop(Coords.Camera.SimZ) - basePitch) * Mathf.Deg2Rad) / Mathf.Tan(Coords.Camera.Fov * 0.5f * Mathf.Deg2Rad);
            float off = Mathf.Atan(frac * Mathf.Tan(lens * 0.5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
            return AnchorDrop(camSimZ) - off - basePitch;
        }

        private void Apply()
        {
            float simZ = Coords.Camera.SimZ - Dolly * DollyRange;
            float e = Zoom * Zoom * (3f - 2f * Zoom);
            float lens = Mathf.Lerp(Coords.Camera.Fov, ZoomFov, Dolly);
            float fov = Mathf.Lerp(lens, GlassesFov, e);
            var cam = Camera;
            if (cam != null) cam.fieldOfView = fov;
            // Turn toward the point that was under the cursor, by the angle it
            // stood off-centre at the authored lens.
            float vHalf = Coords.Camera.Fov * 0.5f;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf * Mathf.Deg2Rad) * (cam != null ? cam.aspect : 16f / 9f)) * Mathf.Rad2Deg;
            float yaw = _glassesAim.x * 2f * hHalf * e, pitch = -_glassesAim.y * 2f * vHalf * e;
            pitch += ZoomPitch(simZ, lens) - AimUp;
            var rot = Coords.Camera.Rotation * Quaternion.Euler(pitch, yaw, 0);
            var pos = Coords.World(X, simZ, Coords.Camera.Height);
            if (_trauma > 0.001f)
            {
                // Squared, so a small blast barely moves it; a few centimetres and a fraction of a degree at most.
                float k = _trauma * _trauma, t = Time.unscaledTime * 38f;
                pos += new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0) * (0.22f * k);
                rot *= Quaternion.Euler((Mathf.PerlinNoise(t, 5.1f) - 0.5f) * 0.5f * k, (Mathf.PerlinNoise(9.2f, t) - 0.5f) * 0.5f * k, 0);
                _trauma = Mathf.Max(0, _trauma - Time.unscaledDeltaTime * 2.2f);
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
