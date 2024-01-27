using System.Collections.Generic;
using UnityEngine;

public static class PoissonDiscSampling
{
    readonly static List<Vector3> pointsCache = new List<Vector3>();

    /// <summary>
    /// Generate points with Poisson Disc Sampling by testing against <paramref name="nearByPoints"/>
    /// </summary>
    /// <param name="center">Brush center</param>
    /// <param name="radius">"Grass spread"</param>
    /// <param name="numSamples"></param>
    /// <returns>Center + generated points</returns>
    public static List<Vector3> GeneratePointsInDisc(List<Vector3> nearByPoints, Vector3 center, float radius, int numSamples = 30)
    {
        pointsCache.Clear();

        pointsCache.Add(center);

        for (int i = 0; i < numSamples; i++)
        {
            float angle = Random.value * Mathf.PI * 2;
            Vector3 dir = new(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            Vector3 candidate = center + dir * Random.Range(radius, 2 * radius);
            if (IsValidInCircle(candidate, radius, nearByPoints))
            {
                pointsCache.Add(candidate);
            }
        }

        return pointsCache;
    }

    private static bool IsValidInCircle(Vector3 candidate, float radius, List<Vector3> points)
    {
        // Go through every point to check:
        // Its not too close to another point
        for (int i = 0; i < points.Count; i++)
        {
            // Check if point is too close to another point
            float sqrDst = (candidate - points[i]).sqrMagnitude;

            if (sqrDst < radius * radius)
            {
                return false;
            }
        }

        return true;
    }
}
