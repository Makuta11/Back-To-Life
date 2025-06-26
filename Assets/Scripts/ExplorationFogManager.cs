using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Collections;

public class ExplorationFogManager : MonoBehaviour
{
    [Header("Tilemap References")]
    public Tilemap fogTilemap;           // The fog tilemap layer
    public Tilemap mineableTilemap;      // Reference to your main tilemap
    public TileBase fogTile;             // The fog tile to use (solid black sprite)
    
    [Header("Discovery Settings")]
    public Transform playerTransform;     // Reference to player
    public float discoveryRadius = 4.5f;  // Radius in tiles to discover
    public float fadeSpeed = 2f;          // How fast fog fades out
    
    [Header("Visual Settings")]
    public Color fogColor = Color.black;  // Fog color (can adjust alpha)
    public float updateInterval = 0.1f;   // How often to check for new discoveries
    
    // Discovered tiles tracking
    private Dictionary<Vector3Int, bool> discoveredTiles = new Dictionary<Vector3Int, bool>();
    private Dictionary<Vector3Int, Coroutine> fadeCoroutines = new Dictionary<Vector3Int, Coroutine>();
    
    // Performance optimization
    private Vector3 lastPlayerPosition;
    private float updateTimer = 0f;
    private HashSet<Vector3Int> tilesBeingProcessed = new HashSet<Vector3Int>();
    
    // Chunk management for future procedural generation
    private const int CHUNK_SIZE = 32;
    private Dictionary<Vector2Int, HashSet<Vector3Int>> discoveredChunks = new Dictionary<Vector2Int, HashSet<Vector3Int>>();
    
    private void Start()
    {
        if (fogTilemap == null)
        {
            Debug.LogError("Fog Tilemap not assigned!");
            return;
        }
        
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerTransform = player.transform;
        }
        
        // Initialize fog over the current world
        InitializeFog();
        
