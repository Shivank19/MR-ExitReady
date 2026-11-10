// Copyright (c) MR-ExitReady contributors.

using System;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// A wall-mounted manual pull station. Put either controller on the handle and press the
    /// trigger to pull it: the siren starts and a white strobe flashes.
    /// </summary>
    public class FireAlarmStation : MonoBehaviour
    {
        private const float ReachDistance = 0.14f;

        public bool Pulled { get; private set; }

        /// <summary>True on the frames the strobe is flashing (used to flash the room).</summary>
        public bool StrobeOn { get; private set; }
        public event Action OnPulled;

        private Transform _handle;
        private Renderer _handleRenderer;
        private Material _handleIdle;
        private Material _handleHover;
        private Light _strobe;
        private Renderer _strobeLens;
        private Material _lensOff;
        private Material _lensOn;
        private AudioSource _siren;
        private Material _beacon;
        private Light _redGlow;
        private ProceduralAssets _assets;

        /// <param name="position">Point on the wall surface.</param>
        /// <param name="normal">Wall normal, pointing into the room.</param>
        public static FireAlarmStation Create(Transform parent, ProceduralAssets assets, Vector3 position, Vector3 normal)
        {
            var go = new GameObject("FireAlarm");
            go.transform.SetParent(parent, false);
            // Local +Z points out of the wall, into the room.
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal, Vector3.up));

            var alarm = go.AddComponent<FireAlarmStation>();
            alarm._assets = assets;
            alarm.Build();
            return alarm;
        }

        private void Build()
        {
            var red = new Color(0.85f, 0.08f, 0.06f);
            var white = new Color(0.95f, 0.95f, 0.95f);

            _assets.Primitive(PrimitiveType.Cube, "Body", transform, new Vector3(0f, 0f, 0.03f),
                new Vector3(0.13f, 0.17f, 0.06f), red);

            var handle = _assets.Primitive(PrimitiveType.Cube, "Handle", transform, new Vector3(0f, -0.02f, 0.07f),
                new Vector3(0.09f, 0.035f, 0.03f), white);
            _handle = handle.transform;
            _handleRenderer = handle.GetComponent<Renderer>();
            _handleIdle = _assets.Prop(white);
            _handleHover = _assets.Prop(new Color(1f, 0.9f, 0.3f));

            var label = ProceduralAssets.Label("Label", transform, new Vector3(0f, 0.05f, 0.061f), "FIRE", 0.35f, white);
            // TextMeshPro faces -Z; flip so it reads from the room side.
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var pull = ProceduralAssets.Label("Pull", transform, new Vector3(0f, -0.055f, 0.061f), "PULL DOWN", 0.18f, white);
            pull.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Strobe above the station.
            var lens = _assets.Primitive(PrimitiveType.Cube, "StrobeLens", transform, new Vector3(0f, 0.16f, 0.03f),
                new Vector3(0.08f, 0.05f, 0.05f), new Color(0.85f, 0.85f, 0.85f));
            _strobeLens = lens.GetComponent<Renderer>();
            _lensOff = _assets.Prop(new Color(0.85f, 0.85f, 0.85f));
            _lensOn = _assets.Prop(Color.white * 1.5f);

            var lightGo = new GameObject("Strobe");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.16f, 0.15f);
            _strobe = lightGo.AddComponent<Light>();
            _strobe.type = LightType.Point;
            _strobe.range = 6f;
            _strobe.intensity = 6f;
            _strobe.color = Color.white;
            _strobe.enabled = false;

            // Until it is pulled, a soft red beacon pulses around the station so it can be found
            // in a smoky room; afterwards a red glow washes the wall.
            var beaconQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            beaconQuad.name = "Beacon";
            Destroy(beaconQuad.GetComponent<Collider>());
            beaconQuad.transform.SetParent(transform, false);
            beaconQuad.transform.localPosition = new Vector3(0f, 0.03f, 0.065f);
            beaconQuad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            beaconQuad.transform.localScale = new Vector3(0.55f, 0.6f, 1f);
            _beacon = new Material(_assets.ParticleAdditive) { name = "Drill_AlarmBeacon" };
            ProceduralAssets.SetColor(_beacon, new Color(1f, 0.1f, 0.05f, 0f));
            var beaconRenderer = beaconQuad.GetComponent<MeshRenderer>();
            beaconRenderer.sharedMaterial = _beacon;
            beaconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var glowGo = new GameObject("RedGlow");
            glowGo.transform.SetParent(transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            _redGlow = glowGo.AddComponent<Light>();
            _redGlow.type = LightType.Point;
            _redGlow.color = new Color(1f, 0.1f, 0.05f);
            _redGlow.range = 1.2f;
            _redGlow.intensity = 0.6f;
            _redGlow.shadows = LightShadows.None;

            _siren = ProceduralAssets.LoopingSource(gameObject, _assets.Siren, 0.6f, 0.4f, 20f, playNow: false);
        }

        private void Update()
        {
            if (Pulled)
            {
                // ~1.5 Hz strobe with a short on-time, like a real visual alarm.
                var on = Mathf.Repeat(Time.time, 0.66f) < 0.1f;
                StrobeOn = on;
                _strobe.enabled = on;
                _redGlow.intensity = on ? 2.5f : 1.2f;
                ProceduralAssets.SetColor(_beacon, new Color(1f, 0.1f, 0.05f, on ? 0.9f : 0.35f));
                _strobeLens.sharedMaterial = on ? _lensOn : _lensOff;
                return;
            }

            var hovering = false;
            foreach (var hand in ControllerInput.Hands)
            {
                if (!ControllerInput.TryGetPosition(hand, out var p) ||
                    Vector3.Distance(p, _handle.position) > ReachDistance)
                {
                    continue;
                }

                hovering = true;
                if (ControllerInput.TriggerDown(hand))
                {
                    Pull(hand);
                    return;
                }
            }

            _handleRenderer.sharedMaterial = hovering ? _handleHover : _handleIdle;

            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            ProceduralAssets.SetColor(_beacon, new Color(1f, 0.1f, 0.05f, hovering ? 0.8f : 0.15f + 0.35f * pulse));
            _redGlow.intensity = 0.3f + 0.6f * pulse;
        }

        private void Pull(OVRInput.Controller hand)
        {
            Pulled = true;
            _handle.localPosition += new Vector3(0f, -0.04f, 0f);
            _handleRenderer.sharedMaterial = _handleIdle;
            _siren.Play();
            ControllerInput.Pulse(hand, 0.8f, 0.2f);
            OnPulled?.Invoke();
        }

        /// <summary>Silences the siren and strobe (end of drill).</summary>
        public void Silence()
        {
            _siren.Stop();
            _strobe.enabled = false;
            StrobeOn = false;
            _redGlow.enabled = false;
            ProceduralAssets.SetColor(_beacon, new Color(1f, 0.1f, 0.05f, 0f));
            enabled = false;
        }
    }
}
