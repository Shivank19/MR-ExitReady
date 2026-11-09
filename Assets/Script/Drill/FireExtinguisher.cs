// Copyright (c) MR-ExitReady contributors.

using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// A portable extinguisher. Hold grip near it to pick it up, keep holding grip to carry it,
    /// and hold the trigger to spray along the controller's forward direction. The spray
    /// knocks down a <see cref="FireHazard"/> inside its cone. Limited charge.
    ///
    /// While held it is parented to the controller, outside the scenario root, so the scenario
    /// must destroy it explicitly on teardown.
    /// </summary>
    public class FireExtinguisher : MonoBehaviour
    {
        private const float PickupDistance = 0.3f;
        private const float SprayRange = 3.5f;
        private const float SprayHalfAngle = 14f;
        private const float KnockdownPerSecond = 0.16f;

        public float Charge { get; private set; }
        public bool IsHeld => _holder != OVRInput.Controller.None;
        public bool IsSpraying { get; private set; }

        private float _capacitySeconds;
        private OVRInput.Controller _holder = OVRInput.Controller.None;
        private FireHazard _fire;
        private DrillScorer _scorer;
        private ParticleSystem _spray;
        private AudioSource _hiss;
        private Transform _visual;
        private Transform _nozzle;
        private Transform _originalParent;
        private float _floorY;
        private bool _reportedEmpty;

        public static FireExtinguisher Create(Transform parent, ProceduralAssets assets, Vector3 floorPosition,
            float floorY, float capacitySeconds, FireHazard fire, DrillScorer scorer)
        {
            var go = new GameObject("FireExtinguisher");
            go.transform.SetParent(parent, false);
            go.transform.position = floorPosition;

            var ext = go.AddComponent<FireExtinguisher>();
            ext._capacitySeconds = capacitySeconds;
            ext.Charge = capacitySeconds;
            ext._fire = fire;
            ext._scorer = scorer;
            ext._floorY = floorY;
            ext._originalParent = parent;
            ext.Build(assets);
            return ext;
        }

        private void Build(ProceduralAssets assets)
        {
            var red = new Color(0.8f, 0.05f, 0.05f);
            var black = new Color(0.08f, 0.08f, 0.08f);

            // _visual holds the bottle; on the floor it stands upright, in hand it's offset so
            // the controller grips the neck and the nozzle points along the controller forward.
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);

            // Unity's cylinder is 2 units tall, so a y-scale of 0.22 makes it 0.44 m.
            assets.Primitive(PrimitiveType.Cylinder, "Bottle", _visual, new Vector3(0f, 0.22f, 0f),
                new Vector3(0.14f, 0.22f, 0.14f), red);
            assets.Primitive(PrimitiveType.Cylinder, "Valve", _visual, new Vector3(0f, 0.47f, 0f),
                new Vector3(0.05f, 0.03f, 0.05f), black);
            assets.Primitive(PrimitiveType.Cube, "Lever", _visual, new Vector3(0f, 0.51f, 0.04f),
                new Vector3(0.025f, 0.012f, 0.11f), black);
            assets.Primitive(PrimitiveType.Cube, "Label", _visual, new Vector3(0f, 0.25f, 0.071f),
                new Vector3(0.09f, 0.12f, 0.002f), new Color(0.95f, 0.95f, 0.95f));

            assets.Primitive(PrimitiveType.Cylinder, "Nozzle", _visual, new Vector3(0f, 0.48f, 0.12f),
                new Vector3(0.025f, 0.06f, 0.025f), black, Quaternion.Euler(90f, 0f, 0f));
            _nozzle = new GameObject("NozzleTip").transform;
            _nozzle.SetParent(_visual, false);
            _nozzle.localPosition = new Vector3(0f, 0.48f, 0.18f);
            _nozzle.localRotation = Quaternion.identity;

            _spray = ProceduralAssets.Particles("Spray", _nozzle, assets.ParticleAlpha);
            {
                _spray.transform.localRotation = Quaternion.identity;
                var main = _spray.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                main.startColor = new Color(0.95f, 0.95f, 1f, 0.7f);
                main.gravityModifier = 0.15f;

                var shape = _spray.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = SprayHalfAngle * 0.6f;
                shape.radius = 0.01f;

                var emission = _spray.emission;
                emission.rateOverTime = 140f;

                var size = _spray.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 5f));

                var col = _spray.colorOverLifetime;
                col.enabled = true;
                col.color = ProceduralAssets.Gradient(
                    (new Color(1f, 1f, 1f, 0.8f), 0f),
                    (new Color(0.9f, 0.9f, 0.95f, 0f), 1f));
            }

            _hiss = ProceduralAssets.LoopingSource(gameObject, assets.Hiss, 0.5f, 1f, 8f, playNow: false);
        }

        private void Update()
        {
            if (!IsHeld)
            {
                TryPickUp();
                return;
            }

            if (!ControllerInput.Grip(_holder))
            {
                Drop();
                return;
            }

            UpdateSpray();
        }

        private void TryPickUp()
        {
            var grabPoint = transform.position + Vector3.up * 0.45f;
            foreach (var hand in ControllerInput.Hands)
            {
                if (!ControllerInput.TryGetPosition(hand, out var p) ||
                    Vector3.Distance(p, grabPoint) > PickupDistance ||
                    !ControllerInput.GripDown(hand))
                {
                    continue;
                }

                _holder = hand;
                var anchor = ControllerInput.Anchor(hand);
                transform.SetParent(anchor, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                // Hold by the neck: controller sits at the valve, nozzle along controller forward.
                _visual.localPosition = new Vector3(0f, -0.47f, -0.02f);
                ControllerInput.Pulse(hand, 0.4f, 0.08f);
                _scorer.MarkReaction("picked up extinguisher");
                DrillLog.Info($"Picked up extinguisher ({hand}, {Charge:F0}s of charge).");
                return;
            }
        }

        private void Drop()
        {
            StopSpray();
            var p = transform.position;
            transform.SetParent(_originalParent, true);
            transform.position = new Vector3(p.x, _floorY, p.z);
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            _visual.localPosition = Vector3.zero;
            DrillLog.Info($"Put down extinguisher ({Charge:F1}s of charge left).");
            _holder = OVRInput.Controller.None;
        }

        private void UpdateSpray()
        {
            var wantsSpray = ControllerInput.Trigger(_holder);
            if (!wantsSpray || Charge <= 0f)
            {
                if (Charge <= 0f && !_reportedEmpty)
                {
                    _reportedEmpty = true;
                    DrillLog.Info("Extinguisher is empty.");
                }

                StopSpray();
                return;
            }

            if (!IsSpraying)
            {
                IsSpraying = true;
                _spray.Play();
                _hiss.Play();
            }

            Charge = Mathf.Max(0f, Charge - Time.deltaTime);
            OVRInput.SetControllerVibration(0.3f, 0.25f, _holder);

            if (_fire == null || _fire.IsOut)
            {
                return;
            }

            _scorer.AddSprayTime(Time.deltaTime, _fire.Intensity);

            var toFire = _fire.Center - _nozzle.position;
            var distance = toFire.magnitude;
            if (distance <= SprayRange && Vector3.Angle(_nozzle.forward, toFire) <= SprayHalfAngle)
            {
                // Closer is more effective, like a real extinguisher's effective range.
                var effectiveness = Mathf.Lerp(1f, 0.35f, distance / SprayRange);
                _fire.ApplyExtinguisher(KnockdownPerSecond * effectiveness * Time.deltaTime);
            }
        }

        private void StopSpray()
        {
            if (!IsSpraying)
            {
                return;
            }

            IsSpraying = false;
            _spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _hiss.Stop();
            if (_holder != OVRInput.Controller.None)
            {
                OVRInput.SetControllerVibration(0f, 0f, _holder);
            }
        }

        private void OnDisable()
        {
            if (_holder != OVRInput.Controller.None)
            {
                OVRInput.SetControllerVibration(0f, 0f, _holder);
            }
        }
    }
}