        // Reveal starting area
        if (playerTransform != null)
        {
            lastPlayerPosition = playerTransform.position;
            RevealArea(playerTransform.position);
        }
    }
    
    private void Update()
    {
        if (playerTransform == null) return;
        
        updateTimer += Time.deltaTime;
        
        // Only update if enough time has passed
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            
            // Check if player has moved
            if (Vector3.Distance(playerTransform.position, lastPlayerPosition) > 0.1f)
            {
                RevealArea(playerTransform.position);
                lastPlayerPosition = playerTransform.position;
            }
        }
    }
    
    // Initialize fog over all existing tiles
    private void InitializeFog()
    {
        if (mineableTilemap == null || fogTile == null) return;
        
        // Get bounds of the mineable tilemap
        BoundsInt bounds = mineableTilemap.cellBounds;
        
        // Place fog tiles everywhere there's a mineable tile
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                if (mineableTilemap.HasTile(pos))
                {
                    fogTilemap.SetTile(pos, fogTile);
                    fogTilemap.SetTileFlags(pos, TileFlags.None);
                    fogTilemap.SetColor(pos, fogColor);
                }
            }
        }
    }
    
    // Reveal area around a world position
    private void RevealArea(Vector3 worldPosition)
    {
        Vector3Int centerTile = mineableTilemap.WorldToCell(worldPosition);
        
        // Calculate tiles to reveal in a circle
        int radiusInTiles = Mathf.CeilToInt(discoveryRadius);
        
        for (int x = -radiusInTiles; x <= radiusInTiles; x++)
        {
            for (int y = -radiusInTiles; y <= radiusInTiles; y++)
            {
                Vector3Int tilePos = centerTile + new Vector3Int(x, y, 0);
                
                // Check if tile is within circular radius
                float distance = Vector2.Distance(
                    new Vector2(centerTile.x, centerTile.y),
                    new Vector2(tilePos.x, tilePos.y)
                );
                
                if (distance <= discoveryRadius)
                {
                    DiscoverTile(tilePos);
                }
            }
        }
    }
    
    // Discover a single tile
    private void DiscoverTile(Vector3Int tilePos)
    {
        // Skip if already discovered or being processed
        if (discoveredTiles.ContainsKey(tilePos) && discoveredTiles[tilePos]) return;
        if (tilesBeingProcessed.Contains(tilePos)) return;
        
        // Check if there's actually a fog tile here
        if (!fogTilemap.HasTile(tilePos)) return;
        
        // Mark as discovered
        discoveredTiles[tilePos] = true;
        tilesBeingProcessed.Add(tilePos);
        
        // Track in chunk system for future save/load
        Vector2Int chunkPos = GetChunkPosition(tilePos);
        if (!discoveredChunks.ContainsKey(chunkPos))
            discoveredChunks[chunkPos] = new HashSet<Vector3Int>();
        discoveredChunks[chunkPos].Add(tilePos);
        
        // Start fade out effect
        if (fadeCoroutines.ContainsKey(tilePos))
        {
            StopCoroutine(fadeCoroutines[tilePos]);
        }
        fadeCoroutines[tilePos] = StartCoroutine(FadeFogTile(tilePos));
    }
    
    // Fade out a fog tile
    private IEnumerator FadeFogTile(Vector3Int tilePos)
    {
        Color currentColor = fogTilemap.GetColor(tilePos);
        float startAlpha = currentColor.a;
        float elapsedTime = 0f;
        
        while (elapsedTime < 1f / fadeSpeed)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime * fadeSpeed;
            
            // Smooth fade using ease-out curve
            float alpha = Mathf.Lerp(startAlpha, 0f, t * t);
            currentColor.a = alpha;
            
            fogTilemap.SetColor(tilePos, currentColor);
            
            yield return null;
        }
        
        // Remove the fog tile completely
        fogTilemap.SetTile(tilePos, null);
        
        // Cleanup
        tilesBeingProcessed.Remove(tilePos);
        fadeCoroutines.Remove(tilePos);
    }
    
    // Helper method to get chunk position for a tile
    private Vector2Int GetChunkPosition(Vector3Int tilePos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(tilePos.x / (float)CHUNK_SIZE),
            Mathf.FloorToInt(tilePos.y / (float)CHUNK_SIZE)
        );
    }
    
    // Check if a tile is discovered (for integration with MiningManager)
    public bool IsTileDiscovered(Vector3Int tilePos)
    {
        return discoveredTiles.ContainsKey(tilePos) && discoveredTiles[tilePos];
    }
    
    // Force reveal an area (useful for torches or special events)
    public void ForceRevealArea(Vector3 worldPosition, float radius)
    {
        float oldRadius = discoveryRadius;
        discoveryRadius = radius;
        RevealArea(worldPosition);
        discoveryRadius = oldRadius;
    }
    
    // Save/Load methods for future implementation
    public DiscoveryData GetSaveData()
    {
        DiscoveryData data = new DiscoveryData();
        data.discoveredPositions = new List<Vector3Int>(discoveredTiles.Keys);
        return data;
    }
    
    public void LoadSaveData(DiscoveryData data)
    {
        if (data == null || data.discoveredPositions == null) return;
        
        // Clear current state
        discoveredTiles.Clear();
        discoveredChunks.Clear();
        
        // Restore discovered tiles
        foreach (var pos in data.discoveredPositions)
        {
            discoveredTiles[pos] = true;
            
            // Remove fog instantly for loaded tiles
            if (fogTilemap.HasTile(pos))
                fogTilemap.SetTile(pos, null);
            
            // Track in chunks
            Vector2Int chunkPos = GetChunkPosition(pos);
            if (!discoveredChunks.ContainsKey(chunkPos))
                discoveredChunks[chunkPos] = new HashSet<Vector3Int>();
            discoveredChunks[chunkPos].Add(pos);
        }
    }
    
    // Called when new chunks are generated (for procedural generation)
    public void OnChunkGenerated(Vector2Int chunkPosition, Tilemap generatedTilemap)
    {
        // Add fog to all tiles in the new chunk
        Vector3Int chunkWorldPos = new Vector3Int(chunkPosition.x * CHUNK_SIZE, chunkPosition.y * CHUNK_SIZE, 0);
        
        for (int x = 0; x < CHUNK_SIZE; x++)
        {
            for (int y = 0; y < CHUNK_SIZE; y++)
            {
                Vector3Int tilePos = chunkWorldPos + new Vector3Int(x, y, 0);
                
                if (generatedTilemap.HasTile(tilePos))
                {
                    // Only add fog if tile isn't already discovered
                    if (!IsTileDiscovered(tilePos))
                    {
                        fogTilemap.SetTile(tilePos, fogTile);
                        fogTilemap.SetTileFlags(tilePos, TileFlags.None);
                        fogTilemap.SetColor(tilePos, fogColor);
                    }
                }
            }
        }
    }
}

// Serializable class for save data
[System.Serializable]
public class DiscoveryData
{
    public List<Vector3Int> discoveredPositions;
}