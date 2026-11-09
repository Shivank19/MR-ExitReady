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

        private static readonly Color VolumeColor = new(0.12f, 0.12f, 0.13f, 0.38f);
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

            var head = HeadLocator.Head;
            HeadInSmoke = head != null && HeadLocator.HasTrackedHead && head.position.y > BottomY;
            if (_tint != null)
            {
                _tint.SetActive(HeadInSmoke);
            }
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
