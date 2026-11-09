// Copyright (c) MR-ExitReady contributors.

using UnityEngine;

namespace MRExitReady.Common
{
    /// <summary>
    /// Finds the headset and controller transforms once and caches them.
    /// Prefers the <see cref="OVRCameraRig"/> anchors and falls back to <see cref="Camera.main"/>.
    /// </summary>
    public static class HeadLocator
    {
        private static OVRCameraRig _rig;
        private static Transform _head;

        public static OVRCameraRig Rig
        {
            get
            {
                if (_rig == null)
                {
                    _rig = Object.FindAnyObjectByType<OVRCameraRig>();
                }

                return _rig;
            }
        }

        /// <summary>The centre-eye transform, or null if no camera exists yet.</summary>
        public static Transform Head
        {
            get
            {
                if (_head != null)
                {
                    return _head;
                }

                var rig = Rig;
                if (rig != null && rig.centerEyeAnchor != null)
                {
                    _head = rig.centerEyeAnchor;
                }
                else if (Camera.main != null)
                {
                    _head = Camera.main.transform;
                }

                return _head;
            }
        }

        /// <summary>True once head tracking reports a real pose (it is (0,0,0) for the first frames).</summary>
        public static bool HasTrackedHead => Head != null && Head.position != Vector3.zero;

        /// <summary>The headset's forward direction flattened onto the floor plane.</summary>
        public static Vector3 FlatForward
        {
            get
            {
                if (Head == null)
                {
                    return Vector3.forward;
                }

                var forward = Head.forward;
                forward.y = 0f;
                return forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
            }
        }

        public static Transform LeftHand => Rig != null ? Rig.leftControllerAnchor : null;
        public static Transform RightHand => Rig != null ? Rig.rightControllerAnchor : null;

        public static Transform Hand(OVRInput.Controller controller) =>
            controller == OVRInput.Controller.LTouch ? LeftHand : RightHand;
    }
}
