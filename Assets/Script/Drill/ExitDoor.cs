// Copyright (c) MR-ExitReady contributors.

using System;
using TMPro;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// An exit placed over a scanned door frame (or a virtual one on a wall).
    ///
    /// <b>Check</b>: hold a controller flat against the door, without pressing anything, for
    /// <see cref="CheckSeconds"/>. The door reports HOT (strong buzz, red) or cool (soft buzz, blue).
    /// <b>Open</b>: squeeze grip with a hand on the door. Opening a cool door is an escape;
    /// opening the hot one ends the drill as a failure.
    /// </summary>
    public class ExitDoor : MonoBehaviour
    {
        private const float CheckSeconds = 1f;
        private const float TouchDepth = 0.18f;   // how close to the door plane counts as touching
        private const float EdgeSlack = 0.1f;     // extra margin around the frame

        public string DisplayName { get; private set; }
        public bool IsHot { get; private set; }
        public bool IsVirtual { get; private set; }
        public bool Checked { get; private set; }
        public Vector3 Center => transform.position;
        public Vector3 Normal => transform.forward;

        /// <summary>Raised once per door the first time it is checked. Arg: was it hot.</summary>
        public event Action<ExitDoor> OnChecked;

        /// <summary>Raised when the user squeezes grip on the door.</summary>
        public event Action<ExitDoor> OnOpened;

        private float _width;
        private float _height;
        private float _dwell;
        private TextMeshPro _status;
        private Renderer[] _frame;
        private Material _frameIdle;
        private Material _frameHot;
        private Material _frameCool;
        private ParticleSystem _seep;
        private Light _underGlowLight;
        private Material _underGlow;
        private Transform _revealGlow;
        private Material _revealMaterial;
        private float _revealTime = -1f;
        private bool _locked;
        private Func<OVRInput.Controller, bool> _handBusy;

        /// <param name="center">Centre of the door opening, on the wall plane.</param>
        /// <param name="normal">Direction pointing into the room.</param>
        /// <param name="handBusy">Returns true if that hand is holding something (can't open/check).</param>
        public static ExitDoor Create(Transform parent, ProceduralAssets assets, string name, Vector3 center,
            Vector3 normal, float width, float height, bool isHot, bool isVirtual,
            Func<OVRInput.Controller, bool> handBusy)
        {
            var go = new GameObject($"Exit_{name}");
            go.transform.SetParent(parent, false);
            normal.y = 0f;
            go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(normal.normalized, Vector3.up));

            var door = go.AddComponent<ExitDoor>();
            door.DisplayName = name;
            door.IsHot = isHot;
            door.IsVirtual = isVirtual;
            door._width = Mathf.Clamp(width, 0.6f, 2.5f);
            door._height = Mathf.Clamp(height, 1.6f, 2.6f);
            door._handBusy = handBusy;
            door.Build(assets);
            return door;
        }

        private void Build(ProceduralAssets assets)
        {
            var idle = new Color(0.9f, 0.9f, 0.9f);
            _frameIdle = assets.Prop(idle);
            _frameHot = assets.Prop(new Color(1f, 0.15f, 0.05f));
            _frameCool = assets.Prop(new Color(0.2f, 0.6f, 1f));

            // Thin outline just in front of the real frame so it doesn't z-fight with the wall.
            const float bar = 0.03f;
            const float lift = 0.02f;
            var hw = _width * 0.5f;
            var hh = _height * 0.5f;
            var parts = new[]
            {
                assets.Primitive(PrimitiveType.Cube, "Left", transform, new Vector3(-hw, 0f, lift), new Vector3(bar, _height, bar), idle),
                assets.Primitive(PrimitiveType.Cube, "Right", transform, new Vector3(hw, 0f, lift), new Vector3(bar, _height, bar), idle),
                assets.Primitive(PrimitiveType.Cube, "Top", transform, new Vector3(0f, hh, lift), new Vector3(_width + bar, bar, bar), idle),
            };
            _frame = new Renderer[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                _frame[i] = parts[i].GetComponent<Renderer>();
            }

            // Green EXIT sign above the door, as in a real building.
            var signColor = new Color(0.05f, 0.6f, 0.2f);
            assets.Primitive(PrimitiveType.Cube, "ExitSign", transform, new Vector3(0f, hh + 0.15f, lift),
                new Vector3(0.36f, 0.14f, 0.03f), signColor);
            var exitText = ProceduralAssets.Label("ExitText", transform, new Vector3(0f, hh + 0.15f, lift + 0.017f),
                "EXIT", 0.9f, Color.white);
            exitText.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Make the sign glow like a lit emergency sign: a soft green halo and a little light.
            GlowQuad("ExitGlow", new Vector3(0f, hh + 0.15f, lift + 0.02f), new Vector2(0.8f, 0.42f),
                new Color(0.2f, 1f, 0.4f, 0.45f), assets);
            var signLight = new GameObject("ExitLight").AddComponent<Light>();
            signLight.transform.SetParent(transform, false);
            signLight.transform.localPosition = new Vector3(0f, hh + 0.1f, 0.25f);
            signLight.type = LightType.Point;
            signLight.color = new Color(0.3f, 1f, 0.45f);
            signLight.range = 1.3f;
            signLight.intensity = 0.8f;
            signLight.shadows = LightShadows.None;

            _status = ProceduralAssets.Label("Status", transform, new Vector3(0f, 0.15f, 0.12f), "", 0.7f, Color.white);
            _status.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            if (IsHot)
            {
                // Grey smoke seeping out under the door: the visual cue a careful person notices.
                _seep = ProceduralAssets.Particles("SmokeSeep", transform, assets.ParticleAlpha);
                _seep.transform.localPosition = new Vector3(0f, -hh + 0.03f, 0.05f);
                _seep.transform.localRotation = Quaternion.identity;
                var main = _seep.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
                main.startColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
                main.gravityModifier = -0.03f;
                var shape = _seep.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(_width * 0.9f, 0.01f, 0.02f);
                var emission = _seep.emission;
                emission.rateOverTime = 26f;
                var col = _seep.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(0.2f, 0.2f, 0.2f, 0f), 0f),
                    (new Color(0.2f, 0.2f, 0.2f, 0.45f), 0.2f),
                    (new Color(0.3f, 0.3f, 0.3f, 0f), 1f));
                _seep.Play();

                // Firelight flickering in the gap under the door.
                _underGlow = GlowQuad("UnderDoorGlow", new Vector3(0f, -hh + 0.015f, 0.03f),
                    new Vector2(_width * 1.1f, 0.12f), new Color(1f, 0.45f, 0.1f, 0.7f), assets);
                _underGlowLight = new GameObject("UnderDoorLight").AddComponent<Light>();
                _underGlowLight.transform.SetParent(transform, false);
                _underGlowLight.transform.localPosition = new Vector3(0f, -hh + 0.05f, 0.2f);
                _underGlowLight.type = LightType.Point;
                _underGlowLight.color = new Color(1f, 0.45f, 0.12f);
                _underGlowLight.range = 1.2f;
                _underGlowLight.shadows = LightShadows.None;
            }

            // Shown across the door once it has been checked: orange throb if hot, a blue flash if cool.
            _revealMaterial = new Material(assets.ParticleAdditive) { name = "Drill_DoorReveal" };
            ProceduralAssets.SetColor(_revealMaterial, Color.clear);
            _revealGlow = GlowQuadWith(_revealMaterial, "RevealGlow", new Vector3(0f, 0f, 0.04f),
                new Vector2(_width * 1.4f, _height * 1.1f)).transform;
            _revealGlow.gameObject.SetActive(false);
        }

        /// <summary>A soft additive glow facing into the room.</summary>
        private Material GlowQuad(string name, Vector3 localPosition, Vector2 size, Color color, ProceduralAssets assets)
        {
            var material = new Material(assets.ParticleAdditive) { name = "Drill_" + name };
            ProceduralAssets.SetColor(material, color);
            GlowQuadWith(material, name, localPosition, size);
            return material;
        }

        private GameObject GlowQuadWith(Material material, string name, Vector3 localPosition, Vector2 size)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = localPosition;
            // A quad's visible side faces -Z; turn it to face +Z, into the room.
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad;
        }

        private void LateUpdate()
        {
            var t = Time.time;
            if (_underGlow != null)
            {
                var flicker = 0.55f + 0.3f * Mathf.PerlinNoise(t * 3f, 1.7f) + 0.15f * Mathf.Sin(t * 17f);
                ProceduralAssets.SetColor(_underGlow, new Color(1f, 0.45f, 0.1f, 0.75f * flicker));
                _underGlowLight.intensity = 1.6f * flicker;
            }

            if (_revealTime >= 0f)
            {
                var age = t - _revealTime;
                Color c;
                if (IsHot)
                {
                    var throb = 0.6f + 0.4f * Mathf.Sin(age * 6f);
                    c = new Color(1f, 0.35f, 0.05f, 0.35f * throb * Mathf.Clamp01(age * 3f));
                }
                else
                {
                    // Cool: a brief blue wash that fades out.
                    c = new Color(0.3f, 0.65f, 1f, 0.35f * Mathf.Clamp01(1f - age / 2f));
                }

                ProceduralAssets.SetColor(_revealMaterial, c);
            }
        }

        /// <summary>Stops all interaction (end of drill).</summary>
        public void Lock() => _locked = true;

        private void Update()
        {
            if (_locked)
            {
                return;
            }

            var anyTouching = false;
            foreach (var hand in ControllerInput.Hands)
            {
                if (!ControllerInput.TryGetPosition(hand, out var p) || !IsTouching(p) ||
                    (_handBusy != null && _handBusy(hand)))
                {
                    continue;
                }

                anyTouching = true;

                if (ControllerInput.GripDown(hand))
                {
                    OnOpened?.Invoke(this);
                    return;
                }

                if (!Checked && ControllerInput.Idle(hand))
                {
                    _dwell += Time.deltaTime;
                    // A faint buzz while checking, so the user knows the touch registered.
                    ControllerInput.Pulse(hand, 0.1f, 0.05f, 0.2f);
                    if (_dwell >= CheckSeconds)
                    {
                        Reveal(hand);
                    }
                }
            }

            if (!anyTouching)
            {
                _dwell = 0f;
            }
        }

        private bool IsTouching(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            return local.z > -0.05f && local.z < TouchDepth &&
                   Mathf.Abs(local.x) < _width * 0.5f + EdgeSlack &&
                   Mathf.Abs(local.y) < _height * 0.5f + EdgeSlack;
        }

        private void Reveal(OVRInput.Controller hand)
        {
            Checked = true;
            _dwell = 0f;

            var material = IsHot ? _frameHot : _frameCool;
            foreach (var r in _frame)
            {
                r.sharedMaterial = material;
            }

            _revealTime = Time.time;
            _revealGlow.gameObject.SetActive(true);
            _status.text = IsHot ? "HOT - don't open" : "Cool - safe to open";
            _status.color = IsHot ? new Color(1f, 0.4f, 0.3f) : new Color(0.5f, 0.8f, 1f);
            ControllerInput.Pulse(hand, IsHot ? 1f : 0.3f, IsHot ? 0.6f : 0.15f, IsHot ? 1f : 0.3f);
            OnChecked?.Invoke(this);
        }
    }
}
