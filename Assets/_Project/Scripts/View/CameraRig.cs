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

        private void Apply()
        {
            float simZ = Coords.Camera.SimZ - Dolly * DollyRange;
            transform.SetPositionAndRotation(Coords.World(X, simZ, Coords.Camera.Height), Coords.Camera.Rotation);
        }
    }
}
