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

        private readonly Texture2D _softDot;

        public ProceduralAssets(Material particleAdditive, Material particleAlpha, Material prop)
        {
            var softDot = MakeSoftDot(64);
            _softDot = softDot;

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

        /// <summary>
        /// A lit material with a texture, tinted and tiled (e.g. 2 = the texture repeats twice per
        /// face). Not cached: each caller gets its own so tiling can differ per object.
        /// </summary>
        public Material Textured(Texture texture, Color tint, Vector2 tiling)
        {
            var material = new Material(_propBase) { name = $"Drill_Textured_{texture.name}" };
            SetColor(material, tint);
            SetTexture(material, texture);
            if (material.HasProperty(BaseMapId))
            {
                material.SetTextureScale(BaseMapId, tiling);
            }

            if (material.HasProperty(MainTexId))
            {
                material.SetTextureScale(MainTexId, tiling);
            }

            return material;
        }

        /// <summary>A copy of the soft round particle texture, for glows and decals.</summary>
        public Texture2D SoftDot => _softDot;

        // ── Procedural surface textures (tile seamlessly) ───────────────────────

        private Texture2D _plaster;
        private Texture2D _wood;
        private Texture2D _fabric;

        /// <summary>Light painted plaster: low-contrast mottling.</summary>
        public Texture2D Plaster => _plaster ??= MakeTexture("Drill_Plaster", 128, (u, v) =>
        {
            var n = 0.6f * Tile(u, v, 6f, 11f) + 0.4f * Tile(u, v, 19f, 37f);
            var g = 0.92f + 0.08f * n;
            return new Color(g, g, g * 0.98f, 1f);
        });

        /// <summary>Wooden floor boards: four planks per tile with grain and dark seams.</summary>
        public Texture2D Wood => _wood ??= MakeTexture("Drill_Wood", 256, (u, v) =>
        {
            const int planks = 4;
            var row = Mathf.FloorToInt(v * planks);
            var inRow = v * planks - row;
            var offset = (row * 0.37f) % 1f;  // stagger the plank ends
            var along = Mathf.Repeat(u + offset, 1f);
            var grain = Mathf.Sin((inRow * 18f + Tile(along, inRow, 3f, 5f) * 4f) * Mathf.PI) * 0.5f + 0.5f;
            var tone = 0.75f + 0.12f * Hash(row) + 0.13f * grain;
            var seam = inRow < 0.04f || along < 0.008f ? 0.55f : 1f;
            var c = new Color(0.62f, 0.44f, 0.28f) * tone * seam;
            c.a = 1f;
            return c;
        });

        /// <summary>Woven fabric for upholstery.</summary>
        public Texture2D Fabric => _fabric ??= MakeTexture("Drill_Fabric", 64, (u, v) =>
        {
            var weave = 0.5f + 0.25f * Mathf.Sin(u * Mathf.PI * 32f) * Mathf.Sin(v * Mathf.PI * 32f);
            var g = 0.85f + 0.15f * weave + 0.05f * Tile(u, v, 8f, 13f);
            return new Color(g, g, g, 1f);
        });

        private static Texture2D MakeTexture(string name, int size, System.Func<float, float, Color> sample)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2,
            };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    pixels[y * size + x] = sample((float)x / size, (float)y / size);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Tileable value noise in 0..1: sums of sines at integer frequencies.</summary>
        private static float Tile(float u, float v, float f1, float f2)
        {
            var a = Mathf.Sin(2f * Mathf.PI * (u * Mathf.Round(f1 * 0.5f) + v * Mathf.Round(f1 * 0.3f)) + 1.3f);
            var b = Mathf.Sin(2f * Mathf.PI * (u * Mathf.Round(f2 * 0.2f) - v * Mathf.Round(f2 * 0.5f)) + 4.1f);
            var c = Mathf.Sin(2f * Mathf.PI * (u * Mathf.Round(f2 * 0.6f) + v * Mathf.Round(f1 * 0.7f)) + 2.7f);
            return (a + b + c) / 6f + 0.5f;
        }

        private static float Hash(int i) => Mathf.Repeat(Mathf.Sin(i * 12.9898f) * 43758.547f, 1f);

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

        public static void SetTexture(Material material, Texture texture)
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
