using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(SmoothTrailMesh), "GenerateMesh")]
    internal static class ReuseSmoothTrailMeshPatch
    {
        private delegate void UploadTriangles(
            Mesh mesh,
            int submesh,
            IndexFormat indicesFormat,
            Array triangles,
            int trianglesArrayLength,
            int start,
            int length,
            bool calculateBounds,
            int baseVertex);

        private sealed class Buffers
        {
            public Vector3[] Vertices = Array.Empty<Vector3>();
            public Vector2[] Uvs = Array.Empty<Vector2>();
            public Color[] Colors = Array.Empty<Color>();
            public int[] Indices = Array.Empty<int>();
        }

        private static readonly FixedConditionalWeakTable<SmoothTrailMesh, Buffers> BuffersByTrail = new FixedConditionalWeakTable<SmoothTrailMesh, Buffers>();

        private static ConfigEntry<bool>? entry;
        private static UploadTriangles? uploadTriangles;
        private static bool uploadReady;

        internal static void Register(RORO plugin)
        {
            entry = plugin.Config.Bind(
                "Network",
                "Experimental Trail Mesh Buffers",
                false,
                "Experimental. SmoothTrailMesh.LateUpdate is in PreLateUpdate, the late window. "
                    + "On, that rebuild keeps buffers on the trail and uploads only the used range. "
                    + "Off runs the vanilla rebuild again. Off by default.");
            RiskOfOptionsIntegration.Hold(new RiskOfOptionsCatalog
            {
                EnableExperimentalTrailMeshBuffers = entry,
            });
        }

        private static bool Prefix(SmoothTrailMesh __instance)
        {
            if (entry == null || !entry.Value || !PrepareUpload())
            {
                return true;
            }

            Queue<SmoothTrailMesh.Point> points = __instance.pointsQueue;
            Mesh mesh = __instance.mesh;
            if (points == null || mesh == null)
            {
                return true;
            }

            int pointCount = points.Count;
            int vertexCount = pointCount * 2;
            int indexCount = (pointCount - 1) * 6;
            if (pointCount <= 0 || vertexCount <= 0 || indexCount < 0)
            {
                return true;
            }

            Buffers buffers = BuffersByTrail.GetOrCreateValue(__instance);
            if (!Grow(ref buffers.Vertices, vertexCount)
                || !Grow(ref buffers.Uvs, vertexCount)
                || !Grow(ref buffers.Colors, vertexCount)
                || (indexCount > 0 && !Grow(ref buffers.Indices, indexCount)))
            {
                return true;
            }

            Array.Clear(buffers.Colors, 0, vertexCount);
            bool fadeVertexAlpha = __instance.fadeVertexAlpha;
            float newest = Time.time;
            float span = newest - points.Peek().time;
            float invSpan = 1f / span;
            int vertex = 0;
            foreach (SmoothTrailMesh.Point point in points)
            {
                float along = (newest - point.time) * invSpan;
                buffers.Vertices[vertex] = point.vertex1;
                buffers.Uvs[vertex] = new Vector2(1f, along);
                buffers.Colors[vertex] = new Color(1f, 1f, 1f, fadeVertexAlpha ? 1f - along : 1f);
                vertex++;
                buffers.Vertices[vertex] = point.vertex2;
                buffers.Uvs[vertex] = new Vector2(0f, along);
                vertex++;
            }

            int cursor = 0;
            int vertexIndex = 0;
            int segments = pointCount - 1;
            for (int segment = 0; segment < segments; segment++)
            {
                buffers.Indices[cursor] = vertexIndex;
                buffers.Indices[cursor + 1] = vertexIndex + 1;
                buffers.Indices[cursor + 2] = vertexIndex + 2;
                buffers.Indices[cursor + 3] = vertexIndex + 3;
                buffers.Indices[cursor + 4] = vertexIndex + 1;
                buffers.Indices[cursor + 5] = vertexIndex + 2;
                cursor += 6;
                vertexIndex += 2;
            }

            int[] indices = indexCount == 0 ? Array.Empty<int>() : buffers.Indices;
            mesh.Clear();
            mesh.SetVertices(buffers.Vertices, 0, vertexCount);
            mesh.SetUVs(0, buffers.Uvs, 0, vertexCount);
            // Used length, not the buffer length. The whole array would upload the spare tail.
            uploadTriangles!(mesh, -1, IndexFormat.UInt32, indices, indices.Length, 0, indexCount, true, 0);
            mesh.SetColors(buffers.Colors, 0, vertexCount);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return false;
        }

        private static bool PrepareUpload()
        {
            if (uploadReady)
            {
                return uploadTriangles != null;
            }

            uploadReady = true;
            MethodInfo? method = AccessTools.Method(typeof(Mesh), "SetTrianglesImpl");
            if (method == null)
            {
                RORO.GlobalLogger?.LogWarning("Trail mesh buffers stay on the vanilla rebuild: SetTrianglesImpl was not found.");
                return false;
            }

            try
            {
                uploadTriangles = AccessTools.MethodDelegate<UploadTriangles>(method);
                return true;
            }
            catch (Exception exception)
            {
                RORO.GlobalLogger?.LogWarning("Trail mesh buffers stay on the vanilla rebuild: " + exception.Message);
                return false;
            }
        }

        private static bool Grow<T>(ref T[] buffer, int needed)
        {
            if (buffer.Length >= needed)
            {
                return true;
            }

            int size = buffer.Length == 0 ? 4 : buffer.Length;
            while (size < needed)
            {
                if (size > int.MaxValue / 2)
                {
                    return false;
                }

                size *= 2;
            }

            buffer = new T[size];
            return true;
        }
    }
}
