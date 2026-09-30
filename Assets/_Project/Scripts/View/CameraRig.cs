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
        /// Field glasses (PLAN §12.7): held, the lens narrows from 19 degrees to
        /// about 7 toward the cursor. The aim moves by a bounded amount and comes
        /// back on release — never an orbit, so the composition holds. This is
        /// where brief §6's "readable at 400 px" is actually seen.
        /// </summary>
        public bool FieldGlasses;
        public const float GlassesFov = 7f;
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
            Apply();
        }

        private void HandleInput(float dt)
        {
            float axis = 0;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) axis -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) axis += 1;
            if (axis != 0) _targetX = Mathf.Clamp(_targetX + axis * PanSpeed * dt, -PanLimit, PanLimit);

            // The wheel zooms; a trackpad's sideways swipe pans.
            var wheel = Input.mouseScrollDelta;
            if (wheel.y != 0) ZoomBy(wheel.y * 0.12f);
            if (wheel.x != 0) PanBy(-wheel.x * 1.5f);

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
            // Metres per pixel at the near lane's distance, through the lens as it is now.
            float dist = Coords.Camera.SimZ - Dolly * DollyRange - (float)Tune.Lanes[0];
            float fov = Camera != null ? Camera.fieldOfView : Coords.Camera.Fov;
            float mpp = 2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Screen.height;
            _targetX = Mathf.Clamp(_dragX - (screen.x - _dragFrom.x) * mpp, -PanLimit, PanLimit);
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
            pitch += ZoomPitch(simZ, lens);
            var rot = Coords.Camera.Rotation * Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(Coords.World(X, simZ, Coords.Camera.Height), rot);
        }
    }
}
