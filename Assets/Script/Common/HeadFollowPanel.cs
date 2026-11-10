// Copyright (c) MR-ExitReady contributors.

using TMPro;
using UnityEngine;

namespace MRExitReady.Common
{
    /// <summary>
    /// A world-space text panel that lazily follows the headset: it stays put while the user
    /// looks around inside a dead zone, then eases back in front of them. Built entirely from
    /// code, so there is nothing to wire up in the Inspector.
    /// </summary>
    public class HeadFollowPanel
    {
        public float Distance = 0.85f;
        public float VerticalOffset = -0.3f;
        public float DeadZoneAngle = 30f;
        public float FollowSpeed = 3f;

        private readonly Transform _root;
        private readonly TextPanel _panel;
        private bool _placed;

        public TextMeshProUGUI Text => _panel.Text;

        /// <summary>Right column, or null for a single-column panel.</summary>
        public TextMeshProUGUI Text2 => _panel.Text2;

        public bool Visible
        {
            get => _panel.Visible;
            set
            {
                if (value && !_panel.Visible)
                {
                    // Reappear in front of the user rather than wherever it was last left.
                    _placed = false;
                }

                _panel.Visible = value;
            }
        }

        /// <param name="root">Transform that is moved around; the canvas is created as its child.</param>
        /// <param name="sizeMm">Panel size in millimetres.</param>
        public HeadFollowPanel(Transform root, Vector2 sizeMm, float textSize, Color background, int columns = 1,
            bool wrap = false)
        {
            _root = root;
            _panel = new TextPanel(root, sizeMm, textSize, background, columns, wrap);
        }

        public void SetBackground(Color color) => _panel.SetBackground(color);

        /// <summary>Forces the panel to snap in front of the user on the next <see cref="Follow"/>.</summary>
        public void Recenter() => _placed = false;

        /// <summary>Call once per frame.</summary>
        public void Follow()
        {
            if (!HeadLocator.HasTrackedHead || !_panel.Visible)
            {
                return;
            }

            var head = HeadLocator.Head;
            var headForward = HeadLocator.FlatForward;
            var desired = head.position + headForward * Distance + Vector3.up * VerticalOffset;

            if (!_placed)
            {
                // Snap the first time head tracking is live so the panel doesn't fly in from
                // the tracking origin.
                _root.position = desired;
                _placed = true;
            }
            else
            {
                var toPanel = _root.position - head.position;
                toPanel.y = 0f;
                var outside = toPanel.sqrMagnitude < 1e-4f ||
                              Vector3.Angle(headForward, toPanel.normalized) > DeadZoneAngle;
                if (outside)
                {
                    var t = 1f - Mathf.Exp(-FollowSpeed * Time.unscaledDeltaTime);
                    _root.position = Vector3.Lerp(_root.position, desired, t);
                }
            }

            // A world-space canvas shows its content facing its own +Z, so the panel faces
            // away from the head (same direction as the view ray).
            var look = _root.position - head.position;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f)
            {
                _root.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }
    }
}
