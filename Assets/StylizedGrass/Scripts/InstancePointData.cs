using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "Instance Point Data")]
public class InstancePointData : ScriptableObject
{
    [Serializable]
    public struct ChunkMap
    {
        public Vector3Int index;
        public int startingPointIndex;
        public int pointCount;
    }

    [Header("Settings")]
    [SerializeField] 
    Vector3Int chunkSize = Vector3Int.one * 2;
    [SerializeField] 
    public Vector3 boundsPadding = Vector3.zero;

    [Header("Debug")]
    // For purely serialization because dictionary doesn't support serialization
    [SerializeField] 
    Vector3[] serializedPoints;
    [SerializeField] 
    ChunkMap[] serializedChunkMaps;

    [SerializeField] 
    Bounds bounds = new(Vector3.zero, Vector3.one * 1000);
    public Bounds Bounds { get => bounds; }

    // Runtime instance point storage
    Dictionary<Vector3Int, List<Vector3>> chunks;
    public Dictionary<Vector3Int, List<Vector3>> Chunks { get => chunks; }

    List<Vector3Int> addedChunksCache = new List<Vector3Int>();
    Vector3Int oldChunkSize = default;

    public int TotalPointAmount { 
        get {
            if (chunks == null)
                return 0;
            
            int n = 0;

            foreach (var pointsInChunk in chunks.Values)
            {
                n += pointsInChunk.Count;
            }

            return n;
        } 
    }

    public void Initialize()
    {
        if (chunks == null)
        {
            chunks = new();

            LoadPoints();
        }
    }

