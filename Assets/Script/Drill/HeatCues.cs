// Copyright (c) MR-ExitReady contributors.

using MRExitReady.Common;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// Danger feedback that grows as the user gets close to the fire: a red heat vignette around
    /// the edge of the view, a louder and slightly higher crackle, and a heartbeat rumble in both
    /// controllers that speeds up with the heat. <see cref="Heat"/> (0-1) also feeds the score.
    /// </summary>
    public class HeatCues : MonoBehaviour
    {
        /// <summary>0 = far away (or no fire), 1 = standing in the flames.</summary>
        public float Heat { get; private set; }

        /// <summary>Distance from the head to the centre of the flames, in metres.</summary>
        public float FireDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>Closer than this to the flames always counts as full heat.</summary>
        private const float CoreRadius = 0.4f;

        private static readonly Color VignetteColor = new(1f, 0.22f, 0.04f, 1f);

        private FireHazard _fire;
        private GameObject _vignette;
        private Material _vignetteMaterial;
        private float _nextBeat;

        public static HeatCues Create(Transform parent, ProceduralAssets assets, FireHazard fire)
        {
            var go = new GameObject("HeatCues");
            go.transform.SetParent(parent, false);
            var cues = go.AddComponent<HeatCues>();
            cues._fire = fire;
            cues.Build(assets);
            return cues;
        }

        private void Build(ProceduralAssets assets)
        {
            var head = HeadLocator.Head;
            if (head == null)
            {
                return;
            }

            // A quad just in front of the eyes, clear in the middle and red at the edges.
            _vignette = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _vignette.name = "HeatVignette";
            Destroy(_vignette.GetComponent<Collider>());
            _vignette.transform.SetParent(head, false);
            _vignette.transform.localPosition = new Vector3(0f, 0f, 0.115f);
            _vignette.transform.localScale = new Vector3(0.62f, 0.48f, 1f);

            _vignetteMaterial = new Material(assets.SmokeVolume) { name = "Drill_HeatVignette" };
            ProceduralAssets.SetTexture(_vignetteMaterial, MakeVignetteTexture(128));
            ProceduralAssets.SetColor(_vignetteMaterial, Transparent());
            var renderer = _vignette.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _vignetteMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _vignette.SetActive(false);
        }

        private void Update()
        {
            var target = 0f;
            var head = HeadLocator.Head;
            if (_fire != null && !_fire.IsOut && head != null && HeadLocator.HasTrackedHead)
            {
                FireDistance = Vector3.Distance(head.position, _fire.Center);

                // A bigger fire is felt from further away: about 1.4 m for a small one, 3 m at full size.
                var reach = 1.4f + 1.6f * _fire.Intensity;
                target = Mathf.Clamp01((reach - FireDistance) / (reach - CoreRadius));
            }
            else
            {
                FireDistance = float.PositiveInfinity;
            }

            // Ease so the cues swell and fade instead of popping.
            Heat = Mathf.MoveTowards(Heat, target, Time.deltaTime * 2.5f);

            if (_fire != null)
            {
                _fire.ProximityBoost = Heat;
            }

            UpdateVignette();
            UpdateHeartbeat();
        }

        private void UpdateVignette()
        {
            if (_vignette == null)
            {
                return;
            }

            var visible = Heat > 0.03f;
            _vignette.SetActive(visible);
            if (!visible)
            {
                return;
            }

            // A slow throb on top of the heat level reads as "danger" rather than a static tint.
            var throb = 0.85f + 0.15f * Mathf.Sin(Time.time * Mathf.Lerp(3f, 9f, Heat));
            var c = VignetteColor;
            c.a = Mathf.Pow(Heat, 1.3f) * 0.75f * throb;
            ProceduralAssets.SetColor(_vignetteMaterial, c);
        }

        private void UpdateHeartbeat()
        {
            if (Heat < 0.2f || Time.time < _nextBeat)
            {
                return;
            }

            var amplitude = 0.15f + 0.55f * Heat;
            foreach (var hand in ControllerInput.Hands)
            {
                ControllerInput.Pulse(hand, amplitude, 0.08f, 0.3f);
            }

            // Faster beat the closer you are: ~1 per 0.9 s at the edge, ~3 per second in the flames.
            _nextBeat = Time.time + Mathf.Lerp(0.9f, 0.33f, Heat);
        }

        private void OnDisable()
        {
            Heat = 0f;
            if (_fire != null)
            {
                _fire.ProximityBoost = 0f;
            }

            if (_vignette != null)
            {
                _vignette.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_vignette != null)
            {
                Destroy(_vignette);
            }
        }

        private static Color Transparent()
        {
            var c = VignetteColor;
            c.a = 0f;
            return c;
        }

        /// <summary>White texture whose alpha is 0 in the middle and rises towards the edges.</summary>
        private static Texture2D MakeVignetteTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Drill_HeatVignette",
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            var half = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - half) / half;
                    var dy = (y - half) / half;
                    var r = Mathf.Sqrt(dx * dx + dy * dy); // 0 centre, 1 edge midpoints, 1.41 corners
                    var a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.05f, r));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
