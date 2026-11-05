// Copyright (c) MR-ExitReady contributors.

using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Rendering;

namespace MRExitReady.Debugging
{
    /// <summary>
    /// Re-skins the meshes that <see cref="EffectMesh"/> generates for MRUK anchors with a
    /// translucent, per-semantic-label colour, so walls / floor / ceiling / doors / windows /
    /// furniture are each identifiable at a glance while passthrough stays visible behind them.
    ///
    /// EffectMesh can only apply a single material to every anchor, which is why the colouring
    /// happens here instead. The component polls rather than relying purely on MRUK's
    /// scene-loaded callback, because callback order between this component and EffectMesh is
    /// not guaranteed and rooms can also appear later (Space Setup re-run, walking into a room
    /// that was scanned separately).
    /// </summary>
    public class SceneLabelColorizer : MonoBehaviour
    {
        [Tooltip("Material to clone per label. Must use a URP shader; render state is forced to transparent in code.")]
        [SerializeField] private Material sourceMaterial;

        [Tooltip("Opacity of the debug meshes. 0 = invisible, 1 = opaque (hides passthrough).")]
        [Range(0f, 1f)]
        [SerializeField] private float opacity = 0.3f;

        [Tooltip("Seconds between scans for anchors that have not been coloured yet.")]
        [SerializeField] private float scanInterval = 0.5f;

        [Tooltip("Toggle all debug meshes on/off by clicking the right thumbstick. A/X is reserved for menu clicks by OVRInputModule.")]
        [SerializeField] private bool allowVisibilityToggle = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Dictionary<MRUKAnchor.SceneLabels, Material> _materials = new();
        private readonly HashSet<Renderer> _seen = new();
        private readonly List<Renderer> _managed = new();

        private float _nextScan;
        private bool _visible = true;

        /// <summary>Colour used for each semantic label. Alpha is replaced by <c>opacity</c>.</summary>
        private static readonly (MRUKAnchor.SceneLabels Label, Color Color)[] Palette =
        {
            (MRUKAnchor.SceneLabels.WALL_FACE, new Color(0.55f, 0.58f, 0.62f)),
            (MRUKAnchor.SceneLabels.INNER_WALL_FACE, new Color(0.40f, 0.44f, 0.50f)),
            (MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE, new Color(1.00f, 0.00f, 1.00f)),
            (MRUKAnchor.SceneLabels.FLOOR, new Color(0.20f, 0.45f, 0.85f)),
            (MRUKAnchor.SceneLabels.CEILING, new Color(0.65f, 0.80f, 0.95f)),
            (MRUKAnchor.SceneLabels.DOOR_FRAME, new Color(1.00f, 0.45f, 0.10f)),
            (MRUKAnchor.SceneLabels.WINDOW_FRAME, new Color(0.10f, 0.85f, 0.90f)),
            (MRUKAnchor.SceneLabels.TABLE, new Color(0.15f, 0.65f, 1.00f)),
            (MRUKAnchor.SceneLabels.COUCH, new Color(0.90f, 0.15f, 0.25f)),
            (MRUKAnchor.SceneLabels.BED, new Color(1.00f, 0.40f, 0.70f)),
            (MRUKAnchor.SceneLabels.STORAGE, new Color(0.65f, 0.35f, 0.90f)),
            (MRUKAnchor.SceneLabels.SCREEN, new Color(0.95f, 0.90f, 0.20f)),
            (MRUKAnchor.SceneLabels.LAMP, new Color(1.00f, 0.75f, 0.30f)),
            (MRUKAnchor.SceneLabels.PLANT, new Color(0.25f, 0.80f, 0.35f)),
            (MRUKAnchor.SceneLabels.WALL_ART, new Color(0.35f, 0.35f, 0.40f)),
            (MRUKAnchor.SceneLabels.GLOBAL_MESH, new Color(0.30f, 0.90f, 0.60f)),
            (MRUKAnchor.SceneLabels.OTHER, new Color(0.85f, 0.85f, 0.85f)),
        };

        private void Start()
        {
            if (sourceMaterial == null)
            {
                Debug.LogError($"[{nameof(SceneLabelColorizer)}] No source material assigned; debug meshes will keep the EffectMesh material.");
                enabled = false;
            }
        }

        private void Update()
        {
            if (allowVisibilityToggle && OVRInput.GetDown(OVRInput.RawButton.RThumbstick))
            {
                SetVisible(!_visible);
            }

            if (Time.unscaledTime < _nextScan)
            {
                return;
            }

            _nextScan = Time.unscaledTime + scanInterval;
            Scan();
        }

        /// <summary>Shows or hides every mesh this component has taken over.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            for (var i = _managed.Count - 1; i >= 0; i--)
            {
                if (_managed[i] == null)
                {
                    _managed.RemoveAt(i);
                    continue;
                }

                _managed[i].enabled = visible;
            }
        }

        private void Scan()
        {
            var mruk = MRUK.Instance;
            if (mruk == null)
            {
                return;
            }

            var rooms = mruk.Rooms;
            for (var r = 0; r < rooms.Count; r++)
            {
                var room = rooms[r];
                if (room == null)
                {
                    continue;
                }

                var anchors = room.Anchors;
                for (var a = 0; a < anchors.Count; a++)
                {
                    var anchor = anchors[a];
                    if (anchor == null)
                    {
                        continue;
                    }

                    // EffectMesh parents the generated mesh under the anchor, so everything we
                    // need to re-skin is in the anchor's own subtree.
                    var renderers = anchor.GetComponentsInChildren<MeshRenderer>(true);
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        Apply(renderers[i], anchor.Label);
                    }
                }
            }
        }

        private void Apply(Renderer target, MRUKAnchor.SceneLabels label)
        {
            if (target == null || !_seen.Add(target))
            {
                return;
            }

            target.sharedMaterial = MaterialFor(label);
            target.shadowCastingMode = ShadowCastingMode.Off;
            target.receiveShadows = false;
            target.enabled = _visible;
            _managed.Add(target);
        }

        private Material MaterialFor(MRUKAnchor.SceneLabels label)
        {
            if (_materials.TryGetValue(label, out var cached))
            {
                return cached;
            }

            var color = ColorFor(label);
            color.a = opacity;

            var material = new Material(sourceMaterial)
            {
                name = $"MRUKDebug_{label}",
                renderQueue = (int)RenderQueue.Transparent,
            };

            // URP reads its blend state from these properties, so a cloned material has to be
            // switched to transparent explicitly - setting the colour alpha alone does nothing.
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);                                   // 0 opaque, 1 transparent
            material.SetFloat("_Blend", 0f);                                     // alpha blend
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);                     // room surfaces are single-sided
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }

            _materials[label] = material;
            return material;
        }

        private static Color ColorFor(MRUKAnchor.SceneLabels label)
        {
            for (var i = 0; i < Palette.Length; i++)
            {
                if ((label & Palette[i].Label) != 0)
                {
                    return Palette[i].Color;
                }
            }

            return Color.white;
        }
    }
}
