// Copyright (c) MR-ExitReady contributors.

using System;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// A procedural fire: flames, embers and a smoke plume from particle systems, a flickering
    /// orange light and a crackle loop. <see cref="Intensity"/> grows over time and drops when the
    /// extinguisher hits it; at zero the fire is out.
    /// </summary>
    public class FireHazard : MonoBehaviour
    {
        public float Intensity { get; private set; }
        public bool IsOut { get; private set; }

        /// <summary>Intensity at the moment the extinguisher first hit (used for scoring).</summary>
        public float IntensityAtFirstSpray { get; private set; } = -1f;

        public event Action Extinguished;

        private float _radius;
        private float _growthPerSecond;
        private ParticleSystem _flames;
        private ParticleSystem _embers;
        private ParticleSystem _smoke;
        private Light _light;
        private AudioSource _audio;
        private float _seed;

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

            // Two incommensurate sines plus noise read as flicker, not a pulse.
            var t = Time.time + _seed;
            var flicker = 0.75f + 0.15f * Mathf.Sin(t * 13f) + 0.1f * Mathf.Sin(t * 23.7f) +
                          0.1f * (Mathf.PerlinNoise(t * 4f, _seed) - 0.5f);
            _light.intensity = (0.8f + 2.2f * Intensity) * flicker;
        }

        private void PutOut()
        {
            IsOut = true;
            Intensity = 0f;
            _flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            // Leave a thin smoulder for a few seconds.
            var emission = _smoke.emission;
            emission.rateOverTime = 4f;
            Destroy(_smoke.gameObject, 8f);

            _light.enabled = false;
            _audio.Stop();
            Extinguished?.Invoke();
        }

        private void ApplyIntensity()
        {
            var i = Mathf.Max(0.05f, Intensity);
            var width = _radius * (0.6f + 0.8f * i);

            var flameShape = _flames.shape;
            flameShape.radius = width;
            var flameMain = _flames.main;
            flameMain.startSize = new ParticleSystem.MinMaxCurve(0.15f + 0.25f * i, 0.3f + 0.45f * i);
            flameMain.startSpeed = new ParticleSystem.MinMaxCurve(0.4f + 0.6f * i, 0.8f + 1.0f * i);
            var flameEmission = _flames.emission;
            flameEmission.rateOverTime = 25f + 90f * i;

            var emberEmission = _embers.emission;
            emberEmission.rateOverTime = 3f + 15f * i;
            var emberShape = _embers.shape;
            emberShape.radius = width;

            var smokeShape = _smoke.shape;
            smokeShape.radius = width * 0.8f;
            var smokeEmission = _smoke.emission;
            smokeEmission.rateOverTime = 4f + 18f * i;
            var smokeMain = _smoke.main;
            smokeMain.startSize = new ParticleSystem.MinMaxCurve(0.4f + 0.5f * i, 0.8f + 0.9f * i);

            _light.range = 2f + 3f * i;
            _audio.volume = 0.25f + 0.55f * i;
        }

        private void Build(ProceduralAssets assets)
        {
            // Flames: short-lived, rising, yellow -> orange -> red -> transparent.
            _flames = ProceduralAssets.Particles("Flames", transform, assets.ParticleAdditive);
            {
                var main = _flames.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.gravityModifier = -0.15f;

                var shape = _flames.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.rotation = new Vector3(-90f, 0f, 0f); // circle lies flat, emits up

                var col = _flames.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 0.95f, 0.6f, 0.0f), 0f),
                    (new Color(1f, 0.75f, 0.2f, 0.9f), 0.15f),
                    (new Color(1f, 0.35f, 0.05f, 0.7f), 0.55f),
                    (new Color(0.6f, 0.1f, 0.02f, 0f), 1f));

                var size = _flames.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

                var noise = _flames.noise;
                noise.enabled = true;
                noise.strength = 0.4f;
                noise.frequency = 1.5f;
                noise.scrollSpeed = 1f;
            }

            // Embers: tiny bright sparks that drift higher.
            _embers = ProceduralAssets.Particles("Embers", transform, assets.ParticleAdditive);
            {
                var main = _embers.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
                main.startColor = new Color(1f, 0.6f, 0.2f, 1f);
                main.gravityModifier = -0.05f;

                var shape = _embers.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.rotation = new Vector3(-90f, 0f, 0f);

                var noise = _embers.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.8f;

                var col = _embers.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 0.8f, 0.3f, 1f), 0f),
                    (new Color(1f, 0.3f, 0.05f, 0f), 1f));
            }

            // Smoke plume: slow, large, dark, rising to the ceiling.
            _smoke = ProceduralAssets.Particles("Smoke", transform, assets.ParticleAlpha);
            {
                _smoke.transform.localPosition = Vector3.up * 0.4f;
                var main = _smoke.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles = 300;

                var shape = _smoke.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.rotation = new Vector3(-90f, 0f, 0f);

                var col = _smoke.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(0.15f, 0.15f, 0.15f, 0f), 0f),
                    (new Color(0.12f, 0.12f, 0.12f, 0.55f), 0.2f),
                    (new Color(0.2f, 0.2f, 0.2f, 0.35f), 0.7f),
                    (new Color(0.25f, 0.25f, 0.25f, 0f), 1f));

                var size = _smoke.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));

                var noise = _smoke.noise;
                noise.enabled = true;
                noise.strength = 0.25f;
                noise.frequency = 0.4f;
            }

            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.4f;
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.55f, 0.2f);
            _light.shadows = LightShadows.None;

            _audio = ProceduralAssets.LoopingSource(gameObject, assets.Crackle, 0.5f, 1f, 10f);

            _flames.Play();
            _embers.Play();
            _smoke.Play();
        }
    }
}
