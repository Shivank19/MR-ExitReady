// Copyright (c) MR-ExitReady contributors.

using System.Collections;
using UnityEngine;

namespace MRExitReady.Debugging
{
    /// <summary>
    /// Places a world-space panel in front of the headset, facing the user.
    ///
    /// Why this exists: MRUK's own <c>SceneDebugger.MoveCanvasInFrontOfCamera</c> only sets the
    /// canvas <i>position</i> and leaves the rotation at identity, so the debug menu ends up
    /// facing world +Z. Unless you happen to be looking down +Z when the app starts, the panel
    /// is edge-on or facing away from you. This component sets position *and* a yaw-only
    /// rotation, and lets you re-summon the panel at any time with the B/Y button.
    ///
    /// Attach it to the object that owns the canvas (or set <see cref="target"/>), and turn
    /// <c>MoveCanvasInFrontOfCamera</c> off on the SceneDebugger so the two don't fight.
    /// </summary>
    public class DebugPanelPlacer : MonoBehaviour
    {
        [Tooltip("Transform to move. Defaults to this object's transform.")]
        [SerializeField] private Transform target;

        [Tooltip("Metres in front of the headset.")]
        [SerializeField] private float distance = 0.8f;

        [Tooltip("Metres above (+) or below (-) eye height.")]
        [SerializeField] private float verticalOffset = -0.1f;

        [Tooltip("Place the panel once head tracking is live.")]
        [SerializeField] private bool placeOnStart = true;

        [Tooltip("Re-summon the panel in front of you (B on the right controller, Y on the left).")]
        [SerializeField] private bool allowRecenterButton = true;

        private Transform _head;

        private void Awake()
        {
            if (target == null)
            {
                target = transform;
            }
        }

        private IEnumerator Start()
        {
            // Head tracking reports (0,0,0) for the first few frames; placing before then
            // would pin the panel to the tracking-space origin.
            const float timeout = 5f;
            var waited = 0f;
            while (waited < timeout && ResolveHead() == null)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            while (waited < timeout && _head != null && _head.position == Vector3.zero)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (placeOnStart)
            {
                Place();
            }
        }

        private void Update()
        {
            if (allowRecenterButton && OVRInput.GetDown(OVRInput.RawButton.B | OVRInput.RawButton.Y))
            {
                Place();
            }
        }

        /// <summary>Snaps the target in front of the headset, upright and facing the user.</summary>
        public void Place()
        {
            if (ResolveHead() == null)
            {
                return;
            }

            var forward = _head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            target.position = _head.position + forward * distance + Vector3.up * verticalOffset;

            // A world-space canvas renders its content facing its own +Z, i.e. away from the
            // viewer, so the panel's forward matches the head's forward rather than opposing it.
            target.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private Transform ResolveHead()
        {
            if (_head != null)
            {
                return _head;
            }

            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                _head = rig.centerEyeAnchor;
                return _head;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                _head = cam.transform;
            }

            return _head;
        }
    }
}
