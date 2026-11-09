// Copyright (c) MR-ExitReady contributors.

using MRExitReady.Common;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// Touch-controller helpers for the drill: per-hand grip/trigger state, hand positions and
    /// timed haptic pulses. Call <see cref="Tick"/> once per frame so pulses stop on time.
    /// </summary>
    public static class ControllerInput
    {
        public static readonly OVRInput.Controller[] Hands = { OVRInput.Controller.LTouch, OVRInput.Controller.RTouch };

        private static float _leftHapticUntil;
        private static float _rightHapticUntil;

        public static Transform Anchor(OVRInput.Controller hand) => HeadLocator.Hand(hand);

        public static bool TryGetPosition(OVRInput.Controller hand, out Vector3 position)
        {
            var anchor = Anchor(hand);
            if (anchor == null || !OVRInput.IsControllerConnected(hand))
            {
                position = default;
                return false;
            }

            position = anchor.position;
            return true;
        }

        public static bool Trigger(OVRInput.Controller hand) =>
            OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, hand) > 0.55f;

        public static bool TriggerDown(OVRInput.Controller hand) =>
            OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, hand);

        public static bool Grip(OVRInput.Controller hand) =>
            OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand) > 0.55f;

        public static bool GripDown(OVRInput.Controller hand) =>
            OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, hand);

        /// <summary>True when neither trigger nor grip is pressed on that hand.</summary>
        public static bool Idle(OVRInput.Controller hand) =>
            OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, hand) < 0.2f &&
            OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand) < 0.2f;

        /// <summary>Vibrates one controller for <paramref name="seconds"/>.</summary>
        public static void Pulse(OVRInput.Controller hand, float amplitude, float seconds, float frequency = 0.5f)
        {
            OVRInput.SetControllerVibration(frequency, Mathf.Clamp01(amplitude), hand);
            var until = Time.unscaledTime + seconds;
            if (hand == OVRInput.Controller.LTouch)
            {
                _leftHapticUntil = Mathf.Max(_leftHapticUntil, until);
            }
            else
            {
                _rightHapticUntil = Mathf.Max(_rightHapticUntil, until);
            }
        }

        public static void Tick()
        {
            var now = Time.unscaledTime;
            if (_leftHapticUntil > 0f && now >= _leftHapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                _leftHapticUntil = 0f;
            }

            if (_rightHapticUntil > 0f && now >= _rightHapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                _rightHapticUntil = 0f;
            }
        }

        public static void StopAll()
        {
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
            _leftHapticUntil = 0f;
            _rightHapticUntil = 0f;
        }
    }
}
