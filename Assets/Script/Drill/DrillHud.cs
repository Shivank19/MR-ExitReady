// Copyright (c) MR-ExitReady contributors.

using System.Collections.Generic;
using System.Text;
using MRExitReady.Common;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// The player-facing panel: scan status, briefing, countdown, live timer with the current
    /// instruction and checklist, and the score debrief.
    /// </summary>
    public class DrillHud
    {
        private static readonly Color Calm = new(0.03f, 0.05f, 0.08f, 0.82f);
        private static readonly Color Alert = new(0.35f, 0.04f, 0.02f, 0.85f);

        private readonly HeadFollowPanel _panel;
        private readonly StringBuilder _sb = new();

        public DrillHud(Transform root)
        {
            _panel = new HeadFollowPanel(root, new Vector2(620f, 520f), 24f, Calm)
            {
                Distance = 0.9f,
                VerticalOffset = -0.25f,
                DeadZoneAngle = 25f,
                FollowSpeed = 3f,
            };
        }

        public void Follow() => _panel.Follow();

        public void ShowMessage(string title, string body, IReadOnlyList<string> logTail = null)
        {
            _panel.SetBackground(Calm);
            _sb.Clear();
            _sb.AppendLine($"<size=130%><b>{title}</b></size>");
            _sb.AppendLine();
            _sb.AppendLine(body);
            if (logTail != null && logTail.Count > 0)
            {
                _sb.AppendLine();
                _sb.Append("<size=65%><color=#9aa5b1>");
                for (var i = Mathf.Max(0, logTail.Count - 5); i < logTail.Count; i++)
                {
                    var line = logTail[i];
                    _sb.AppendLine(line.Length > 70 ? line.Substring(0, 69) + "…" : line);
                }

                _sb.Append("</color></size>");
            }

            _panel.Text.text = _sb.ToString();
        }

        public void ShowBriefing(string title, string briefing, string error)
        {
            _panel.SetBackground(Calm);
            _sb.Clear();
            _sb.AppendLine($"<size=130%><b>{title}</b></size>");
            _sb.AppendLine();
            _sb.AppendLine($"<size=85%>{briefing}</size>");
            _sb.AppendLine();
            if (!string.IsNullOrEmpty(error))
            {
                _sb.AppendLine($"<color=#ff6b6b>{error}</color>");
            }

            _sb.Append("<color=#8ce99a><b>Press A to start</b></color>");
            _panel.Text.text = _sb.ToString();
        }

        public void ShowCountdown(int seconds)
        {
            _panel.SetBackground(Calm);
            _panel.Text.text = $"\n\n<align=center><size=300%><b>{seconds}</b></size>\n\nGet ready...</align>";
        }

        public void ShowActive(string title, float remaining, string instruction, string warning,
            IReadOnlyList<Objective> objectives)
        {
            var urgent = remaining < 15f;
            _panel.SetBackground(urgent ? Alert : Calm);

            _sb.Clear();
            var clock = $"{Mathf.FloorToInt(remaining / 60f):0}:{Mathf.FloorToInt(remaining % 60f):00}";
            _sb.AppendLine($"<size=170%><b><color={(urgent ? "#ff6b6b" : "#ffffff")}>{clock}</color></b></size>   <size=80%><color=#9aa5b1>{title}</color></size>");
            _sb.AppendLine();
            _sb.AppendLine($"<color=#ffd166><b>{instruction}</b></color>");
            _sb.AppendLine(string.IsNullOrEmpty(warning) ? "" : $"<color=#ff6b6b><b>{warning}</b></color>");
            _sb.AppendLine();
            foreach (var o in objectives)
            {
                var mark = o.Failed ? "<color=#ff6b6b>[x]</color>"
                    : o.Done ? "<color=#8ce99a>[v]</color>"
                    : "<color=#9aa5b1>[ ]</color>";
                _sb.AppendLine($"{mark} {o.Label}");
            }

            _panel.Text.text = _sb.ToString();
        }

        public void ShowDebrief(string title, ScoreReport report)
        {
            _panel.SetBackground(report.Passed ? Calm : Alert);
            _sb.Clear();

            var headline = report.Outcome switch
            {
                DrillOutcome.Escaped => "You got out.",
                DrillOutcome.HotDoor => "You opened a door into the fire.",
                _ => "You didn't get out in time.",
            };

            var gradeColor = report.Passed ? (report.Total >= 85 ? "#8ce99a" : "#ffd166") : "#ff6b6b";
            _sb.AppendLine($"<size=80%><color=#9aa5b1>{title}</color></size>");
            _sb.AppendLine($"<size=150%><b><color={gradeColor}>{report.Grade}</color></b></size>   <size=130%>{report.Total}/100</size>");
            _sb.AppendLine(headline);
            _sb.AppendLine();

            _sb.Append("<size=80%>");
            foreach (var line in report.Lines)
            {
                var points = line.Points == 0 ? "   " : line.Points > 0 ? $"+{line.Points}" : $"{line.Points}";
                var color = line.Good ? "#8ce99a" : "#ff6b6b";
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

                _sb.AppendLine($"<color=#ffd166>- {line.Tip}</color>");
                tips++;
            }

            _sb.AppendLine("</size>");
            _sb.Append("<color=#8ce99a><b>Press A to try again</b></color>");
            _panel.Text.text = _sb.ToString();
        }
    }
}
