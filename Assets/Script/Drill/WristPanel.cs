// Copyright (c) MR-ExitReady contributors.

using MRExitReady.Common;
using TMPro;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// A small "watch" panel that floats just above the left controller and turns to face the
    /// eyes, so the drill timer and tasks are a glance at the wrist away instead of always in
    /// front of the user.
    /// </summary>
    public class WristPanel
    {
        private const OVRInput.Controller Hand = OVRInput.Controller.LTouch;

        /// <summary>Height above the controller, in metres.</summary>
        public float Lift = 0.085f;

        /// <summary>How far the panel is pulled from the controller towards the head, in metres.</summary>
        public float TowardHead = 0.02f;

        private readonly Transform _root;
        private readonly TextPanel _panel;

        public TextMeshProUGUI Text => _panel.Text;

        public bool Visible
        {
            get => _panel.Visible;
            set => _panel.Visible = value;
        }

        public WristPanel(Transform parent, Color background)
        {
            _root = new GameObject("WristHud").transform;
            _root.SetParent(parent, false);
            _panel = new TextPanel(_root, new Vector2(200f, 175f), 12.5f, background, wrap: true);
        }

        public void SetBackground(Color color) => _panel.SetBackground(color);

        /// <summary>True when the left controller is tracked, so the panel can be placed on it.</summary>
        public static bool Available
        {
            get
            {
                var hand = HeadLocator.LeftHand;
                return hand != null && HeadLocator.HasTrackedHead &&
                       OVRInput.IsControllerConnected(Hand) && OVRInput.GetControllerPositionTracked(Hand);
            }
        }

        /// <summary>Call once per frame while visible. Returns false if the controller isn't tracked.</summary>
        public bool Follow()
        {
            if (!Available)
            {
                return false;
            }

            var hand = HeadLocator.LeftHand.position;
            var head = HeadLocator.Head.position;
            var toHead = head - hand;
            var position = hand + Vector3.up * Lift +
                           (toHead.sqrMagnitude > 1e-4f ? toHead.normalized * TowardHead : Vector3.zero);
            _root.position = position;

            // Face the eyes (canvas content faces its +Z, i.e. away from the viewer).
            var look = position - head;
            if (look.sqrMagnitude > 1e-4f)
            {
                _root.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }

            return true;
        }
    }
}
