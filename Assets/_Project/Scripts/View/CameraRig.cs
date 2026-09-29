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

        /// <summary>How far the dolly brings the camera in, metres.</summary>
        public const float DollyRange = 9f;

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
            _cam.farClipPlane = 4000f;
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

            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0) _targetDolly = Mathf.Clamp01(_targetDolly + wheel * 0.12f);

            // Drag with the right or middle button: the ground under the cursor
            // stays under the cursor.
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                _dragging = true;
                _dragFrom = Input.mousePosition;
                _dragX = _targetX;
            }
            if (_dragging && !(Input.GetMouseButton(1) || Input.GetMouseButton(2))) _dragging = false;
            if (_dragging)
            {
                // Metres per pixel at the near lane's distance.
                float dist = Coords.Camera.SimZ - (float)Tune.Lanes[0];
                float mpp = 2f * dist * Mathf.Tan(Coords.Camera.Fov * 0.5f * Mathf.Deg2Rad) / Screen.height;
                _targetX = Mathf.Clamp(_dragX - (Input.mousePosition.x - _dragFrom.x) * mpp, -PanLimit, PanLimit);
            }
        }

        /// <summary>Point the glasses at a viewport position (0..1), as a click would. For UIAudit.</summary>
        public void AimGlasses(Vector2 viewport)
            => _glassesAim = new Vector2(Mathf.Clamp(viewport.x - 0.5f, -0.5f, 0.5f), Mathf.Clamp(viewport.y - 0.5f, -0.5f, 0.5f));

        private void Apply()
        {
            float simZ = Coords.Camera.SimZ - Dolly * DollyRange;
            float e = Zoom * Zoom * (3f - 2f * Zoom);
            float fov = Mathf.Lerp(Coords.Camera.Fov, GlassesFov, e);
            var cam = Camera;
            if (cam != null) cam.fieldOfView = fov;
            // Turn toward the point that was under the cursor, by the angle it
            // stood off-centre at the authored lens.
            float vHalf = Coords.Camera.Fov * 0.5f;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf * Mathf.Deg2Rad) * (cam != null ? cam.aspect : 16f / 9f)) * Mathf.Rad2Deg;
            float yaw = _glassesAim.x * 2f * hHalf * e, pitch = -_glassesAim.y * 2f * vHalf * e;
            var rot = Coords.Camera.Rotation * Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(Coords.World(X, simZ, Coords.Camera.Height), rot);
        }
    }
}
