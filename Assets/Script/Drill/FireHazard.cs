// Copyright (c) MR-ExitReady contributors.

using System;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// A procedural fire built from layered particle systems: a white-hot core, the main flame
    /// body, tall stretched flame tongues, drifting embers, bursts of sparks, a soft glow halo
    /// and a dark smoke plume, plus a scorch mark that spreads on the surface and two
    /// independently flickering lights. <see cref="Intensity"/> grows over time and drops when the
    /// extinguisher hits it; at zero the fire is out.
    /// </summary>
    public class FireHazard : MonoBehaviour
    {
        public float Intensity { get; private set; }
        public bool IsOut { get; private set; }

        /// <summary>Intensity at the moment the extinguisher first hit (used for scoring).</summary>
        public float IntensityAtFirstSpray { get; private set; } = -1f;

        public event Action Extinguished;

        /// <summary>0-1, set by <see cref="HeatCues"/>: makes the crackle louder when the user is close.</summary>
        public float ProximityBoost { get; set; }

        private float _radius;
        private float _growthPerSecond;
        private ParticleSystem _core;
        private ParticleSystem _flames;
        private ParticleSystem _tongues;
        private ParticleSystem _embers;
        private ParticleSystem _sparks;
        private ParticleSystem _glow;
        private ParticleSystem _smoke;
        private Light _light;
        private Light _fill;
        private AudioSource _audio;
        private Transform _scorch;
        private float _scorchRadius;
        private float _seed;
        private float _nextBurst;

        public static FireHazard Create(Transform parent, ProceduralAssets assets, Vector3 position, float radius,
            float startIntensity, float growthPerSecond)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var fire = go.AddComponent<FireHazard>();
            fire._radius = Mathf.Clamp(radius, 0.15f, 0.6f);
            fire._growthPerSecond = growthPerSecond;
            fire.Intensity = Mathf.Clamp01(startIntensity);
            fire._seed = UnityEngine.Random.value * 100f;
            fire.Build(assets);
            fire.ApplyIntensity();
            return fire;
        }

        /// <summary>Approximate centre of the flames, for aiming.</summary>
        public Vector3 Center => transform.position + Vector3.up * (0.25f + 0.35f * Intensity);

        /// <summary>Called by the extinguisher every frame its spray hits the fire.</summary>
        public void ApplyExtinguisher(float amount)
        {
            if (IsOut)
            {
                return;
            }

            if (IntensityAtFirstSpray < 0f)
            {
                IntensityAtFirstSpray = Intensity;
            }

            Intensity = Mathf.Max(0f, Intensity - amount);
            if (Intensity <= 0.02f)
            {
                PutOut();
            }
        }

        private void Update()
        {
            if (IsOut)
            {
                return;
            }

            Intensity = Mathf.Min(1f, Intensity + _growthPerSecond * Time.deltaTime);
            ApplyIntensity();

            // Two incommensurate sines plus noise read as flicker, not a pulse. The fill light
            // flickers on its own phase so the light on the walls shifts and breathes.
            var t = Time.time + _seed;
            var flicker = 0.75f + 0.15f * Mathf.Sin(t * 13f) + 0.1f * Mathf.Sin(t * 23.7f) +
                          0.1f * (Mathf.PerlinNoise(t * 4f, _seed) - 0.5f);
            _light.intensity = (1.0f + 2.6f * Intensity) * flicker;
            var flicker2 = 0.7f + 0.3f * Mathf.PerlinNoise(t * 2.3f, _seed + 7f);
            _fill.intensity = (0.4f + 1.4f * Intensity) * flicker2;
            _light.transform.localPosition = new Vector3(
                0.06f * Mathf.Sin(t * 5.1f), 0.35f + 0.25f * Intensity, 0.06f * Mathf.Cos(t * 4.3f));

            // Spark bursts: a spray of sparks every second or so, more often when big.
            if (Time.time >= _nextBurst)
            {
                _sparks.Emit(Mathf.RoundToInt(6 + 22 * Intensity * UnityEngine.Random.value));
                _nextBurst = Time.time + UnityEngine.Random.Range(0.4f, 1.6f) * (1.4f - Intensity);
            }

            // The scorch mark spreads with the fire and never shrinks.
            _scorchRadius = Mathf.Max(_scorchRadius, _radius * (1.1f + 1.2f * Intensity));
            _scorch.localScale = Vector3.one * (_scorchRadius * 2f);
        }

        private void PutOut()
        {
            IsOut = true;
            Intensity = 0f;
            _core.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _tongues.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _glow.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            // A last spray of sparks, then a thin grey smoulder for a few seconds.
            _sparks.Emit(25);
            var emission = _smoke.emission;
            emission.rateOverTime = 6f;
            var main = _smoke.main;
            main.startColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            Destroy(_smoke.gameObject, 8f);

            _light.enabled = false;
            _fill.enabled = false;
            _audio.Stop();
            Extinguished?.Invoke();
        }

        private void ApplyIntensity()
        {
            var i = Mathf.Max(0.05f, Intensity);
            var width = _radius * (0.6f + 0.8f * i);

            SetShape(_core, width * 0.45f);
            var coreMain = _core.main;
            coreMain.startSize = new ParticleSystem.MinMaxCurve(0.12f + 0.15f * i, 0.2f + 0.25f * i);
            SetRate(_core, 20f + 50f * i);

            SetShape(_flames, width);
            var flameMain = _flames.main;
            flameMain.startSize = new ParticleSystem.MinMaxCurve(0.18f + 0.3f * i, 0.35f + 0.5f * i);
            flameMain.startSpeed = new ParticleSystem.MinMaxCurve(0.4f + 0.6f * i, 0.8f + 1.1f * i);
            SetRate(_flames, 30f + 100f * i);

            SetShape(_tongues, width * 0.85f);
            var tongueMain = _tongues.main;
            tongueMain.startSpeed = new ParticleSystem.MinMaxCurve(0.9f + 1.0f * i, 1.4f + 1.6f * i);
            tongueMain.startSize = new ParticleSystem.MinMaxCurve(0.08f + 0.1f * i, 0.14f + 0.16f * i);
            SetRate(_tongues, 8f + 40f * i);

            SetShape(_embers, width);
            SetRate(_embers, 4f + 18f * i);
            SetShape(_sparks, width * 0.7f);

            var glowMain = _glow.main;
            glowMain.startSize = new ParticleSystem.MinMaxCurve(width * 3.2f + 0.4f, width * 4f + 0.6f);
            _glow.transform.localPosition = Vector3.up * (0.15f + 0.3f * i);

            SetShape(_smoke, width * 0.8f);
            SetRate(_smoke, 5f + 24f * i);
            var smokeMain = _smoke.main;
            smokeMain.startSize = new ParticleSystem.MinMaxCurve(0.45f + 0.6f * i, 0.9f + 1.1f * i);

            _light.range = 2.5f + 4f * i;
            _fill.range = 4f + 4f * i;
            _audio.volume = Mathf.Min(1f, (0.25f + 0.55f * i) * (1f + 0.8f * ProximityBoost));
            _audio.pitch = 1f + 0.12f * ProximityBoost;
        }

        private static void SetShape(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.radius = Mathf.Max(0.01f, radius);
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            var emission = ps.emission;
            emission.rateOverTime = rate;
        }

        private void Build(ProceduralAssets assets)
        {
            // Core: bright, low, short-lived: the white-yellow heart of the fire.
            _core = Layer("Core", assets.ParticleAdditive, 0.25f, 0.45f, -0.25f);
            {
                var main = _core.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                var col = _core.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 1f, 0.9f, 0f), 0f),
                    (new Color(1f, 0.95f, 0.7f, 1f), 0.2f),
                    (new Color(1f, 0.7f, 0.25f, 0.6f), 0.7f),
                    (new Color(1f, 0.5f, 0.1f, 0f), 1f));
            }

            // Main flame body: yellow -> orange -> deep red, turbulent.
            _flames = Layer("Flames", assets.ParticleAdditive, 0.5f, 0.95f, -0.18f);
            {
                var col = _flames.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 0.95f, 0.6f, 0f), 0f),
                    (new Color(1f, 0.72f, 0.2f, 0.9f), 0.15f),
                    (new Color(1f, 0.32f, 0.05f, 0.7f), 0.55f),
                    (new Color(0.55f, 0.08f, 0.02f, 0f), 1f));
                var size = _flames.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));
                var noise = _flames.noise;
                noise.enabled = true;
                noise.strength = 0.45f;
                noise.frequency = 1.6f;
                noise.scrollSpeed = 1.2f;
            }

            // Tongues: fast, velocity-stretched streaks that lick upwards above the body.
            _tongues = Layer("Tongues", assets.ParticleAdditive, 0.35f, 0.6f, -0.3f);
            {
                var renderer = _tongues.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.18f;
                renderer.lengthScale = 2.2f;
                var col = _tongues.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 0.85f, 0.4f, 0f), 0f),
                    (new Color(1f, 0.55f, 0.12f, 0.8f), 0.25f),
                    (new Color(0.9f, 0.2f, 0.03f, 0f), 1f));
                var noise = _tongues.noise;
                noise.enabled = true;
                noise.strength = 0.8f;
                noise.frequency = 2.2f;
                noise.scrollSpeed = 1.5f;
            }

            // Embers: tiny bright motes drifting up and away.
            _embers = Layer("Embers", assets.ParticleAdditive, 1.2f, 2.4f, -0.05f);
            {
                var main = _embers.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
                main.startColor = new Color(1f, 0.6f, 0.2f, 1f);
                var noise = _embers.noise;
                noise.enabled = true;
                noise.strength = 0.7f;
                noise.frequency = 0.8f;
                var col = _embers.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 0.85f, 0.4f, 1f), 0f),
                    (new Color(1f, 0.35f, 0.05f, 0f), 1f));
            }

            // Sparks: emitted in bursts from Update; fast, stretched, falling back down.
            _sparks = Layer("Sparks", assets.ParticleAdditive, 0.5f, 1.1f, 0.6f);
            {
                var main = _sparks.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.02f);
                main.startColor = new Color(1f, 0.8f, 0.4f, 1f);
                var shape = _sparks.shape;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var emission = _sparks.emission;
                emission.rateOverTime = 0f;
                var renderer = _sparks.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.06f;
                renderer.lengthScale = 1f;
                var col = _sparks.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 0.95f, 0.7f, 1f), 0f),
                    (new Color(1f, 0.4f, 0.05f, 0f), 1f));
            }

            // Glow: a few big, faint, slow billboards that make the fire bloom into the air.
            _glow = Layer("Glow", assets.ParticleAdditive, 0.8f, 1.2f, 0f);
            {
                var main = _glow.main;
                main.startSpeed = 0f;
                main.maxParticles = 12;
                var shape = _glow.shape;
                shape.radius = 0.02f;
                var emission = _glow.emission;
                emission.rateOverTime = 6f;
                var col = _glow.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 0.5f, 0.1f, 0f), 0f),
                    (new Color(1f, 0.5f, 0.1f, 0.22f), 0.4f),
                    (new Color(1f, 0.4f, 0.05f, 0f), 1f));
            }

            // Smoke plume: slow, large, near-black, rising to the ceiling.
            _smoke = Layer("Smoke", assets.ParticleAlpha, 3.5f, 5f, -0.02f);
            {
                _smoke.transform.localPosition = Vector3.up * 0.45f;
                var main = _smoke.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
                main.maxParticles = 350;
                var col = _smoke.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(0.1f, 0.09f, 0.08f, 0f), 0f),
                    (new Color(0.08f, 0.08f, 0.08f, 0.6f), 0.2f),
                    (new Color(0.15f, 0.15f, 0.15f, 0.4f), 0.7f),
                    (new Color(0.22f, 0.22f, 0.22f, 0f), 1f));
                var size = _smoke.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.6f));
                var rot = _smoke.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                var noise = _smoke.noise;
                noise.enabled = true;
                noise.strength = 0.3f;
                noise.frequency = 0.4f;
            }

            // Scorch mark: a dark soft disc on the surface under the fire.
            var scorch = GameObject.CreatePrimitive(PrimitiveType.Quad);
            scorch.name = "Scorch";
            Destroy(scorch.GetComponent<Collider>());
            _scorch = scorch.transform;
            _scorch.SetParent(transform, false);
            _scorch.localPosition = Vector3.up * 0.004f;
            _scorch.localRotation = Quaternion.Euler(90f, 0f, 0f); // lies flat, visible from above
            var scorchMaterial = new Material(assets.ParticleAlpha) { name = "Drill_Scorch" };
            ProceduralAssets.SetColor(scorchMaterial, new Color(0.03f, 0.02f, 0.015f, 0.85f));
            var scorchRenderer = scorch.GetComponent<MeshRenderer>();
            scorchRenderer.sharedMaterial = scorchMaterial;
            scorchRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _scorchRadius = _radius;

            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(transform, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.52f, 0.18f);
            _light.shadows = LightShadows.None;

            var fillGo = new GameObject("FireFill");
            fillGo.transform.SetParent(transform, false);
            fillGo.transform.localPosition = Vector3.up * 1.2f;
            _fill = fillGo.AddComponent<Light>();
            _fill.type = LightType.Point;
            _fill.color = new Color(1f, 0.35f, 0.1f);
            _fill.shadows = LightShadows.None;

            _audio = ProceduralAssets.LoopingSource(gameObject, assets.Crackle, 0.5f, 1f, 10f);

            _core.Play();
            _flames.Play();
            _tongues.Play();
            _embers.Play();
            _sparks.Play();
            _glow.Play();
            _smoke.Play();
        }

        /// <summary>A flat circular emitter at the fire's base that emits upwards.</summary>
        private ParticleSystem Layer(string name, Material material, float minLife, float maxLife, float gravity)
        {
            var ps = ProceduralAssets.Particles(name, transform, material);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.rotation = new Vector3(-90f, 0f, 0f); // circle lies flat, emits up
            return ps;
        }
    }
}
