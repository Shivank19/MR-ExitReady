// Copyright (c) MR-ExitReady contributors.

using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MRExitReady.Drill
{
    /// <summary>
    /// Draws an <see cref="MRUKRoom"/> as a simple furnished room: plaster walls with a skirting
    /// board, a wooden floor, door leaves in the door frames, glass in the windows and boxes for
    /// the furniture. Used for MRUK's sample room (nothing in it matches the real space, so
    /// without this the user sees fire on furniture that isn't there). No ceiling, so the smoke
    /// layer stays readable.
    /// </summary>
    public class RoomVisuals : MonoBehaviour
    {
        private const float WallThickness = 0.04f;

        public MRUKRoom Room { get; private set; }

        public static RoomVisuals Create(Transform parent, ProceduralAssets assets, MRUKRoom room)
        {
            var go = new GameObject("RoomVisuals");
            go.transform.SetParent(parent, false);
            var visuals = go.AddComponent<RoomVisuals>();
            visuals.Room = room;
            visuals.Build(assets);
            return visuals;
        }

        private void Build(ProceduralAssets assets)
        {
            var room = Room;
            var floorY = RoomGeometry.FloorY(room);
            int walls = 0, doors = 0, windows = 0, furniture = 0;

            foreach (var anchor in room.Anchors)
            {
                if (anchor == null)
                {
                    continue;
                }

                if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE))
                {
                    BuildWall(assets, room, anchor, floorY);
                    walls++;
                }
                else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR))
                {
                    BuildFloor(assets, anchor);
                }
                else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME))
                {
                    BuildDoor(assets, room, anchor);
                    doors++;
                }
                else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME))
                {
                    BuildWindow(assets, room, anchor);
                    windows++;
                }
                else if (anchor.VolumeBounds.HasValue)
                {
                    BuildFurniture(assets, anchor);
                    furniture++;
                }
            }

            DrillLog.Info($"Room visuals: {walls} walls, {doors} doors, {windows} windows, {furniture} furniture.");
        }

        // ── Pieces ─────────────────────────────────────────────────────────────

        private void BuildWall(ProceduralAssets assets, MRUKRoom room, MRUKAnchor anchor, float floorY)
        {
            if (!anchor.PlaneRect.HasValue)
            {
                return;
            }

            var size = anchor.PlaneRect.Value.size;
            var inward = RoomGeometry.InwardNormal(room, anchor);
            var center = anchor.GetAnchorCenter();
            var rotation = Quaternion.LookRotation(inward, Vector3.up);

            // The slab sits behind the scanned plane so its room-facing side is on the plane.
            Slab("Wall", center - inward * (WallThickness * 0.5f), rotation,
                new Vector3(size.x, size.y, WallThickness),
                assets.Textured(assets.Plaster, new Color(0.86f, 0.84f, 0.8f), new Vector2(size.x / 1.5f, size.y / 1.5f)));

            // Skirting board along the bottom.
            var bottom = new Vector3(center.x, floorY + 0.05f, center.z);
            Slab("Skirting", bottom + inward * 0.008f, rotation, new Vector3(size.x, 0.1f, 0.016f),
                assets.Prop(new Color(0.93f, 0.92f, 0.9f)));
        }

        private void BuildFloor(ProceduralAssets assets, MRUKAnchor anchor)
        {
            if (!anchor.PlaneRect.HasValue)
            {
                return;
            }

            var rect = anchor.PlaneRect.Value;
            var center = anchor.GetAnchorCenter();

            // Align the boards with the floor plane's own axes.
            var forward = Vector3.ProjectOnPlane(anchor.transform.up, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.ProjectOnPlane(anchor.transform.forward, Vector3.up);
            }

            var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Slab("Floor", center - Vector3.up * 0.011f, rotation, new Vector3(rect.width, 0.02f, rect.height),
                assets.Textured(assets.Wood, Color.white, new Vector2(rect.width / 1.6f, rect.height / 1.6f)));
        }

        private void BuildDoor(ProceduralAssets assets, MRUKRoom room, MRUKAnchor anchor)
        {
            var size = anchor.PlaneRect.HasValue ? anchor.PlaneRect.Value.size : new Vector2(0.9f, 2f);
            var inward = RoomGeometry.InwardNormal(room, anchor);
            var center = anchor.GetAnchorCenter();
            var rotation = Quaternion.LookRotation(inward, Vector3.up);

            // Frame (architrave) and a slightly recessed door leaf with a handle.
            var frameColor = new Color(0.93f, 0.92f, 0.9f);
            var leaf = Slab("DoorLeaf", center + inward * 0.006f, rotation, new Vector3(size.x - 0.06f, size.y - 0.03f, 0.03f),
                assets.Textured(assets.Wood, new Color(0.75f, 0.6f, 0.45f), new Vector2(0.5f, 1.2f)));
            var t = leaf.transform;
            var hw = size.x * 0.5f;
            var hh = size.y * 0.5f;
            Slab("FrameL", center + inward * 0.012f - t.right * hw, rotation, new Vector3(0.07f, size.y + 0.07f, 0.03f),
                assets.Prop(frameColor));
            Slab("FrameR", center + inward * 0.012f + t.right * hw, rotation, new Vector3(0.07f, size.y + 0.07f, 0.03f),
                assets.Prop(frameColor));
            Slab("FrameTop", center + inward * 0.012f + Vector3.up * hh, rotation, new Vector3(size.x + 0.07f, 0.07f, 0.03f),
                assets.Prop(frameColor));
            Slab("Handle", center + inward * 0.04f + t.right * (hw - 0.12f) - Vector3.up * 0.05f, rotation,
                new Vector3(0.12f, 0.025f, 0.03f), assets.Prop(new Color(0.75f, 0.72f, 0.65f)));
        }

        private void BuildWindow(ProceduralAssets assets, MRUKRoom room, MRUKAnchor anchor)
        {
            if (!anchor.PlaneRect.HasValue)
            {
                return;
            }

            var size = anchor.PlaneRect.Value.size;
            var inward = RoomGeometry.InwardNormal(room, anchor);
            var center = anchor.GetAnchorCenter();
            var rotation = Quaternion.LookRotation(inward, Vector3.up);
            Slab("Window", center + inward * 0.005f, rotation, new Vector3(size.x, size.y, 0.01f),
                assets.Prop(new Color(0.55f, 0.72f, 0.85f)));
            var frame = assets.Prop(new Color(0.95f, 0.95f, 0.95f));
            Slab("Mullion", center + inward * 0.012f, rotation, new Vector3(0.04f, size.y, 0.02f), frame);
            Slab("Transom", center + inward * 0.012f, rotation, new Vector3(size.x, 0.04f, 0.02f), frame);
        }

        private void BuildFurniture(ProceduralAssets assets, MRUKAnchor anchor)
        {
            var bounds = anchor.VolumeBounds.Value;
            var t = anchor.transform;
            var label = anchor.Label;

            // The anchor's local axes aren't guaranteed to have Y up, so find the vertical one
            // and build an upright box: (width, height, depth) with a yaw-only rotation.
            var axes = new[] { t.right, t.up, t.forward };
            var lengths = Vector3.Scale(bounds.size, t.lossyScale);
            var v = 0;
            for (var i = 1; i < 3; i++)
            {
                if (Mathf.Abs(Vector3.Dot(axes[i], Vector3.up)) > Mathf.Abs(Vector3.Dot(axes[v], Vector3.up)))
                {
                    v = i;
                }
            }

            var h1 = (v + 1) % 3;
            var h2 = (v + 2) % 3;
            var depthDir = Vector3.ProjectOnPlane(axes[h2], Vector3.up);
            if (depthDir.sqrMagnitude < 1e-4f)
            {
                depthDir = Vector3.forward;
            }

            var rotation = Quaternion.LookRotation(depthDir.normalized, Vector3.up);
            var size = new Vector3(lengths[h1], lengths[v], lengths[h2]);
            var center = t.TransformPoint(bounds.center);

            var (color, texture) = FurnitureLook(assets, label);
            var material = texture != null
                ? assets.Textured(texture, color, new Vector2(Mathf.Max(1f, size.x / 0.5f), Mathf.Max(1f, size.y / 0.5f)))
                : assets.Prop(color);

            if ((label & MRUKAnchor.SceneLabels.TABLE) != 0 && size.y > 0.3f)
            {
                // Table top plus four legs instead of a solid block.
                var top = center + Vector3.up * (size.y * 0.5f - 0.02f);
                Slab("TableTop", top, rotation, new Vector3(size.x, 0.04f, size.z), material);
                var legMat = assets.Prop(color * 0.8f);
                for (var i = 0; i < 4; i++)
                {
                    var local = new Vector3(((i & 1) == 0 ? -0.45f : 0.45f) * size.x, 0f,
                        ((i & 2) == 0 ? -0.45f : 0.45f) * size.z);
                    Slab("Leg", center + rotation * local, rotation, new Vector3(0.05f, size.y, 0.05f), legMat);
                }

                return;
            }

            Slab(label.ToString(), center, rotation, size, material);
        }

        private static (Color, Texture2D) FurnitureLook(ProceduralAssets assets, MRUKAnchor.SceneLabels label)
        {
            if ((label & MRUKAnchor.SceneLabels.COUCH) != 0) return (new Color(0.32f, 0.42f, 0.5f), assets.Fabric);
            if ((label & MRUKAnchor.SceneLabels.BED) != 0) return (new Color(0.85f, 0.85f, 0.88f), assets.Fabric);
            if ((label & MRUKAnchor.SceneLabels.TABLE) != 0) return (new Color(0.8f, 0.65f, 0.48f), assets.Wood);
            if ((label & MRUKAnchor.SceneLabels.STORAGE) != 0) return (new Color(0.9f, 0.88f, 0.84f), assets.Wood);
            if ((label & MRUKAnchor.SceneLabels.SCREEN) != 0) return (new Color(0.08f, 0.08f, 0.1f), null);
            if ((label & MRUKAnchor.SceneLabels.LAMP) != 0) return (new Color(0.95f, 0.9f, 0.7f), null);
            if ((label & MRUKAnchor.SceneLabels.PLANT) != 0) return (new Color(0.25f, 0.55f, 0.3f), null);
            return (new Color(0.6f, 0.6f, 0.62f), null);
        }

        /// <summary>A box with no collider, placed in world space.</summary>
        private GameObject Slab(string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = size;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }
    }
}
