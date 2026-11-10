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

        [Tooltip("Heat level (0-1, see HeatCues) above which the user counts as too close to the fire.")]
        public float tooCloseHeat = 0.65f;
        public float heatPenaltyPerSecond = 1f;
        public int heatPenaltyCap = 10;
    }

    public enum EventKind
    {
        Neutral,
        Good,
        Bad,
    }

    /// <summary>One timestamped entry of the drill timeline.</summary>
    public readonly struct TimelineEvent
    {
        public readonly float Time;
        public readonly string Label;
        public readonly EventKind Kind;

        public TimelineEvent(float time, string label, EventKind kind)
        {
            Time = time;
            Label = label;
            Kind = kind;
        }
    }

    /// <summary>Measurements taken during the drill, shown on the debrief.</summary>
    public class DrillMetrics
    {
        public float? ReactionTime;
        public float? AlarmTime;
        public float? FirstDoorCheckTime;
        public float? ExitTime;
        public float DistanceWalked;
        public float CrouchSeconds;
        public float SmokeSeconds;
        public int SmokeEntries;
        public float HeatSeconds;
        public float ClosestFireDistance = float.PositiveInfinity;
        public float IdleSeconds;
        public int DoorsChecked;
        public float SpraySeconds;
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
        public readonly List<TimelineEvent> Timeline = new();
        public DrillMetrics Metrics;
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
        private bool _pickedUpExtinguisher;

        // Per-frame tracking.
        private readonly DrillMetrics _m = new();
        private readonly List<TimelineEvent> _timeline = new();
        private readonly HashSet<string> _checkedDoors = new();
        private Vector3? _lastHead;
        private float _idleStreak;
        private bool _wasTooClose;

        /// <summary>Standing still for at least this long counts as hesitating.</summary>
        private const float IdleStreakSeconds = 2f;

        /// <summary>Slower than this (m/s, on the floor plane) counts as standing still.</summary>
        private const float IdleSpeed = 0.15f;

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
            AddEvent("Fire breaks out", EventKind.Neutral);
            DrillLog.Info("Drill started.");
        }

        /// <summary>Adds a timestamped entry to the debrief timeline.</summary>
        public void AddEvent(string label, EventKind kind)
        {
            if (_startTime < 0f)
            {
                return;
            }

            _timeline.Add(new TimelineEvent(Now, label, kind));
        }

        /// <summary>
        /// Per-frame body tracking: walked distance, standing still, crouching and distance to the
        /// fire. <paramref name="heat"/> is the 0-1 heat level from <see cref="HeatCues"/>.
        /// </summary>
        public void TrackFrame(Vector3 head, float headHeight, float standingHeight, float fireDistance, float heat,
            float dt)
        {
            if (_startTime < 0f || Outcome != DrillOutcome.InProgress || dt <= 0f)
            {
                return;
            }

            // Distance and stillness on the floor plane; tiny tracking jitter is ignored.
            var flat = new Vector3(head.x, 0f, head.z);
            var step = _lastHead.HasValue ? Vector3.Distance(flat, _lastHead.Value) : 0f;
            _lastHead = flat;
            if (step > 0.004f && step < 1f)
            {
                _m.DistanceWalked += step;
            }

            if (step / dt < IdleSpeed)
            {
                _idleStreak += dt;
            }
            else
            {
                FlushIdle();
            }

            if (standingHeight > 0.5f && headHeight < standingHeight * 0.75f)
            {
                _m.CrouchSeconds += dt;
            }

            _m.ClosestFireDistance = Mathf.Min(_m.ClosestFireDistance, fireDistance);

            var tooClose = heat >= _w.tooCloseHeat;
            if (tooClose)
            {
                _m.HeatSeconds += dt;
                if (!_wasTooClose)
                {
                    AddEvent("Got too close to the fire", EventKind.Bad);
                    DrillLog.Info($"Too close to the fire ({fireDistance:F2} m).");
                }
            }

            _wasTooClose = tooClose;
        }

        private void FlushIdle()
        {
            if (_idleStreak >= IdleStreakSeconds)
            {
                _m.IdleSeconds += _idleStreak;
            }

            _idleStreak = 0f;
        }

        public void SmokeEntered()
        {
            _m.SmokeEntries++;
            if (_m.SmokeEntries == 1)
            {
                AddEvent("Walked into the smoke", EventKind.Bad);
            }
        }

        public void ExtinguisherPickedUp()
        {
            if (_pickedUpExtinguisher)
            {
                return;
            }

            _pickedUpExtinguisher = true;
            MarkReaction("picked up extinguisher");
            AddEvent("Picked up the extinguisher", EventKind.Neutral);
            DrillLog.Info("Picked up the extinguisher.");
        }

        /// <summary>
        /// The first deliberate action (move, grab, pull, check) marks the reaction time. Actions
        /// that add their own timeline entry pass <paramref name="timelineLabel"/> = null.
        /// </summary>
        public void MarkReaction(string what, string timelineLabel = null)
        {
            if (_reactionTime.HasValue || _startTime < 0f)
            {
                return;
            }

            _reactionTime = Now;
            if (timelineLabel != null)
            {
                AddEvent(timelineLabel, EventKind.Neutral);
            }

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
            AddEvent("Pulled the fire alarm", EventKind.Good);
            DrillLog.Info($"Alarm pulled at {_alarmTime:F1}s.");
        }

        public void DoorChecked(string door, bool hot)
        {
            if (!_checkedAnyDoor)
            {
                _m.FirstDoorCheckTime = Now;
            }

            _checkedAnyDoor = true;
            MarkReaction("checked a door");
            if (_checkedDoors.Add(door))
            {
                AddEvent($"Checked {door}: {(hot ? "HOT" : "cool")}", EventKind.Good);
            }

            DrillLog.Info($"Checked {door}: {(hot ? "HOT" : "cool")}.");
        }

        public void AddSmokeTime(float seconds) => _smokeSeconds += seconds;

        public void AddSprayTime(float seconds, float fireIntensity)
        {
            if (_sprayTotal == 0f)
            {
                MarkReaction("used extinguisher");
                AddEvent(fireIntensity > _w.largeFireIntensity ? "Sprayed a fire that was too big" : "Started spraying",
                    fireIntensity > _w.largeFireIntensity ? EventKind.Bad : EventKind.Neutral);
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
            AddEvent("Put the fire out", EventKind.Good);
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
            AddEvent(hot ? $"Opened {door} into the fire" : $"Got out through {door}", hot ? EventKind.Bad : EventKind.Good);
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
            AddEvent("Time ran out", EventKind.Bad);
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

            // Distance from the fire.
            var heatPenalty = -Mathf.Min(_w.heatPenaltyCap, Mathf.RoundToInt(_m.HeatSeconds * _w.heatPenaltyPerSecond));
            if (heatPenalty < 0)
            {
                r.Lines.Add(new ScoreLine(false, $"Too close to the fire for {_m.HeatSeconds:F0}s", heatPenalty,
                    "Keep a couple of metres from flames. An extinguisher works from 2-3 m away."));
            }

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

            FlushIdle();
            _m.ReactionTime = _reactionTime;
            _m.AlarmTime = _alarmTime;
            _m.ExitTime = escaped ? r.Duration : null;
            _m.SmokeSeconds = _smokeSeconds;
            _m.SpraySeconds = _sprayTotal;
            _m.DoorsChecked = _checkedDoors.Count;
            r.Metrics = _m;
            r.Timeline.AddRange(_timeline);
            r.Passed = escaped;
            r.Grade = !escaped ? "NOT READY"
                : r.Total >= 85 ? "EXIT READY"
                : r.Total >= 65 ? "MOSTLY READY"
                : "NEEDS PRACTICE";

            DrillLog.Info($"Score {r.Total}/100 - {r.Grade} ({Outcome}, {r.Duration:F1}s, smoke {_smokeSeconds:F1}s, " +
                          $"alarm {(_alarmTime.HasValue ? _alarmTime.Value.ToString("F1") + "s" : "no")}, " +
                          $"reaction {(_reactionTime.HasValue ? _reactionTime.Value.ToString("F1") + "s" : "-")}, " +
                          $"walked {_m.DistanceWalked:F1} m, crouched {_m.CrouchSeconds:F1}s, idle {_m.IdleSeconds:F1}s, " +
                          $"closest to fire {(_m.ClosestFireDistance < 1000f ? _m.ClosestFireDistance.ToString("F2") + " m" : "-")}).");
            return r;
        }
    }
}
