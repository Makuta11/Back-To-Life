using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class OreDistribution
{
    public BlockData blockData;
    [Range(0f, 100f)]
    public float percentage = 5f;
    [Range(3, 10)]
    public int minVeinSize = 3;
    [Range(3, 10)]
    public int maxVeinSize = 7;
}

public class FastMapGenerator : MonoBehaviour
{
    [Header("Map Settings")]
    [SerializeField] private int mapSize = 64;
    [SerializeField] private int spawnClearRadius = 3;
    
    [Header("Cave Generation")]
    [Range(0f, 1f)]
    [SerializeField] private float initialCaveDensity = 0.45f;
    [SerializeField] private int caveSmoothingIterations = 5;
    
    [Header("Blocks")]
    [SerializeField] private BlockData stoneBlock;
    [SerializeField] private List<OreDistribution> oreTypes = new List<OreDistribution>();
    
    [Header("References")]
    [SerializeField] private Tilemap mineableTilemap;
    [SerializeField] private Transform playerTransform;
    
    // Generation data
    private bool[,] caveMap;
    private BlockData[,] blockMap;
    
    private void Start()
    {
        // Auto-find references if not set
        if (mineableTilemap == null)
            mineableTilemap = GameObject.Find("Grid/Tilemap")?.GetComponent<Tilemap>();
        if (playerTransform == null)
            playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
    }
    
