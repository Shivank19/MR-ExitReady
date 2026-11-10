// Copyright (c) MR-ExitReady contributors.

using System.Collections.Generic;
using System.Text;
using MRExitReady.Common;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// The player-facing panels:
    /// <list type="bullet">
    /// <item>front panel (lazily follows the head): scan status, briefing, countdown;</item>
    /// <item>wrist panel (above the left controller) during the drill: timer, instruction, checklist,
    /// so the view stays clear. Falls back to the front panel if the controller isn't tracked;</item>
    /// <item>warning strip, low in the view, only while something urgent is happening;</item>
    /// <item>debrief (two columns): score and tips | timeline and measurements.</item>
    /// </list>
    /// </summary>
    public class DrillHud
    {
        private static readonly Color Calm = new(0.03f, 0.05f, 0.08f, 0.82f);
        private static readonly Color Alert = new(0.35f, 0.04f, 0.02f, 0.85f);
        private static readonly Color Warn = new(0.45f, 0.05f, 0.02f, 0.9f);

        private const string Green = "#8ce99a";
        private const string Red = "#ff6b6b";
        private const string Yellow = "#ffd166";
        private const string Grey = "#9aa5b1";

        private readonly HeadFollowPanel _front;
        private readonly HeadFollowPanel _debrief;
        private readonly HeadFollowPanel _warning;
        private readonly WristPanel _wrist;
        private readonly StringBuilder _sb = new();

        public DrillHud(Transform root)
        {
            _front = new HeadFollowPanel(Child(root, "FrontPanel"), new Vector2(620f, 520f), 24f, Calm)
            {
                Distance = 0.9f,
                VerticalOffset = -0.25f,
                DeadZoneAngle = 25f,
                FollowSpeed = 3f,
            };

            _debrief = new HeadFollowPanel(Child(root, "DebriefPanel"), new Vector2(1040f, 640f), 21f, Calm,
                columns: 2, wrap: true)
            {
                Distance = 1.0f,
                VerticalOffset = -0.15f,
                DeadZoneAngle = 30f,
                FollowSpeed = 2.5f,
            };

            _warning = new HeadFollowPanel(Child(root, "WarningStrip"), new Vector2(520f, 70f), 26f, Warn)
            {
                Distance = 0.8f,
                VerticalOffset = -0.18f,
                DeadZoneAngle = 8f,
                FollowSpeed = 8f,
            };
            _warning.Text.alignment = TMPro.TextAlignmentOptions.Center;

            _wrist = new WristPanel(root, Calm);
            Show(front: true);
        }

        private static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private void Show(bool front = false, bool debrief = false, bool wrist = false, bool warning = false)
        {
            _front.Visible = front;
            _debrief.Visible = debrief;
            _wrist.Visible = wrist;
            _warning.Visible = warning;
        }

        /// <summary>Call once per frame.</summary>
        public void Follow()
        {
            _front.Follow();
            _debrief.Follow();
            _warning.Follow();
            if (_wrist.Visible)
            {
                _wrist.Follow();
            }
        }

        public void ShowMessage(string title, string body, IReadOnlyList<string> logTail = null)
        {
            Show(front: true);
            _front.SetBackground(Calm);
            _sb.Clear();
            _sb.AppendLine($"<size=130%><b>{title}</b></size>");
            _sb.AppendLine();
            _sb.AppendLine(body);
            if (logTail != null && logTail.Count > 0)
            {
                _sb.AppendLine();
                _sb.Append($"<size=65%><color={Grey}>");
                for (var i = Mathf.Max(0, logTail.Count - 5); i < logTail.Count; i++)
                {
                    var line = logTail[i];
                    _sb.AppendLine(line.Length > 70 ? line.Substring(0, 69) + "…" : line);
                }

                _sb.Append("</color></size>");
            }

            _front.Text.text = _sb.ToString();
        }

        public void ShowBriefing(string title, string briefing, string error)
        {
            Show(front: true);
            _front.SetBackground(Calm);
            _sb.Clear();
            _sb.AppendLine($"<size=130%><b>{title}</b></size>");
            _sb.AppendLine();
            _sb.AppendLine($"<size=85%>{briefing}</size>");
            _sb.AppendLine();
            if (!string.IsNullOrEmpty(error))
            {
                _sb.AppendLine($"<color={Red}>{error}</color>");
            }

            _sb.Append($"<color={Green}><b>Press A to start</b></color>");
            _front.Text.text = _sb.ToString();
        }

        public void ShowCountdown(int seconds)
        {
            Show(front: true);
            _front.SetBackground(Calm);
            _front.Text.text = $"\n<align=center><size=300%><b>{seconds}</b></size>\n\nGet ready...\n\n" +
                               $"<size=80%><color={Yellow}>Your timer and tasks are on your\n<b>left wrist</b>. " +
                               "Glance down to check them.</color></size></align>";
        }

        public void ShowActive(string title, float remaining, string instruction, string warning,
            IReadOnlyList<Objective> objectives)
        {
            var urgent = remaining < 15f;

            // Prefer the wrist; if the left controller isn't tracked, use the front panel.
            var onWrist = WristPanel.Available;
            var hasWarning = !string.IsNullOrEmpty(warning);
            Show(front: !onWrist, wrist: onWrist, warning: onWrist && hasWarning);

            _sb.Clear();
            var clock = Clock(remaining);
            _sb.AppendLine($"<size=190%><b><color={(urgent ? Red : "#ffffff")}>{clock}</color></b></size>");
            _sb.AppendLine($"<color={Yellow}><b>{instruction}</b></color>");
            if (!onWrist && hasWarning)
            {
                _sb.AppendLine($"<color={Red}><b>{warning}</b></color>");
            }

            _sb.Append("<size=85%>");
            foreach (var o in objectives)
            {
                var mark = o.Failed ? $"<color={Red}>[x]</color>"
                    : o.Done ? $"<color={Green}>[v]</color>"
                    : $"<color={Grey}>[ ]</color>";
                _sb.AppendLine($"{mark} {o.Label}");
            }

            _sb.Append("</size>");

            if (onWrist)
            {
                _wrist.SetBackground(urgent ? Alert : Calm);
                _wrist.Text.text = _sb.ToString();
                if (hasWarning)
                {
                    _warning.Text.text = $"<b>{warning}</b>";
                }
            }
            else
            {
                _front.SetBackground(urgent ? Alert : Calm);
                _front.Text.text = $"<size=80%><color={Grey}>{title}</color></size>\n" + _sb;
            }
        }

        /// <summary>The big moment right after the drill ends, shown during the slow-motion beat.</summary>
        public void ShowOutcome(ScoreReport report)
        {
            Show(front: true);
            var (text, color, bg) = report.Outcome switch
            {
                DrillOutcome.Escaped => ("YOU GOT OUT", Green, Calm),
                DrillOutcome.HotDoor => ("HOT DOOR", Red, Alert),
                _ => ("TIME'S UP", Red, Alert),
            };
            _front.SetBackground(bg);
            _front.Text.text = $"\n\n<align=center><size=260%><b><color={color}>{text}</color></b></size>\n\n" +
                               $"<size=120%>{Clock(report.Duration)}</size></align>";
        }

        public void ShowDebrief(string title, ScoreReport report, float timeLimit)
        {
            Show(debrief: true);
            _debrief.SetBackground(report.Passed ? Calm : Alert);
            _debrief.Text.text = DebriefScore(title, report, timeLimit);
            _debrief.Text2.text = DebriefDetails(report);
        }

        // ── Debrief columns ───────────────────────────────────────────────────

        private string DebriefScore(string title, ScoreReport report, float timeLimit)
        {
            _sb.Clear();
            var headline = report.Outcome switch
            {
                DrillOutcome.Escaped => "You got out.",
                DrillOutcome.HotDoor => "You opened a door into the fire.",
                _ => "You didn't get out in time.",
            };

            var gradeColor = report.Passed ? (report.Total >= 85 ? Green : Yellow) : Red;
            _sb.AppendLine($"<size=80%><color={Grey}>{title}</color></size>");
            _sb.AppendLine($"<size=160%><b><color={gradeColor}>{report.Grade}</color></b></size>");
            _sb.AppendLine($"<size=140%><b>{report.Total}</b>/100</size>   {headline}");
            _sb.AppendLine();

            var timeColor = report.Outcome == DrillOutcome.Escaped ? Green : Red;
            _sb.AppendLine($"<size=80%><color={Grey}>TOTAL TIME</color></size>");
            _sb.AppendLine($"<size=190%><b><color={timeColor}>{Clock(report.Duration)}</color></b></size>" +
                           $"  <color={Grey}>of {Clock(timeLimit)}</color>");
            _sb.AppendLine();

            _sb.Append("<size=85%>");
            foreach (var line in report.Lines)
            {
                var points = line.Points == 0 ? "   " : line.Points > 0 ? $"+{line.Points}" : $"{line.Points}";
                var color = line.Good ? Green : Red;
                _sb.AppendLine($"<color={color}>{points,4}</color>  {line.Label}");
            }

            var tips = 0;
            foreach (var line in report.Lines)
            {
                if (string.IsNullOrEmpty(line.Tip) || tips >= 2)
                {
                    continue;
                }

                if (tips == 0)
                {
                    _sb.AppendLine();
                    _sb.AppendLine("<b>Next time</b>");
                }

                _sb.AppendLine($"<color={Yellow}>- {line.Tip}</color>");
                tips++;
            }

            _sb.AppendLine("</size>");
            _sb.Append($"<color={Green}><b>Press A to try again</b></color>");
            return _sb.ToString();
        }

        private string DebriefDetails(ScoreReport report)
        {
            const int maxEvents = 11;
            _sb.Clear();
            _sb.AppendLine("<b>Timeline</b>");
            _sb.Append("<size=82%>");
            var timeline = report.Timeline;
            var first = Mathf.Max(0, timeline.Count - maxEvents);
            if (first > 0)
            {
                _sb.AppendLine($"<color={Grey}>  ... {first} earlier</color>");
            }

            for (var i = first; i < timeline.Count; i++)
            {
                var e = timeline[i];
                var color = e.Kind == EventKind.Good ? Green : e.Kind == EventKind.Bad ? Red : "#ffffff";
                _sb.AppendLine($"<color={Grey}>{Clock(e.Time)}</color>  <color={color}>{e.Label}</color>");
            }

            _sb.AppendLine("</size>");

            var m = report.Metrics;
            if (m == null)
            {
                return _sb.ToString();
            }

            _sb.AppendLine("<b>Your numbers</b>");
            _sb.Append("<size=82%>");
            Metric("First reaction", Seconds(m.ReactionTime), m.ReactionTime.HasValue && m.ReactionTime.Value <= 5f);
            Metric("Alarm raised", m.AlarmTime.HasValue ? "at " + Clock(m.AlarmTime.Value) : "never",
                m.AlarmTime.HasValue);
            Metric("Doors checked", m.DoorsChecked.ToString(), m.DoorsChecked > 0);
            Metric("Distance walked", $"{m.DistanceWalked:F1} m", null);
            Metric("Time crouched", $"{m.CrouchSeconds:F0}s", m.CrouchSeconds >= 3f ? true : null);
            Metric("Time in smoke", $"{m.SmokeSeconds:F0}s", m.SmokeSeconds < 3f);
            Metric("Closest to fire",
                float.IsPositiveInfinity(m.ClosestFireDistance) ? "-" : $"{m.ClosestFireDistance:F1} m",
                float.IsPositiveInfinity(m.ClosestFireDistance) || m.ClosestFireDistance >= 1.2f);
            Metric("Standing still", $"{m.IdleSeconds:F0}s", m.IdleSeconds < 10f);
            if (m.SpraySeconds > 0f)
            {
                Metric("Extinguisher used", $"{m.SpraySeconds:F0}s", null);
            }

            _sb.Append("</size>");
            return _sb.ToString();
        }

        /// <param name="good">true = green, false = red, null = neutral.</param>
        private void Metric(string label, string value, bool? good)
        {
            var color = good == null ? "#ffffff" : good.Value ? Green : Red;
            _sb.AppendLine($"{label}<pos=62%><color={color}><b>{value}</b></color>");
        }

        private static string Seconds(float? s) => s.HasValue ? $"{s.Value:F1}s" : "-";

        private static string Clock(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            return $"{Mathf.FloorToInt(seconds / 60f):0}:{Mathf.FloorToInt(seconds % 60f):00}";
        }
    }
}
