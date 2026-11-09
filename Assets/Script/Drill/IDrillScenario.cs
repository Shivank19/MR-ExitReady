// Copyright (c) MR-ExitReady contributors.

using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>One line of the in-drill checklist.</summary>
    public class Objective
    {
        public readonly string Label;
        public bool Done;
        public bool Failed;

        public Objective(string label)
        {
            Label = label;
        }
    }

    /// <summary>Tuning for the hazards, set from the DrillGameManager Inspector.</summary>
    [Serializable]
    public class ScenarioSettings
    {
        [Tooltip("Fire size at the start (0-1). Below ~0.5 counts as 'small enough to fight'.")]
        [Range(0f, 1f)] public float fireStartIntensity = 0.3f;

        [Tooltip("How fast the fire grows, intensity per second.")]
        public float fireGrowthPerSecond = 0.008f;

        [Tooltip("Seconds for the smoke layer to bank down from the ceiling to its lowest point.")]
        public float smokeDescendSeconds = 60f;

        [Tooltip("Lowest the smoke gets, in metres above the floor. Crouching below this clears it.")]
        public float smokeMinHeight = 1.15f;

        [Tooltip("Seconds of spray in the extinguisher.")]
        public float extinguisherSeconds = 10f;
    }

    /// <summary>Everything a scenario needs to build itself.</summary>
    public class DrillContext
    {
        public Transform Root;
        public ProceduralAssets Assets;
        public DrillScorer Scorer;
        public MRUKRoom Room;
        public ScenarioSettings Settings;
    }

    /// <summary>
    /// A drill scenario: builds its hazards and props in the scanned room, drives the
    /// instructions and objectives each frame, and says when it is over. New scenarios
    /// (kitchen fire, smoke-filled corridor, ...) implement this and plug into DrillGameManager.
    /// </summary>
    public interface IDrillScenario
    {
        string Title { get; }

        /// <summary>Rich text shown before the drill starts.</summary>
        string Briefing { get; }

        /// <summary>Lays out the scenario. Returns false with a reason if the room can't host it.</summary>
        bool Setup(DrillContext context, out string error);

        void Tick(float elapsed, float remaining);

        /// <summary>What the user should do right now.</summary>
        string Instruction { get; }

        /// <summary>Extra warning line (e.g. "you're in smoke"), or null.</summary>
        string Warning { get; }

        IReadOnlyList<Objective> Objectives { get; }

        bool IsFinished { get; }

        /// <summary>Stops hazards and interaction at the end of the drill, leaving visuals in place.</summary>
        void Freeze();

        /// <summary>Destroys everything the scenario created.</summary>
        void Teardown();
    }
}
