// Copyright (c) MR-ExitReady contributors.

using System.Collections.Generic;
using MRExitReady.Common;

namespace MRExitReady.Drill
{
    /// <summary>
    /// Live log of the fire drill. Lines show on the drill HUD and in logcat:
    /// <c>adb logcat -s Unity | grep ExitReady-Drill</c>.
    /// </summary>
    public static class DrillLog
    {
        private static readonly TaggedLog Log = new("ExitReady-Drill");

        public static IReadOnlyList<string> Recent => Log.Recent;

        public static void Info(string message) => Log.Info(message);
        public static void Warn(string message) => Log.Warn(message);
        public static void Error(string message) => Log.Error(message);
    }
}
