﻿// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using Crest.Spline;
using UnityEngine;

namespace Crest
{
    /// <summary>
    /// Generates mesh suitable for rendering gerstner waves from a spline
    /// </summary>
    public static class ShapeGerstnerSplineHandling
    {
        public static bool GenerateMeshFromSpline(Spline.Spline spline, Transform transform, int subdivisions, float radius, int smoothingIterations, Vector2 customDataDefault, ref Mesh mesh)
        {
            return GenerateMeshFromSpline<SplinePointDataNone>(spline, transform, subdivisions, radius, smoothingIterations, customDataDefault, ref mesh);
        }

        public static bool GenerateMeshFromSpline<SplinePointCustomData>(Spline.Spline spline, Transform transform, int subdivisions, float radius, int smoothingIterations, Vector2 customDataDefault, ref Mesh mesh)
            where SplinePointCustomData : ISplinePointCustomData
        {
            var splinePoints = spline.GetComponentsInChildren<SplinePoint>();
            if (splinePoints.Length < 2) return false;

            var splinePointCount = splinePoints.Length;
            if (spline._closed && splinePointCount > 2)
            {
                splinePointCount++;
            }

            var points = new Vector3[(splinePointCount - 1) * 3 + 1];

            if (!SplineInterpolation.GenerateCubicSplineHull(splinePoints, points, spline._closed))
            {
                return false;
            }

            // Sample spline

            // Estimate total length of spline and use this to compute a sample count
            var lengthEst = 0f;
            for (int i = 1; i < splinePointCount; i++)
            {
                lengthEst += (splinePoints[i % splinePoints.Length].transform.position - splinePoints[i - 1].transform.position).magnitude;
            }
            lengthEst = Mathf.Max(lengthEst, 1f);

            var spacing = 16f / Mathf.Pow(2f, subdivisions + 1);
            var pointCount = Mathf.CeilToInt(lengthEst / spacing);
            pointCount = Mathf.Max(pointCount, 1);

            var sampledPtsOnSpline = new Vector3[pointCount];
            var sampledPtsOffSpline = new Vector3[pointCount];
            // First set of sample points lie on spline
            sampledPtsOnSpline[0] = points[0];

            // Default spline data - applies to all geom generation / input types
            var radiusMultiplier = new float[pointCount];
            radiusMultiplier[0] = 1f;
            if (splinePoints[0].TryGetComponent(out SplinePointData splineDataComp00))
            {
                radiusMultiplier[0] = splineDataComp00.GetData().x;
            }

            // Custom spline data - specific to this construction
            var customData = new Vector2[pointCount];
            customData[0] = customDataDefault;
            if (splinePoints[0].TryGetComponent(out SplinePointCustomData customDataComp00))
            {
                customData[0] = customDataComp00.GetData();
            }

            for (var i = 1; i < pointCount; i++)
            {
                float t = i / (float)(pointCount - 1);

                SplineInterpolation.InterpolateCubicPosition(splinePointCount, points, t, out sampledPtsOnSpline[i]);

                var tpts = t * (splinePoints.Length - 1f);
                var spidx = Mathf.FloorToInt(tpts);
                var alpha = tpts - spidx;

                // Interpolate default data
                var splineData0 = 1f;
                if (splinePoints[spidx].TryGetComponent(out SplinePointData splineDataComp0))
                {
                    splineData0 = splineDataComp0.GetData().x;
                }
                var splineData1 = 1f;
                if (splinePoints[Mathf.Min(spidx + 1, splinePoints.Length - 1)].TryGetComponent(out SplinePointData splineDataComp1))
                {
                    splineData1 = splineDataComp1.GetData().x;
                }
                radiusMultiplier[i] = Mathf.Lerp(splineData0, splineData1, Mathf.SmoothStep(0f, 1f, alpha));

                // Interpolate custom data
                var customData0 = customDataDefault;
                if (splinePoints[spidx].TryGetComponent(out SplinePointCustomData customDataComp0))
                {
                    customData0 = customDataComp0.GetData();
                }
                var customData1 = customDataDefault;
                if (splinePoints[Mathf.Min(spidx + 1, splinePoints.Length - 1)].TryGetComponent(out SplinePointCustomData customDataComp1))
                {
                    customData1 = customDataComp1.GetData();
                }
                customData[i] = Vector2.Lerp(customData0, customData1, Mathf.SmoothStep(0f, 1f, alpha));
            }

            // Second set of sample points lie off-spline - some distance to the right
            for (var i = 0; i < pointCount; i++)
            {
                var ibefore = i - 1;
                var iafter = i + 1;
                if (!spline._closed)
                {
                    // Not closed - clamp to range
                    ibefore = Mathf.Max(ibefore, 0);
                    iafter = Mathf.Min(iafter, pointCount - 1);
                }
                else
                {
                    // Closed - wrap into range
                    if (ibefore < 0) ibefore += pointCount;
                    iafter %= pointCount;
                }

                var tangent = sampledPtsOnSpline[iafter] - sampledPtsOnSpline[ibefore];
                var normal = tangent;
                normal.x = tangent.z;
                normal.z = -tangent.x;
                normal.y = 0f;
                normal = normal.normalized;
                sampledPtsOffSpline[i] = sampledPtsOnSpline[i] + normal * radius * radiusMultiplier[i];
            }
            if (spline._closed)
            {
                var midPoint = Vector3.Lerp(sampledPtsOffSpline[0], sampledPtsOffSpline[sampledPtsOffSpline.Length - 1], 0.5f);
                sampledPtsOffSpline[0] = sampledPtsOffSpline[sampledPtsOffSpline.Length - 1] = midPoint;
            }

            // Blur the second set of points to help solve overlaps or large distortions. Not perfect but helps in many cases.
            if (smoothingIterations > 0)
            {
                var scratchPoints = new Vector3[pointCount];

                // Ring buffer style access when closed spline
                if (!spline._closed)
                {
                    for (var j = 0; j < smoothingIterations; j++)
                    {
                        scratchPoints[0] = sampledPtsOffSpline[0];
                        scratchPoints[pointCount - 1] = sampledPtsOffSpline[pointCount - 1];
                        for (var i = 1; i < pointCount - 1; i++)
                        {
                            scratchPoints[i] = (sampledPtsOffSpline[i] + sampledPtsOffSpline[i + 1] + sampledPtsOffSpline[i - 1]) / 3f;
                            scratchPoints[i] = sampledPtsOnSpline[i] + (scratchPoints[i] - sampledPtsOnSpline[i]).normalized * radius * radiusMultiplier[i];
                        }
                        var tmp = sampledPtsOffSpline;
                        sampledPtsOffSpline = scratchPoints;
                        scratchPoints = tmp;
                    }
                }
                else
                {
                    for (var j = 0; j < smoothingIterations; j++)
                    {
                        for (var i = 0; i < sampledPtsOffSpline.Length; i++)
                        {
                            // Slightly odd indexing. The first and last point are the same, the indices need to wrap to either the
                            // second element (if overflow) or the penultimate element (if underflow) to ensure tension is maintained at ends.
                            var ibefore = i - 1;
                            var iafter = i + 1;

                            if (ibefore < 0) ibefore = sampledPtsOffSpline.Length - 2;
                            if (iafter >= sampledPtsOffSpline.Length) iafter = 1;

                            scratchPoints[i] = (sampledPtsOffSpline[i] + sampledPtsOffSpline[iafter] + sampledPtsOffSpline[ibefore]) / 3f;
                            scratchPoints[i] = sampledPtsOnSpline[i] + (scratchPoints[i] - sampledPtsOnSpline[i]).normalized * radius * radiusMultiplier[i];
                        }
                        var tmp = sampledPtsOffSpline;
                        sampledPtsOffSpline = scratchPoints;
                        scratchPoints = tmp;
                    }
                }
            }

            return UpdateMesh(transform, sampledPtsOnSpline, sampledPtsOffSpline, customData, spline._closed, ref mesh);
        }