    public void GenerateMap()
    {
        Stopwatch timer = Stopwatch.StartNew();
        
        // Initialize arrays
        caveMap = new bool[mapSize, mapSize];
        blockMap = new BlockData[mapSize, mapSize];
        
        // Fill with stone
        for (int x = 0; x < mapSize; x++)
            for (int y = 0; y < mapSize; y++)
                blockMap[x, y] = stoneBlock;
        
        // Clear existing tiles efficiently
        mineableTilemap.CompressBounds();
        BoundsInt bounds = mineableTilemap.cellBounds;
        List<Vector3Int> toClear = new List<Vector3Int>();
        
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                if (mineableTilemap.HasTile(pos))
                    toClear.Add(pos);
            }
        }
        
        if (toClear.Count > 0)
        {
            TileBase[] empty = new TileBase[toClear.Count];
            mineableTilemap.SetTiles(toClear.ToArray(), empty);
        }
        
        // Generate caves
        GenerateCaves();
        
        // Place ores
        PlaceOres();
        
        // Clear spawn area
        ClearSpawnArea();
        
        // Place all tiles at once
        List<Vector3Int> positions = new List<Vector3Int>();
        List<TileBase> tiles = new List<TileBase>();
        
        for (int x = 0; x < mapSize; x++)
        {
            for (int y = 0; y < mapSize; y++)
            {
                if (!caveMap[x, y])
                {
                    Vector3Int tilePos = new Vector3Int(x - mapSize / 2, y - mapSize / 2, 0);
                    positions.Add(tilePos);
                    tiles.Add(blockMap[x, y].blockTile);
                }
            }
        }
        
        mineableTilemap.SetTiles(positions.ToArray(), tiles.ToArray());
        
        // Move player to spawn
        if (playerTransform != null)
            playerTransform.position = new Vector3(0, 0, playerTransform.position.z);
        
        timer.Stop();
        Debug.Log($"Map generated in {timer.ElapsedMilliseconds}ms - {positions.Count} tiles placed");
    }
    
    private void GenerateCaves()
    {
        // Initialize random cave pattern
        System.Random rand = new System.Random();
        for (int x = 0; x < mapSize; x++)
            for (int y = 0; y < mapSize; y++)
                caveMap[x, y] = rand.NextDouble() < initialCaveDensity;
        
        // Smooth using cellular automata
        for (int iteration = 0; iteration < caveSmoothingIterations; iteration++)
        {
            bool[,] newCaveMap = new bool[mapSize, mapSize];
            
            for (int x = 0; x < mapSize; x++)
            {
                for (int y = 0; y < mapSize; y++)
                {
                    int neighborWalls = GetNeighborWallCount(x, y);
                    
                    if (neighborWalls > 4)
                        newCaveMap[x, y] = false;
                    else if (neighborWalls < 4)
                        newCaveMap[x, y] = true;
                    else
                        newCaveMap[x, y] = caveMap[x, y];
                }
            }
            
            caveMap = newCaveMap;
        }
    }
    
    private int GetNeighborWallCount(int gridX, int gridY)
    {
        int wallCount = 0;
        
        for (int x = gridX - 1; x <= gridX + 1; x++)
        {
            for (int y = gridY - 1; y <= gridY + 1; y++)
            {
                if (x >= 0 && x < mapSize && y >= 0 && y < mapSize)
                {
                    if (x != gridX || y != gridY)
                    {
                        if (!caveMap[x, y])
                            wallCount++;
                    }
                }
                else
                {
                    wallCount++;
                }
            }
        }
        
        return wallCount;
    }
    
    private void PlaceOres()
    {
        // Count solid tiles
        int solidTiles = 0;
        for (int x = 0; x < mapSize; x++)
            for (int y = 0; y < mapSize; y++)
                if (!caveMap[x, y])
                    solidTiles++;
        
        // Place each ore type
        System.Random rand = new System.Random();
        foreach (var ore in oreTypes)
        {
            int targetCount = Mathf.RoundToInt(solidTiles * (ore.percentage / 100f));
            int placed = 0;
            int attempts = 0;
            
            while (placed < targetCount && attempts < targetCount * 10)
            {
                attempts++;
                int x = rand.Next(0, mapSize);
                int y = rand.Next(0, mapSize);
                
                if (!caveMap[x, y] && blockMap[x, y] == stoneBlock)
                {
                    int veinSize = rand.Next(ore.minVeinSize, ore.maxVeinSize + 1);
                    placed += PlaceOreVein(x, y, ore.blockData, veinSize);
                }
            }
        }
    }
    
    private int PlaceOreVein(int startX, int startY, BlockData oreType, int targetSize)
    {
        List<Vector2Int> vein = new List<Vector2Int>();
        List<Vector2Int> candidates = new List<Vector2Int> { new Vector2Int(startX, startY) };
        
        while (vein.Count < targetSize && candidates.Count > 0)
        {
            int index = Random.Range(0, candidates.Count);
            Vector2Int pos = candidates[index];
            candidates.RemoveAt(index);
            
            if (pos.x >= 0 && pos.x < mapSize && pos.y >= 0 && pos.y < mapSize &&
                !caveMap[pos.x, pos.y] && blockMap[pos.x, pos.y] == stoneBlock)
            {
                blockMap[pos.x, pos.y] = oreType;
                vein.Add(pos);
                
                // Add neighbors
                Vector2Int[] neighbors = {
                    new Vector2Int(pos.x + 1, pos.y),
                    new Vector2Int(pos.x - 1, pos.y),
                    new Vector2Int(pos.x, pos.y + 1),
                    new Vector2Int(pos.x, pos.y - 1)
                };
                
                foreach (var n in neighbors)
                    if (!candidates.Contains(n) && !vein.Contains(n))
                        candidates.Add(n);
            }
        }
        
        return vein.Count;
    }
    
    private void ClearSpawnArea()
    {
        int centerX = mapSize / 2;
        int centerY = mapSize / 2;
        
        for (int x = -spawnClearRadius / 2; x <= spawnClearRadius / 2; x++)
        {
            for (int y = -spawnClearRadius / 2; y <= spawnClearRadius / 2; y++)
            {
                int mapX = centerX + x;
                int mapY = centerY + y;
                
                if (mapX >= 0 && mapX < mapSize && mapY >= 0 && mapY < mapSize)
                {
                    caveMap[mapX, mapY] = true;
                }
            }
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(FastMapGenerator))]
public class FastMapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        FastMapGenerator generator = (FastMapGenerator)target;
        
        EditorGUILayout.Space();
        
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Generate Map", GUILayout.Height(40)))
        {
            generator.GenerateMap();
        }
        GUI.backgroundColor = Color.white;
        
        if (GUILayout.Button("Clear Map", GUILayout.Height(25)))
        {
            Tilemap tilemap = GameObject.Find("Grid/Tilemap")?.GetComponent<Tilemap>();
            if (tilemap != null)
            {
                tilemap.CompressBounds();
                BoundsInt bounds = tilemap.cellBounds;
                List<Vector3Int> positions = new List<Vector3Int>();
                
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    for (int y = bounds.yMin; y < bounds.yMax; y++)
                    {
                        Vector3Int pos = new Vector3Int(x, y, 0);
                        if (tilemap.HasTile(pos))
                            positions.Add(pos);
                    }
                }
                
                if (positions.Count > 0)
                {
                    TileBase[] empty = new TileBase[positions.Count];
                    tilemap.SetTiles(positions.ToArray(), empty);
                }
                
                Debug.Log($"Cleared {positions.Count} tiles");
            }
        }
        
        EditorGUILayout.Space();
        
        // Get the mapSize value using serialization
        SerializedProperty mapSizeProp = serializedObject.FindProperty("mapSize");
        int size = mapSizeProp.intValue;
        EditorGUILayout.HelpBox($"Map will be {size}x{size} = {size * size:N0} tiles", MessageType.Info);
    }
}
#endif