    #region Serialization
    /// <summary>
    /// Load points from serializedPoints array by placing them into to chunks with the help of serializedChunkMaps.
    /// </summary>
    public void LoadPoints()
    {
        if (chunks != null)
            chunks.Clear();
        else
            chunks = new();

        List<Vector3> points;

        for (int i = 0; i < serializedChunkMaps.Length; i++)
        {
            points = serializedPoints.Skip(serializedChunkMaps[i].startingPointIndex).Take(serializedChunkMaps[i].pointCount).ToList();

            chunks.Add(serializedChunkMaps[i].index, points);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Save points from chunks into serializedPoints and generate serializedChunkMaps items to allow reversing this process.
    /// </summary>
    public void SavePoints()
    {
        // Initialize values
        if (serializedPoints == null || serializedPoints.Length != TotalPointAmount)
            serializedPoints = new Vector3[TotalPointAmount];

        if (serializedChunkMaps == null || serializedChunkMaps.Length != chunks.Keys.Count)
            serializedChunkMaps = new ChunkMap[chunks.Keys.Count];

        var keys = chunks.Keys.ToArray();
        int pointIndex = 0;

        for (int i = 0; i < keys.Length; i++)
        {
            var pointAmount = chunks[keys[i]].Count;

            serializedChunkMaps[i] = new() { 
                index = keys[i], 
                startingPointIndex = pointIndex, 
                pointCount = pointAmount 
            };

            for (int p = 0; p < pointAmount; p++)
            {
                serializedPoints[pointIndex] = chunks[keys[i]][p];

                pointIndex++;
            }
        }

        bounds = CalculateTotalBounds();

        // Save asset
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssetIfDirty(this);
    }

    private void OnEnable()
    {
        oldChunkSize = chunkSize;
    }
    
    private void OnValidate()
    {
        if (chunkSize.x <= 0)
            chunkSize.x = 1;

        if(chunkSize.y <= 0)
            chunkSize.y = 1;

        if (chunkSize.z <= 0)
            chunkSize.z = 1;

        // If chunk size was changed => reassign all points to correct chunks
        if (oldChunkSize != chunkSize)
        {
            ReassignChunks();

            oldChunkSize = chunkSize;
        }
    }

    public bool SaveNotInSync()
    {
        return serializedPoints != null && serializedPoints.Length != TotalPointAmount;
    }
#endif
    #endregion

    /// <summary>
    /// Populates <paramref name="points"/> with visible points if any points are visible.
    /// </summary>
    /// <returns>Is visible at all.</returns>
    public bool GetVisiblePoints(ref List<Vector3> points)
    {
        bool totalBoundsVisible = false;
        
        if (Camera.allCamerasCount == 0 || chunks.Count == 0 || TotalPointAmount == 0)
            return false;

        points.Clear();
        addedChunksCache.Clear();

        Plane[] frustumPlanes; 
        foreach (var camera in Camera.allCameras)
        {
            // Skip disabled cameras
            if (!camera.enabled)
                continue;

            frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);

            // Check if combined bounds are inside frustum
            if (GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                totalBoundsVisible = true;
            else
                continue;

            foreach (var chunk in chunks.Keys)
            {
                // Make sure chunk hasn't been added yet and that it is inside the frustum
                if (!addedChunksCache.Contains(chunk) && GeometryUtility.TestPlanesAABB(frustumPlanes, ChunkToBounds(chunk)))
                {
                    points.AddRange(chunks[chunk]);

                    addedChunksCache.Add(chunk);
                }
            }
        }

#if UNITY_EDITOR
        // Calculate scene camera also if it is current (= not found in Camera.allCameras)
        if (!Application.isPlaying && Camera.current && !Camera.allCameras.Contains(Camera.current))
        {
            frustumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.current);

            // Check if combined bounds are inside frustum
            if (GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
            {
                totalBoundsVisible = true;

                foreach (var chunk in chunks.Keys)
                {
                    // Make sure chunk hasn't been added yet and that it is inside the frustum
                    if (!addedChunksCache.Contains(chunk) && GeometryUtility.TestPlanesAABB(frustumPlanes, ChunkToBounds(chunk)))
                    {
                        points.AddRange(chunks[chunk]);

                        addedChunksCache.Add(chunk);
                    }
                }
            }
        }
#endif

        if (totalBoundsVisible)
            return true;
        else
            return false;
    }

    Bounds ChunkToBounds(Vector3Int chunk)
    {
        return new(chunk * chunkSize + (Vector3)chunkSize * 0.5f, chunkSize + boundsPadding);
    }

    // Helper methods for drawing, saving and gizmos
#if UNITY_EDITOR
    Vector3Int PointToChunk(Vector3 point)
    {
        return new Vector3Int(Mathf.FloorToInt(point.x / chunkSize.x), Mathf.FloorToInt(point.y / chunkSize.y), Mathf.FloorToInt(point.z / chunkSize.z));
    }

    Bounds CalculateTotalBounds()
    {
        Bounds bounds = new();

        foreach (var chunk in chunks.Keys)
        {
            bounds.Encapsulate(ChunkToBounds(chunk));
        }

        return bounds;
    }

    public void AddPointsToChunk(List<Vector3> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            AddInstancePointToChunk(points[i]);
        }
    }

    public void AddInstancePointToChunk(Vector3 point)
    {
        Vector3Int chunk = PointToChunk(point);

        if (chunks.ContainsKey(chunk))
            chunks[chunk].Add(point);
        else
            chunks[chunk] = new() { point };
    }

    /// <summary>
    /// Remove points around <paramref name="point"/> in <paramref name="brushRadius"/>.
    /// </summary>
    public void RemoveInstancePointsAroundPoint(Vector3 point, float brushRadius)
    {
        Vector3Int chunk = PointToChunk(point);

        if (!chunks.ContainsKey(chunk))
            return;

        // Go through points in chunk and it's neighbouring chunks (3x3x3 grid)
        var indexer = Vector3Int.one;
        for (int x = chunk.x - 1; x <= chunk.x + 1; x++)
        {
            for (int y = chunk.y - 1; y <= chunk.y + 1; y++)
            {
                for (int z = chunk.z - 1; z <= chunk.z + 1; z++)
                {
                    indexer.x = x;
                    indexer.y = y;
                    indexer.z = z;

                    if (chunks.ContainsKey(indexer))
                    {
                        // Delete instance points within given radius of given point
                        for (int i = 0; i < chunks[indexer].Count; i++)
                        {
                            if (Vector3.Distance(chunks[indexer][i], point) < brushRadius)
                            {
                                chunks[indexer].RemoveAt(i);

                                i--;
                            }
                        }

                        // Delete chunk if its empty
                        if (chunks[indexer].Count == 0)
                            chunks.Remove(indexer);
                    }
                }
            }
        }

    }

    /// <summary>
    /// Populate <paramref name="adjacentChunkPoints"/> with all the points from the chunk that <paramref name="point"/> is in and it's neighbours (3x3x3 grid).
    /// </summary>
    public void GetPointsAdjacentChunks(Vector3 point, ref List<Vector3> adjacentChunkPoints)
    {
        adjacentChunkPoints.Clear();

        var chunk = PointToChunk(point);
        var indexer = Vector3Int.one;

        // Get points from adjacent chunks in 3x3x3 pattern
        for (int x = chunk.x - 1; x <= chunk.x + 1; x++)
        {
            for (int y = chunk.y - 1; y <= chunk.y + 1; y++)
            {
                for (int z = chunk.z - 1; z <= chunk.z + 1; z++)
                {
                    indexer.x = x;
                    indexer.y = y;
                    indexer.z = z;

                    if (chunks.ContainsKey(indexer))
                    {
                        adjacentChunkPoints.AddRange(chunks[indexer]);
                    }
                }
            }
        }
    }

    public void GetAllPoints(ref List<Vector3> points)
    {
        points.Clear();

        foreach (var chunk in chunks.Keys)
        {
            points.AddRange(chunks[chunk]);
        }
    }

    /// <summary>
    /// Reassign all points to chunks. Useful when chunk size changes.
    /// </summary>
    private void ReassignChunks()
    {
        if (chunks == null)
            return;

        List<Vector3> points = new List<Vector3>();

        GetAllPoints(ref points);

        chunks.Clear();

        for (int i = 0; i < points.Count; i++)
        {
            AddInstancePointToChunk(points[i]);
        }

        SavePoints();
    }

    // Gizmos for chunk visualization
    public void DrawGizmos()
    {
        Bounds bounds;

        if (chunks != null && chunks.Keys != null)
            foreach (var chunk in chunks.Keys)
            {
                bounds = ChunkToBounds(chunk);

                Gizmos.DrawWireCube(bounds.center, bounds.size);

                Handles.Label(bounds.center, chunk.ToString());
            }
    }
#endif

}
