// Copyright (c) MR-ExitReady contributors.

using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// Changes how the real room looks through passthrough as the drill escalates: it gets darker
    /// and more washed-out as the fire grows and the smoke banks down, and flashes bright with
    /// every strobe once the alarm is pulled. Only the passthrough image is adjusted, so the
    /// virtual fire keeps its full colour. Restores passthrough when disabled.
    /// </summary>
    public class Atmosphere : MonoBehaviour
    {
        private OVRPassthroughLayer _passthrough;
        private FireHazard _fire;
        private FireAlarmStation _alarm;
        private SmokeLayer _smoke;
        private float _darkness;

        public static Atmosphere Create(Transform parent, FireHazard fire, FireAlarmStation alarm, SmokeLayer smoke)
        {
            var go = new GameObject("Atmosphere");
            go.transform.SetParent(parent, false);
            var atmosphere = go.AddComponent<Atmosphere>();
            atmosphere._fire = fire;
            atmosphere._alarm = alarm;
            atmosphere._smoke = smoke;
            atmosphere._passthrough = FindAnyObjectByType<OVRPassthroughLayer>();
            if (atmosphere._passthrough == null)
            {
                DrillLog.Warn("No OVRPassthroughLayer in the scene; room dimming is off.");
            }

            return atmosphere;
        }

        private void Update()
        {
            if (_passthrough == null)
            {
                return;
            }

            // Danger 0..1 from the fire size and how far the smoke has come down.
            var fire = _fire != null && !_fire.IsOut ? _fire.Intensity : 0.15f;
            var smoke = _smoke != null && _smoke.IsLow ? 1f : 0f;
            var target = Mathf.Clamp01(0.75f * fire + 0.35f * smoke);
            _darkness = Mathf.MoveTowards(_darkness, target, Time.deltaTime * 0.5f);

            var brightness = -0.45f * _darkness;
            var saturation = -0.55f * _darkness;
            var contrast = 0.15f * _darkness;

            if (_alarm != null && _alarm.StrobeOn)
            {
                brightness += 0.5f;
            }

            _passthrough.SetBrightnessContrastSaturation(Mathf.Clamp(brightness, -1f, 1f), contrast, saturation);
        }

        private void OnDisable()
        {
            if (_passthrough != null)
            {
                _passthrough.SetBrightnessContrastSaturation(0f, 0f, 0f);
                _passthrough.DisableColorMap();
            }
        }
    }
}
