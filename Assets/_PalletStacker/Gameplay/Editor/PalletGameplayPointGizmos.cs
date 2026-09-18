using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay.Editor
{
    public static class PalletGameplayPointGizmos
    {
        private static readonly Color PalletPointColor = new(0.1f, 0.9f, 1f, 0.9f);
        private static readonly Color DestinationPointColor = new(0.25f, 1f, 0.3f, 0.9f);

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawPalletPoints(PalletSpawner spawner, GizmoType gizmoType)
        {
            DrawPoints(
                spawner.SpawnPoints,
                PalletPointColor,
                "Pallet",
                new Vector3(1.2f, 0.08f, 1.2f));
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawDestinationPoints(
            PalletRoundController roundController,
            GizmoType gizmoType)
        {
            DrawPoints(
                GetDestinationTransforms(roundController.DestinationZones),
                DestinationPointColor,
                "Destination",
                new Vector3(2f, 0.08f, 2f));
        }

        private static IReadOnlyList<Transform> GetDestinationTransforms(
            IReadOnlyList<PalletDestinationZone> zones)
        {
            if (zones == null) return null;

            Transform[] transforms = new Transform[zones.Count];
            for (int index = 0; index < zones.Count; index++)
                transforms[index] = zones[index] != null ? zones[index].transform : null;
            return transforms;
        }

        private static void DrawPoints(
            IReadOnlyList<Transform> points,
            Color color,
            string labelPrefix,
            Vector3 footprint)
        {
            if (points == null) return;

            Color previousColor = Gizmos.color;
            Gizmos.color = color;

            for (int index = 0; index < points.Count; index++)
            {
                Transform point = points[index];
                if (point == null) continue;

                Vector3 position = point.position;
                Gizmos.matrix = Matrix4x4.TRS(position, point.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.up * footprint.y * 0.5f, footprint);
                Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.8f);

                Gizmos.matrix = Matrix4x4.identity;
                Handles.color = color;
                Handles.Label(
                    position + Vector3.up * 0.2f,
                    $"{labelPrefix} {index + 1:00}");
            }

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = previousColor;
        }
    }
}
