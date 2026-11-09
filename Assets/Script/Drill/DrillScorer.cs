// Copyright (c) MR-ExitReady contributors.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>Point values for the readiness score. Tunable from the DrillGameManager Inspector.</summary>
    [Serializable]
    public class ScoreWeights
    {
        public int escaped = 40;
        public int alarmPulled = 15;
        public int alarmPulledEarlyBonus = 5;
        public float alarmEarlySeconds = 10f;
        public int doorChecked = 15;
        public int hotDoorOpened = -30;
        public float smokePenaltyPerSecond = 1f;
        public int smokePenaltyCap = 15;
        public int extinguishedSmallFire = 10;
        public int foughtLargeFire = -10;
        public float largeFireIntensity = 0.7f;
        public float largeFireSpraySeconds = 8f;
        public float smallFireIntensity = 0.5f;
        public int speedBonusMax = 15;
        public float speedBonusSeconds = 45f;
    }

    public enum DrillOutcome
    {
        InProgress,
        Escaped,
        HotDoor,
        TimedOut,
    }

    /// <summary>One row of the debrief.</summary>
    public readonly struct ScoreLine
    {
        public readonly bool Good;
        public readonly string Label;
        public readonly int Points;
        public readonly string Tip;

        public ScoreLine(bool good, string label, int points, string tip = null)
        {
            Good = good;
            Label = label;
            Points = points;
            Tip = tip;
        }
    }

    public class ScoreReport
    {
        public int Total;
        public string Grade;
        public bool Passed;
        public DrillOutcome Outcome;
        public float Duration;
        public readonly List<ScoreLine> Lines = new();
    }

    /// <summary>
    /// Records what the user did during a drill and turns it into a 0-100 readiness score.
    /// Every recorded event is also written to <see cref="DrillLog"/>.
    /// </summary>
    public class DrillScorer
    {
        private readonly ScoreWeights _w;

        private float _startTime = -1f;
        private float? _reactionTime;
        private float? _alarmTime;
        private bool _checkedAnyDoor;
        private bool _checkedExitDoor;
        private float _smokeSeconds;
        private float _sprayTotal;
        private float _sprayOnLargeFire;
        private bool _extinguishedSmall;
        private bool _extinguished;
        private float _endTime = -1f;

        public DrillOutcome Outcome { get; private set; } = DrillOutcome.InProgress;
        public bool AlarmPulled => _alarmTime.HasValue;
        public bool CheckedAnyDoor => _checkedAnyDoor;
        public bool FireOut => _extinguished;
        public float SmokeSeconds => _smokeSeconds;

        public DrillScorer(ScoreWeights weights)
        {
            _w = weights ?? new ScoreWeights();
        }

        private float Now => Time.time - _startTime;

        public void Begin()
        {
            _startTime = Time.time;
            DrillLog.Info("Drill started.");
        }

        /// <summary>The first deliberate action (move, grab, pull, check) marks the reaction time.</summary>
        public void MarkReaction(string what)
        {
            if (_reactionTime.HasValue || _startTime < 0f)
            {
                return;
            }

            _reactionTime = Now;
            DrillLog.Info($"Reaction after {_reactionTime:F1}s ({what}).");
        }

        public void AlarmPulledEvent()
        {
            if (_alarmTime.HasValue)
            {
                return;
            }

            _alarmTime = Now;
            MarkReaction("pulled alarm");
            DrillLog.Info($"Alarm pulled at {_alarmTime:F1}s.");
        }

        public void DoorChecked(string door, bool hot)
        {
            _checkedAnyDoor = true;
            MarkReaction("checked a door");
            DrillLog.Info($"Checked {door}: {(hot ? "HOT" : "cool")}.");
        }

        public void AddSmokeTime(float seconds) => _smokeSeconds += seconds;

        public void AddSprayTime(float seconds, float fireIntensity)
        {
            if (_sprayTotal == 0f)
            {
                MarkReaction("used extinguisher");
                DrillLog.Info($"Started spraying (fire intensity {fireIntensity:F2}).");
            }

            _sprayTotal += seconds;
            if (fireIntensity > _w.largeFireIntensity)
            {
                _sprayOnLargeFire += seconds;
            }
        }

        public void FireExtinguished(float intensityWhenFirstSprayed)
        {
            _extinguished = true;
            _extinguishedSmall = intensityWhenFirstSprayed <= _w.smallFireIntensity;
            DrillLog.Info($"Fire extinguished (it was {(_extinguishedSmall ? "small" : "large")} when you started).");
        }

        public void Exited(string door, bool hot, bool checkedThisDoor)
        {
            if (Outcome != DrillOutcome.InProgress)
            {
                return;
            }

            _checkedExitDoor = checkedThisDoor;
            _endTime = Now;
            Outcome = hot ? DrillOutcome.HotDoor : DrillOutcome.Escaped;
            DrillLog.Info($"Opened {door} at {_endTime:F1}s -> {(hot ? "HOT DOOR" : "escaped")}.");
        }

        public void TimedOut()
        {
            if (Outcome != DrillOutcome.InProgress)
            {
                return;
            }

            _endTime = Now;
            Outcome = DrillOutcome.TimedOut;
            DrillLog.Warn("Time ran out.");
        }

        public ScoreReport Build()
        {
            var r = new ScoreReport { Outcome = Outcome, Duration = _endTime >= 0f ? _endTime : Now };
            var escaped = Outcome == DrillOutcome.Escaped;

            // Getting out.
            switch (Outcome)
            {
                case DrillOutcome.Escaped:
                    r.Lines.Add(new ScoreLine(true, $"Got out in {r.Duration:F0}s", _w.escaped));
                    break;
                case DrillOutcome.HotDoor:
                    r.Lines.Add(new ScoreLine(false, "Opened a hot door", _w.hotDoorOpened,
                        "A hot door means fire on the other side. Use another exit."));
                    break;
                default:
                    r.Lines.Add(new ScoreLine(false, "Did not get out in time", 0,
                        "Leaving is the priority. Possessions and firefighting come second."));
                    break;
            }

            // Alarm.
            if (_alarmTime.HasValue)
            {
                var early = _alarmTime.Value <= _w.alarmEarlySeconds;
                r.Lines.Add(new ScoreLine(true, $"Raised the alarm at {_alarmTime:F0}s",
                    _w.alarmPulled + (early ? _w.alarmPulledEarlyBonus : 0)));
            }
            else
            {
                r.Lines.Add(new ScoreLine(false, "Never raised the alarm", 0,
                    "Pull the alarm on your way out so everyone else gets out too."));
            }

            // Door check.
            if (_checkedExitDoor || (_checkedAnyDoor && Outcome != DrillOutcome.HotDoor))
            {
                r.Lines.Add(new ScoreLine(true, "Checked the door before opening it", _w.doorChecked));
            }
            else
            {
                r.Lines.Add(new ScoreLine(false, "Opened a door without checking it", 0,
                    "Feel the door with the back of your hand first. Hot means don't open."));
            }

            // Smoke.
            var smokePenalty = -Mathf.Min(_w.smokePenaltyCap, Mathf.RoundToInt(_smokeSeconds * _w.smokePenaltyPerSecond));
            r.Lines.Add(smokePenalty == 0
                ? new ScoreLine(true, "Stayed below the smoke", 0)
                : new ScoreLine(false, $"Stood in smoke for {_smokeSeconds:F0}s", smokePenalty,
                    "Smoke rises. Crouch low where the air is cleaner."));

            // Extinguisher.
            if (_extinguished && _extinguishedSmall)
            {
                r.Lines.Add(new ScoreLine(true, "Put out a small fire", _w.extinguishedSmallFire));
            }
            else if (_sprayOnLargeFire >= _w.largeFireSpraySeconds)
            {
                r.Lines.Add(new ScoreLine(false, "Fought a fire that was too big", _w.foughtLargeFire,
                    "Only fight a fire that is small and when your exit is clear. Otherwise, leave."));
            }

            // Speed.
            if (escaped && r.Duration < _w.speedBonusSeconds)
            {
                var bonus = Mathf.RoundToInt(_w.speedBonusMax * (1f - r.Duration / _w.speedBonusSeconds));
                if (bonus > 0)
                {
                    r.Lines.Add(new ScoreLine(true, "Quick decision", bonus));
                }
            }

            var total = 0;
            foreach (var line in r.Lines)
            {
                total += line.Points;
            }

            r.Total = Mathf.Clamp(total, 0, 100);
            r.Passed = escaped;
            r.Grade = !escaped ? "NOT READY"
                : r.Total >= 85 ? "EXIT READY"
                : r.Total >= 65 ? "MOSTLY READY"
                : "NEEDS PRACTICE";

            DrillLog.Info($"Score {r.Total}/100 - {r.Grade} ({Outcome}, {r.Duration:F1}s, smoke {_smokeSeconds:F1}s, " +
                          $"alarm {(_alarmTime.HasValue ? _alarmTime.Value.ToString("F1") + "s" : "no")}, " +
                          $"reaction {(_reactionTime.HasValue ? _reactionTime.Value.ToString("F1") + "s" : "-")}).");
            return r;
        }
    }
}
