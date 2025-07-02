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
    [Header("Player Center Offset")]
    [Tooltip("Offset from player transform position for fog discovery center")]
    public Vector3 playerCenterOffset = new Vector3(0, 0, 0);

    [Header("Chamber Reveal Settings")]
    [Tooltip("Enable chamber-based reveal for caves")]
    public bool enableChamberReveal = true;
    [Tooltip("Maximum chamber size to prevent performance issues")]
    public int maxChamberSize = 500;
    [Tooltip("Delay between revealing each tile in chamber for visual effect")]
    public float chamberRevealDelay = 0.005f;

    [Header("Visual Settings")]
    public Color fogColor = Color.black;  // Fog color (can adjust alpha)
    public float updateInterval = 0.1f;   // How often to check for new discoveries
    
    // Discovered tiles tracking
    private Dictionary<Vector3Int, bool> discoveredTiles = new Dictionary<Vector3Int, bool>();
    private Dictionary<Vector3Int, Coroutine> fadeCoroutines = new Dictionary<Vector3Int, Coroutine>();
    
    // Chamber reveal tracking
    private HashSet<Vector3Int> currentlyRevealingChamber = new HashSet<Vector3Int>();
    private Coroutine chamberRevealCoroutine;
    
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
            Vector3 discoveryCenter = playerTransform.position + playerCenterOffset;
            lastPlayerPosition = discoveryCenter;
            RevealArea(discoveryCenter);
        }
    }
    
    private void Update()
    {
        if (playerTransform == null) return;
        
        updateTimer += Time.deltaTime;
        
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            
            // Apply offset to player position
            Vector3 discoveryCenter = playerTransform.position + playerCenterOffset;
            
            // Check if player has moved
            if (Vector3.Distance(discoveryCenter, lastPlayerPosition) > 0.1f)
            {
                RevealArea(discoveryCenter);
                lastPlayerPosition = discoveryCenter;
            }
        }
    }
    
    // Public method to cover entire map with fog (called by map generator)
    public void CoverMapWithFog(int mapSize)
    {
        if (fogTile == null) 
        {
            Debug.LogError("Fog tile not assigned!");
            return;
        }
        
        // Clear any existing fog first
        ClearAllFog();
        
        // Cover entire map area with fog
        int halfSize = mapSize / 2;
        List<Vector3Int> positions = new List<Vector3Int>();
        List<TileBase> tiles = new List<TileBase>();
        
        for (int x = -halfSize; x < halfSize; x++)
        {
            for (int y = -halfSize; y < halfSize; y++)
            {
                positions.Add(new Vector3Int(x, y, 0));
                tiles.Add(fogTile);
            }
        }
        
        // Batch set all fog tiles for performance
        fogTilemap.SetTiles(positions.ToArray(), tiles.ToArray());
        
        // Set fog properties
        foreach (var pos in positions)
        {
            fogTilemap.SetTileFlags(pos, TileFlags.None);
            fogTilemap.SetColor(pos, fogColor);
        }
        
        Debug.Log($"Covered {mapSize}x{mapSize} map with fog ({positions.Count} tiles)");
        
        // Clear discovered tiles tracking since this is a new map
        discoveredTiles.Clear();
        discoveredChunks.Clear();
        currentlyRevealingChamber.Clear();
        
        // Re-reveal the spawn area if player exists
        if (playerTransform != null)
        {
            RevealArea(playerTransform.position + playerCenterOffset);
        }
    }

    // Public method to cover a specific chunk with fog (for procedural generation)
    public void CoverChunkWithFog(Vector2Int chunkCoord, int chunkSize)
    {
        if (fogTile == null) return;
        
        Vector3Int chunkWorldPos = new Vector3Int(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, 0);
        List<Vector3Int> positions = new List<Vector3Int>();
        List<TileBase> tiles = new List<TileBase>();
        
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                Vector3Int tilePos = chunkWorldPos + new Vector3Int(x, y, 0);
                
                // Only add fog if tile isn't already discovered
                if (!IsTileDiscovered(tilePos))
                {
                    positions.Add(tilePos);
                    tiles.Add(fogTile);
                }
            }
        }
        
        // Batch set fog tiles
        if (positions.Count > 0)
        {
            fogTilemap.SetTiles(positions.ToArray(), tiles.ToArray());
            
            foreach (var pos in positions)
            {
                fogTilemap.SetTileFlags(pos, TileFlags.None);
                fogTilemap.SetColor(pos, fogColor);
            }
        }
        
        Debug.Log($"Added fog to chunk ({chunkCoord.x}, {chunkCoord.y}) - {positions.Count} tiles");
    }

    // Helper method to clear all fog
    private void ClearAllFog()
    {
        fogTilemap.CompressBounds();
        BoundsInt bounds = fogTilemap.cellBounds;
        List<Vector3Int> positions = new List<Vector3Int>();
        
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                if (fogTilemap.HasTile(pos))
                {
                    positions.Add(pos);
                }
            }
        }
        
        if (positions.Count > 0)
        {
            TileBase[] empty = new TileBase[positions.Count];
            fogTilemap.SetTiles(positions.ToArray(), empty);
        }
    }

    // Initialize fog over the current world
    private void InitializeFog()
    {
        // Try to determine map size from the mineable tilemap bounds
        if (mineableTilemap != null)
        {
            BoundsInt bounds = mineableTilemap.cellBounds;
            int mapSize = Mathf.Max(bounds.size.x, bounds.size.y);
            
            if (mapSize > 0)
            {
                CoverMapWithFog(mapSize);
            }
            else
            {
                // Default to 64x64 if no tiles exist yet
                CoverMapWithFog(64);
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
                    // Check if this is a cave tile and chamber reveal is enabled
                    if (enableChamberReveal && !mineableTilemap.HasTile(tilePos) && 
                        fogTilemap.HasTile(tilePos) && !IsTileDiscovered(tilePos)) 
                    {
                        // This is an undiscovered cave tile - reveal the entire chamber
                        RevealChamber(tilePos);
                    }
                    else
                    {
                        // Normal tile-by-tile reveal for solid blocks or if chamber reveal is disabled
                        DiscoverTile(tilePos);
                    }
                }
            }
        }
    }
    
    // Reveal an entire cave chamber starting from a tile
    private void RevealChamber(Vector3Int startTile)
    {
        // Don't start a new chamber reveal if one is in progress
        if (currentlyRevealingChamber.Count > 0) return;
        
        // Find all connected cave tiles using flood fill
        HashSet<Vector3Int> chamberTiles = new HashSet<Vector3Int>();
        Queue<Vector3Int> toCheck = new Queue<Vector3Int>();
        toCheck.Enqueue(startTile);
        chamberTiles.Add(startTile);
        
        while (toCheck.Count > 0 && chamberTiles.Count < maxChamberSize)
        {
            Vector3Int current = toCheck.Dequeue();
            
            // Check all 4 adjacent tiles
            Vector3Int[] neighbors = {
                current + Vector3Int.up,
                current + Vector3Int.down,
                current + Vector3Int.left,
                current + Vector3Int.right
            };
            
            foreach (var neighbor in neighbors)
            {
                // Check if this is a cave tile we haven't checked yet
                if (!chamberTiles.Contains(neighbor) && 
                    !mineableTilemap.HasTile(neighbor) && 
                    fogTilemap.HasTile(neighbor) &&
                    !IsTileDiscovered(neighbor))
                {
                    chamberTiles.Add(neighbor);
                    toCheck.Enqueue(neighbor);
                }
            }
        }
        
        // Start revealing the chamber
        if (chamberTiles.Count > 0)
        {
            currentlyRevealingChamber = chamberTiles;
            if (chamberRevealCoroutine != null)
                StopCoroutine(chamberRevealCoroutine);
            chamberRevealCoroutine = StartCoroutine(RevealChamberAnimation(chamberTiles));
            
            Debug.Log($"Revealing chamber with {chamberTiles.Count} tiles");
        }
    }
    
    // Animate the chamber reveal for visual effect
    private IEnumerator RevealChamberAnimation(HashSet<Vector3Int> chamberTiles)
    {
        // Convert to list and sort by distance from center for ripple effect
        List<Vector3Int> sortedTiles = new List<Vector3Int>(chamberTiles);
        Vector3Int center = sortedTiles[0]; // First tile is where we started
        
        sortedTiles.Sort((a, b) => 
        {
            float distA = Vector3Int.Distance(a, center);
            float distB = Vector3Int.Distance(b, center);
            return distA.CompareTo(distB);
        });
        
        // Reveal tiles in order with slight delay
        foreach (var tile in sortedTiles)
        {
            DiscoverTile(tile);
            if (chamberRevealDelay > 0)
                yield return new WaitForSeconds(chamberRevealDelay);
        }
        
        currentlyRevealingChamber.Clear();
        chamberRevealCoroutine = null;
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
    
    // Force reveal a chamber (useful for special events or items)
    public void ForceRevealChamber(Vector3 worldPosition)
    {
        Vector3Int tilePos = mineableTilemap.WorldToCell(worldPosition);
        if (!mineableTilemap.HasTile(tilePos)) // Only works on empty tiles
        {
            RevealChamber(tilePos);
        }
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

// Serializable class for save data
[System.Serializable]
public class DiscoveryData
{
    public List<Vector3Int> discoveredPositions;
}