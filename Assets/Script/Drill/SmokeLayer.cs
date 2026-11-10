// Copyright (c) MR-ExitReady contributors.

using MRExitReady.Common;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// A layer of smoke that fills the room from the ceiling down. It is a translucent box
    /// spanning the room footprint, from the ceiling to <see cref="BottomY"/>, which lowers over
    /// the drill. When the head is inside the layer the view is tinted, and the time is
    /// counted ("standing in smoke"); crouching below the layer clears it.
    /// </summary>
    public class SmokeLayer : MonoBehaviour
    {
        public float BottomY { get; private set; }

        /// <summary>True while the headset is above the bottom of the smoke layer.</summary>
        public bool HeadInSmoke { get; private set; }

        /// <summary>True once the layer is low enough that a standing adult is in it.</summary>
        public bool IsLow => BottomY - _floorY < 1.5f;

        private float _floorY;
        private float _ceilingY;
        private float _minHeightAboveFloor;
        private float _descendSeconds;
        private float _elapsed;
        private Transform _volume;
        private Material _volumeMaterial;
        private GameObject _tint;
        private Material _tintMaterial;
        private ParticleSystem _clouds;
        private Bounds _roomBounds;

        // The box gives the layer a continuous density; the cloud particles on top make it roll.
        private static readonly Color VolumeColor = new(0.1f, 0.1f, 0.11f, 0.22f);
        private static readonly Color TintColor = new(0.08f, 0.08f, 0.08f, 0.55f);

        public static SmokeLayer Create(Transform parent, ProceduralAssets assets, Bounds roomBounds, float floorY,
            float ceilingY, float minHeightAboveFloor, float descendSeconds)
        {
            var go = new GameObject("SmokeLayer");
            go.transform.SetParent(parent, false);

            var layer = go.AddComponent<SmokeLayer>();
            layer._floorY = floorY;
            layer._ceilingY = ceilingY;
            layer._minHeightAboveFloor = minHeightAboveFloor;
            layer._descendSeconds = Mathf.Max(1f, descendSeconds);
            layer.BottomY = ceilingY;
            layer.Build(assets, roomBounds);
            return layer;
        }

        private void Build(ProceduralAssets assets, Bounds roomBounds)
        {
            _roomBounds = roomBounds;
            BuildClouds(assets, roomBounds);

            var volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
            volume.name = "SmokeVolume";
            Destroy(volume.GetComponent<Collider>());
            volume.transform.SetParent(transform, false);
            _volume = volume.transform;

            _volumeMaterial = new Material(assets.SmokeVolume);
            ProceduralAssets.SetColor(_volumeMaterial, VolumeColor);
            var renderer = volume.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _volumeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var c = roomBounds.center;
            _volume.position = new Vector3(c.x, _ceilingY, c.z);
            _volume.localScale = new Vector3(roomBounds.size.x + 0.2f, 0.001f, roomBounds.size.z + 0.2f);

            // Full-view tint shown when the head is in the smoke: a quad just in front of the eyes.
            var head = HeadLocator.Head;
            if (head != null)
            {
                _tint = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _tint.name = "SmokeTint";
                Destroy(_tint.GetComponent<Collider>());
                _tint.transform.SetParent(head, false);
                _tint.transform.localPosition = new Vector3(0f, 0f, 0.12f);
                _tint.transform.localScale = new Vector3(0.6f, 0.4f, 1f);
                _tintMaterial = new Material(assets.SmokeVolume);
                ProceduralAssets.SetColor(_tintMaterial, TintColor);
                var tintRenderer = _tint.GetComponent<MeshRenderer>();
                tintRenderer.sharedMaterial = _tintMaterial;
                tintRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _tint.SetActive(false);
            }
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var target = _floorY + _minHeightAboveFloor;
            var t = Mathf.Clamp01(_elapsed / _descendSeconds);
            // Ease out: smoke fills quickly at first, then slows as it banks down.
            BottomY = Mathf.Lerp(_ceilingY, target, 1f - (1f - t) * (1f - t));

            var thickness = Mathf.Max(0.001f, _ceilingY - BottomY);
            var p = _volume.position;
            _volume.position = new Vector3(p.x, BottomY + thickness * 0.5f, p.z);
            var s = _volume.localScale;
            _volume.localScale = new Vector3(s.x, thickness, s.z);

            UpdateClouds(thickness);

            var head = HeadLocator.Head;
            HeadInSmoke = head != null && HeadLocator.HasTrackedHead && head.position.y > BottomY;
            if (_tint != null)
            {
                _tint.SetActive(HeadInSmoke);
            }
        }

        /// <summary>
        /// Big, slow, dark puffs that churn inside the layer. Their emitter box tracks the layer,
        /// so the clouds visibly roll down from the ceiling as the smoke banks down.
        /// </summary>
        private void BuildClouds(ProceduralAssets assets, Bounds roomBounds)
        {
            _clouds = ProceduralAssets.Particles("SmokeClouds", transform, assets.ParticleAlpha);
            var main = _clouds.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 260;

            var shape = _clouds.shape;
            shape.shapeType = ParticleSystemShapeType.Box;

            var col = _clouds.colorOverLifetime;
            col.enabled = true;
            col.color = ProceduralAssets.Gradient(
                (new Color(0.1f, 0.1f, 0.1f, 0f), 0f),
                (new Color(0.09f, 0.09f, 0.1f, 0.32f), 0.25f),
                (new Color(0.14f, 0.14f, 0.15f, 0.28f), 0.75f),
                (new Color(0.18f, 0.18f, 0.18f, 0f), 1f));

            var rot = _clouds.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            var size = _clouds.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.3f));

            var noise = _clouds.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.1f;

            _clouds.transform.rotation = Quaternion.identity;
            _clouds.Play();
        }

        private void UpdateClouds(float thickness)
        {
            if (_clouds == null)
            {
                return;
            }

            var c = _roomBounds.center;
            _clouds.transform.position = new Vector3(c.x, BottomY + thickness * 0.5f, c.z);
            var shape = _clouds.shape;
            shape.scale = new Vector3(_roomBounds.size.x, Mathf.Max(0.05f, thickness * 0.8f), _roomBounds.size.z);

            // More puffs as the layer gets thicker; a thin layer just under the ceiling stays light.
            var area = Mathf.Max(1f, _roomBounds.size.x * _roomBounds.size.z);
            var emission = _clouds.emission;
            emission.rateOverTime = Mathf.Clamp(area * (0.4f + 2.2f * thickness), 2f, 40f);
        }

        private void OnDisable()
        {
            // Frozen at the end of the drill: don't leave the view tinted over the debrief.
            HeadInSmoke = false;
            if (_tint != null)
            {
                _tint.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_tint != null)
            {
                Destroy(_tint);
            }
        }
    }
}