        // Generates a mesh from the points sampled along the spline, and corresponding offset points. Bridges points with a ribbon of triangles.
        static bool UpdateMesh(Transform transform, Vector3[] sampledPtsOnSpline, Vector3[] sampledPtsOffSpline, Vector2[] customData, bool closed, ref Mesh mesh)
        {
            if (mesh == null)
            {
                mesh = new Mesh();
            }

            //                       \
            //               \   ___--4
            //                4--      \
            //                 \        \
            //  splinePoint1 -> 3--------3
            //                  |        |
            //                  2--------2
            //                  |        |
            //                  1--------1
            //                  |        |
            //  splinePoint0 -> 0--------0
            //
            //                  ^        ^
            // sampledPointsOnSpline   sampledPointsOffSpline
            //

            var triCount = (sampledPtsOnSpline.Length - 1) * 2;
            var indices = new int[triCount * 3];
            var vertCount = 2 * sampledPtsOnSpline.Length;
            var verts = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var uvs2 = new Vector2[vertCount];
            var uvs3 = new Vector2[vertCount];

            // This iterates over result points and emits a quad starting from the current result points (resultPts0[i0], resultPts1[i1]) to
            // the next result points. If the spline is closed, last quad bridges the last result points and the first result points.
            for (var i = 0; i < sampledPtsOnSpline.Length; i += 1)
            {
                // Vert indices:
                //
                //              2i1------2i1+1
                //               |\       |
                //               |  \     |
                //               |    \   |
                //               |      \ |
                //              2i0------2i0+1
                //               |        |
                //               ~        ~
                //               |        |
                //    splinePoint0--------|
                //

                verts[2 * i] = transform.InverseTransformPoint(sampledPtsOnSpline[i]);
                verts[2 * i + 1] = transform.InverseTransformPoint(sampledPtsOffSpline[i]);

                var axis0 = new Vector2(verts[2 * i].x - verts[2 * i + 1].x, verts[2 * i].z - verts[2 * i + 1].z).normalized;
                uvs[2 * i] = axis0;
                uvs[2 * i + 1] = axis0;

                // uvs2.x - 1-0 inverted normalized dist from shoreline
                uvs2[2 * i].x = 1f;
                uvs2[2 * i + 1].x = 0f;

                uvs3[2 * i] = customData[i];
                uvs3[2 * i + 1] = customData[i];

                // Emit two triangles
                if (i < sampledPtsOnSpline.Length - 1)
                {
                    var inext = i + 1;

                    indices[i * 6] = 2 * i;
                    indices[i * 6 + 1] = 2 * inext;
                    indices[i * 6 + 2] = 2 * i + 1;

                    indices[i * 6 + 3] = 2 * inext;
                    indices[i * 6 + 4] = 2 * inext + 1;
                    indices[i * 6 + 5] = 2 * i + 1;
                }
            }

            mesh.SetIndices(new int[] { }, MeshTopology.Triangles, 0);
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.uv2 = uvs2;
            mesh.uv3 = uvs3;
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateNormals();

            return true;
        }
    }
}
