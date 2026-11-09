// Copyright (c) MR-ExitReady contributors.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MRExitReady.Common
{
    /// <summary>
    /// A small ring buffer of recent log lines that also forwards every line to the Unity log
    /// with a fixed tag, so it can be read live in the headset and with
    /// <c>adb logcat -s Unity | grep &lt;tag&gt;</c>.
    /// </summary>
    public class TaggedLog
    {
        private readonly string _tag;
        private readonly int _capacity;
        private readonly List<string> _lines = new();

        public event Action<string> LineAdded;

        public TaggedLog(string tag, int capacity = 40)
        {
            _tag = tag;
            _capacity = capacity;
        }

        public IReadOnlyList<string> Recent => _lines;

        public void Info(string message) => Write(message, LogType.Log);
        public void Warn(string message) => Write(message, LogType.Warning);
        public void Error(string message) => Write(message, LogType.Error);

        public void Clear() => _lines.Clear();

        private void Write(string message, LogType type)
        {
            var line = $"{Time.realtimeSinceStartup:F1}s {message}";
            if (_lines.Count >= _capacity)
            {
                _lines.RemoveAt(0);
            }

            _lines.Add(line);

            var tagged = $"[{_tag}] {line}";
            switch (type)
            {
                case LogType.Warning: Debug.LogWarning(tagged); break;
                case LogType.Error: Debug.LogError(tagged); break;
                default: Debug.Log(tagged); break;
            }

            LineAdded?.Invoke(line);
        }
    }
}
