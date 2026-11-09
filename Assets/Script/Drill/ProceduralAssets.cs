// Copyright (c) MR-ExitReady contributors.

using System.Collections.Generic;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// Placeholder art for v1: coloured primitives, soft-particle materials and audio clips
    /// generated in code, so the drill needs no imported models or sound files. Swap any of
    /// these for real assets later without touching the gameplay scripts.
    /// </summary>
    public class ProceduralAssets
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private readonly Material _propBase;
        private readonly Dictionary<Color, Material> _props = new();

        private AudioClip _siren;
        private AudioClip _crackle;
        private AudioClip _hiss;

        /// <summary>Additive soft particle material (flames, embers, sparks).</summary>
        public Material ParticleAdditive { get; }

        /// <summary>Alpha-blended soft particle material (smoke, extinguisher spray).</summary>
        public Material ParticleAlpha { get; }

        /// <summary>Alpha-blended, double-sided material for the smoke layer volume.</summary>
        public Material SmokeVolume { get; }

        public ProceduralAssets(Material particleAdditive, Material particleAlpha, Material prop)
        {
            var softDot = MakeSoftDot(64);

            ParticleAdditive = new Material(particleAdditive) { name = "Drill_ParticleAdditive" };
            SetTexture(ParticleAdditive, softDot);

            ParticleAlpha = new Material(particleAlpha) { name = "Drill_ParticleAlpha" };
            SetTexture(ParticleAlpha, softDot);

            // Same shader as the smoke particles, but with a plain white texture so the whole
            // box is evenly tinted.
            SmokeVolume = new Material(particleAlpha) { name = "Drill_SmokeVolume" };
            SetTexture(SmokeVolume, Texture2D.whiteTexture);

            _propBase = prop;
        }

        // ── Materials ───────────────────────────────────────────────────────────

        public Material Prop(Color color)
        {
            if (_props.TryGetValue(color, out var cached))
            {
                return cached;
            }

            var material = new Material(_propBase) { name = $"Drill_Prop_{ColorUtility.ToHtmlStringRGB(color)}" };
            SetColor(material, color);
            _props[color] = material;
            return material;
        }

        public static void SetColor(Material material, Color color)
        {
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }
        }

        private static void SetTexture(Material material, Texture texture)
        {
            if (material.HasProperty(BaseMapId))
            {
                material.SetTexture(BaseMapId, texture);
            }

            if (material.HasProperty(MainTexId))
            {
                material.SetTexture(MainTexId, texture);
            }
        }

        // ── Primitives ──────────────────────────────────────────────────────────

        /// <summary>Creates a coloured primitive with its collider removed.</summary>
        public GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Color color, Quaternion? localRotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation ?? Quaternion.identity;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Prop(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>World-space 3D text, e.g. a sign on a prop.</summary>
        public static TMPro.TextMeshPro Label(string name, Transform parent, Vector3 localPosition, string text,
            float fontSize, Color color)
        {
            var go = new GameObject(name, typeof(TMPro.TextMeshPro));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var tmp = go.GetComponent<TMPro.TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(1f, 0.3f);
            return tmp;
        }

        // ── Particles ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a stopped particle system with world simulation space and the given material.
        /// Callers configure the modules and then call <c>Play()</c>.
        /// </summary>
        public static ParticleSystem Particles(string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 500;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        public static Gradient Gradient(params (Color color, float time)[] keys)
        {
            var colorKeys = new GradientColorKey[keys.Length];
            var alphaKeys = new GradientAlphaKey[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].time);
                alphaKeys[i] = new GradientAlphaKey(keys[i].color.a, keys[i].time);
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        private static Texture2D MakeSoftDot(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Drill_SoftDot",
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
                    var d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    var a = Mathf.SmoothStep(1f, 0f, d);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // ── Audio ───────────────────────────────────────────────────────────────

        private const int SampleRate = 44100;

        /// <summary>Rising/falling two-tone siren, ~1.2s loop.</summary>
        public AudioClip Siren => _siren ??= MakeSiren("Drill_Siren", 1.2f);

        /// <summary>Crackling fire: brown noise with random pops, ~2s loop.</summary>
        public AudioClip Crackle => _crackle ??= MakeNoise("Drill_Crackle", 2f, crackle: true);

        /// <summary>Extinguisher hiss: bright white noise, ~1s loop.</summary>
        public AudioClip Hiss => _hiss ??= MakeNoise("Drill_Hiss", 1f, crackle: false);

        private static AudioClip MakeSiren(string name, float seconds)
        {
            // Frequency sweeps 700 -> 1300 Hz and back; phase is integrated so there are no clicks.
            var count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            var phase = 0.0;
            for (var i = 0; i < count; i++)
            {
                var t = (float)i / count;                       // 0..1 over the loop
                var sweep = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f); // 0 -> 1 -> 0
                var freq = 700.0 + 600.0 * sweep;
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                // Square-ish wave (sine + third harmonic) reads as an alarm, not a tone.
                var s = Mathf.Sin((float)phase) + 0.3f * Mathf.Sin((float)(phase * 3.0));
                data[i] = 0.35f * s;
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeNoise(string name, float seconds, bool crackle)
        {
            var count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            var rng = new System.Random(crackle ? 1234 : 5678);
            var brown = 0f;
            var pop = 0f;
            for (var i = 0; i < count; i++)
            {
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                if (crackle)
                {
                    brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.995f;
                    if (rng.NextDouble() < 0.0006)
                    {
                        pop = 0.9f;
                    }

                    pop *= 0.985f;
                    data[i] = 0.6f * brown + pop * white;
                }
                else
                {
                    data[i] = 0.25f * white;
                }
            }

            // Fade the ends so the loop point doesn't click.
            var fade = Mathf.Min(512, count / 4);
            for (var i = 0; i < fade; i++)
            {
                var k = (float)i / fade;
                data[i] *= k;
                data[count - 1 - i] *= k;
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Adds a looping 3D audio source playing <paramref name="clip"/>.</summary>
        public static AudioSource LoopingSource(GameObject host, AudioClip clip, float volume, float spatialBlend,
            float maxDistance = 12f, bool playNow = true)
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.5f;
            source.maxDistance = maxDistance;
            source.playOnAwake = false;
            if (playNow)
            {
                source.Play();
            }

            return source;
        }
    }
}
