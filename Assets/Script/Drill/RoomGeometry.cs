// Copyright (c) MR-ExitReady contributors.

using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>World-space helpers over a scanned <see cref="MRUKRoom"/>.</summary>
    public static class RoomGeometry
    {
        public const MRUKAnchor.SceneLabels Furniture =
            MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.STORAGE |
            MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.OTHER;

        public const MRUKAnchor.SceneLabels Walls =
            MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE;

        public static float FloorY(MRUKRoom room)
        {
            var floors = room.FloorAnchors;
            return floors != null && floors.Count > 0 && floors[0] != null
                ? floors[0].transform.position.y
                : room.GetRoomBounds().min.y;
        }

        public static float CeilingY(MRUKRoom room, float floorY)
        {
            var ceilings = room.CeilingAnchors;
            var y = ceilings != null && ceilings.Count > 0 && ceilings[0] != null
                ? ceilings[0].transform.position.y
                : room.GetRoomBounds().max.y;
            // Guard against bad data: rooms are 2.0-4.0 m tall.
            return Mathf.Clamp(y, floorY + 2f, floorY + 4f);
        }

        public static Vector3 Center(MRUKRoom room) => room.GetRoomBounds().center;

        /// <summary>A plane anchor's normal, flattened and flipped to point into the room.</summary>
        public static Vector3 InwardNormal(MRUKRoom room, MRUKAnchor anchor)
        {
            return Inward(Center(room), anchor.GetAnchorCenter(), anchor.transform.forward);
        }

        public static Vector3 Inward(Vector3 roomCenter, Vector3 point, Vector3 normal)
        {
            normal.y = 0f;
            if (normal.sqrMagnitude < 1e-4f)
            {
                normal = roomCenter - point;
                normal.y = 0f;
            }

            normal.Normalize();
            var toCenter = roomCenter - point;
            toCenter.y = 0f;
            return Vector3.Dot(toCenter, normal) < 0f ? -normal : normal;
        }

        public static List<MRUKAnchor> AnchorsWith(MRUKRoom room, MRUKAnchor.SceneLabels labels)
        {
            var result = new List<MRUKAnchor>();
            foreach (var anchor in room.Anchors)
            {
                if (anchor != null && anchor.HasAnyLabel(labels))
                {
                    result.Add(anchor);
                }
            }

            return result;
        }

        /// <summary>
        /// World-space axis-aligned bounds of a volume anchor (furniture). Falls back to a small
        /// box around the anchor centre for plane-only anchors.
        /// </summary>
        public static Bounds WorldBounds(MRUKAnchor anchor)
        {
            if (!anchor.VolumeBounds.HasValue)
            {
                return new Bounds(anchor.GetAnchorCenter(), Vector3.one * 0.3f);
            }

            var local = anchor.VolumeBounds.Value;
            var t = anchor.transform;
            var bounds = new Bounds(t.TransformPoint(local.center), Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                bounds.Encapsulate(t.TransformPoint(corner));
            }

            return bounds;
        }

        /// <summary>Centre of the top face of a furniture anchor.</summary>
        public static Vector3 TopCenter(MRUKAnchor anchor)
        {
            var b = WorldBounds(anchor);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>The floor-level room-bounds corner farthest from <paramref name="from"/>, pulled inward.</summary>
        public static Vector3 FarthestCorner(MRUKRoom room, Vector3 from, float floorY, float inset)
        {
            var b = room.GetRoomBounds();
            var best = b.center;
            var bestDistance = -1f;
            for (var i = 0; i < 4; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, floorY, (i & 2) == 0 ? b.min.z : b.max.z);
                var d = FlatDistance(corner, from);
                if (d > bestDistance)
                {
                    bestDistance = d;
                    best = corner;
                }
            }

            var toCenter = new Vector3(b.center.x, floorY, b.center.z) - best;
            return best + toCenter.normalized * Mathf.Min(inset, toCenter.magnitude * 0.5f);
        }
    }
}
