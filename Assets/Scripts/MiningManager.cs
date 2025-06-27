using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Collections;

// MiningManager: Handles highlighting, mining, and hover-detection for isometric hex/cube tiles
public class MiningManager : MonoBehaviour
{
    [Header("Tilemap References")]
    public Tilemap mineableTilemap;  // Tilemap to mine from
    public Camera mainCamera;        // Main camera (assigned or will auto-find)
    private ExplorationFogManager fogManager;

    [Header("Block Data")]
    public List<BlockData> blockDataList = new List<BlockData>(); // All mineable blocks

    [Header("Mining Settings")]
    public float miningRange = 3f;       // Player mining distance
    public Transform playerTransform;    // Reference to player position
    private float isometricHorizontalOffset = -0.5f; // Must be -0.5 for correct alignment (set in Inspector)
    private float isometricVerticalOffset = 0f;      // Must be 0 for correct alignment (set in Inspector)

    [Header("Visual Feedback")]
    public GameObject miningProgressPrefab; // Progress bar prefab (optional)

    // Private/internal
    private Dictionary<TileBase, BlockData> tileToBlockData = new Dictionary<TileBase, BlockData>();
    private Vector3Int currentHoveredTile;
    private Vector3Int lastHighlightedTile;
    private bool isHighlighting = false;
    private Coroutine miningCoroutine;

    // Cache for mineable check results
    private Dictionary<Vector3Int, bool> mineableCache = new Dictionary<Vector3Int, bool>();
    private Dictionary<Vector3Int, List<bool>> visibilityCache = new Dictionary<Vector3Int, List<bool>>(); // For debug drawing
    private Vector3 lastPlayerPosition;
    private float cacheInvalidateDistance = 0.1f; // Invalidate cache if player moves this much

    // Mining animation cache
    private Dictionary<Sprite, Tile> spriteTileCache = new Dictionary<Sprite, Tile>();

    // Mining state tracking for guaranteed reset
    private Vector3Int currentMiningTile = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
    private TileBase originalMiningTile = null;

    // Initializes the tile/block lookup for mining
    private void Start()
    {
        foreach (var blockData in blockDataList)
        {
            if (blockData.blockTile != null)
                tileToBlockData[blockData.blockTile] = blockData;
        }
        if (mainCamera == null)
            mainCamera = Camera.main;

        lastPlayerPosition = playerTransform.position;

        // Find the fog manager
        fogManager = Object.FindAnyObjectByType<ExplorationFogManager>();
        if (fogManager == null)
        {
            Debug.LogWarning("ExplorationFogManager not found! Fog of war checks will be disabled.");
        }
    }

    private void Update()
    {
        // Check if player moved significantly to invalidate cache
        if (Vector3.Distance(playerTransform.position, lastPlayerPosition) > cacheInvalidateDistance)
        {
            mineableCache.Clear();
            visibilityCache.Clear();
            lastPlayerPosition = playerTransform.position;
        }
        
        HandleTileHighlighting();
        HandleMining();
    }

    // Get the 4 edge midpoints of the diamond-shaped tile in isometric view
    private Vector2[] GetDiamondEdgePoints(Vector3Int blockPos)
    {
        Vector3 blockWorldPos = mineableTilemap.CellToWorld(blockPos);
        Vector2 center = new Vector2(blockWorldPos.x, blockWorldPos.y) + 
                new Vector2(isometricHorizontalOffset, isometricVerticalOffset) + 
                new Vector2(mineableTilemap.cellSize.x / 2, mineableTilemap.cellSize.y / 2);
        
        // Diamond edge midpoints in isometric space
        // The diamond is wider than it is tall in isometric view
        float halfWidth = mineableTilemap.cellSize.x / 2;
        float halfHeight = mineableTilemap.cellSize.y / 2;
        
        Vector2[] edgePoints = new Vector2[4];
        edgePoints[0] = center + new Vector2(halfWidth/2, halfHeight/2);     // NE edge (top right)
        edgePoints[1] = center + new Vector2(halfWidth/2, -halfHeight/2);   // SE edge (bottom right)
        edgePoints[2] = center + new Vector2(-halfWidth/2, -halfHeight/2);    // SW edge (bottom left)
        edgePoints[3] = center + new Vector2(-halfWidth/2, halfHeight/2);    // NW edge (top left)
        
        return edgePoints;
    }

    // Check if a block can be mined (has line-of-sight from player to ANY edge)
    private bool IsBlockMineable(Vector3Int blockPos)
    {
        // Check fog first - can't mine what hasn't been discovered
        if (fogManager != null && !fogManager.IsTileDiscovered(blockPos))
        {
            return false;
        }

        // Check cache first
        if (mineableCache.ContainsKey(blockPos))
            return mineableCache[blockPos];
        
        Vector2 playerPos = new Vector2(playerTransform.position.x, playerTransform.position.y);
        Vector2[] edgePoints = GetDiamondEdgePoints(blockPos);
        List<bool> edgeVisibility = new List<bool>();
        bool anyEdgeVisible = false;
        
        // Check each edge point
        for (int i = 0; i < edgePoints.Length; i++)
        {
            Vector2 direction = edgePoints[i] - playerPos;
            float distance = direction.magnitude;
            
            // First check if this edge is within range
            if (distance > miningRange)
            {
                edgeVisibility.Add(false);
                continue;
            }
            
            // Perform raycast from player to edge point
            bool isVisible = true;
            RaycastHit2D[] hits = Physics2D.RaycastAll(playerPos, direction.normalized, distance * 0.99f);
            
            // Check each hit to see if it's blocking our target
            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider != null && hit.collider == mineableTilemap.GetComponent<TilemapCollider2D>())
                {
                    // Check if this hit is a different tile blocking our path
                    Vector3Int hitTilePos = mineableTilemap.WorldToCell(hit.point + direction.normalized * 0.01f);
                    
                    // If we hit a tile that's not our target, this edge is blocked
                    if (mineableTilemap.HasTile(hitTilePos) && hitTilePos != blockPos)
                    {
                        isVisible = false;
                        break;
                    }
                }
            }
            
            edgeVisibility.Add(isVisible);
            if (isVisible) anyEdgeVisible = true;
        }
        
        // Cache results
        mineableCache[blockPos] = anyEdgeVisible;
        visibilityCache[blockPos] = edgeVisibility;
        
        return anyEdgeVisible;
    }

    // Checks if a world point is within the bounds of a hexagon tile (with offsets)
    private bool IsPointInTileSprite(Vector3Int tilePos, Vector2 worldPoint)
    {
        TileBase tile = mineableTilemap.GetTile(tilePos);
        if (tile == null) return false;
        Sprite sprite = mineableTilemap.GetSprite(tilePos);
        if (sprite == null) return false;
        Vector3 tileWorldPos = mineableTilemap.CellToWorld(tilePos);

        // Offsets to align the detection hex with the visible tile
        float verticalOffset = isometricVerticalOffset * mineableTilemap.cellSize.y;
        float horizontalOffset = isometricHorizontalOffset * mineableTilemap.cellSize.x;
        Vector2 basePos = new Vector2(tileWorldPos.x, tileWorldPos.y) + new Vector2(horizontalOffset, verticalOffset);

        // Hexagon shape for tile (local cell coordinates)
        Vector2[] hexVerts = new Vector2[] {
            new Vector2(0f, 0.75f),
            new Vector2(0f, 0.25f),
            new Vector2(0.5f, 0f),
            new Vector2(1f, 0.25f),
            new Vector2(1f, 0.75f),
            new Vector2(0.5f, 1f)
        };
        for (int i = 0; i < hexVerts.Length; i++)
            hexVerts[i] += basePos;

        // Standard point-in-polygon (ray cast)
        Vector2 testPoint = worldPoint;
        bool inside = false;
        int j = hexVerts.Length - 1;
        for (int i = 0; i < hexVerts.Length; j = i++)
        {
            if (((hexVerts[i].y > testPoint.y) != (hexVerts[j].y > testPoint.y)) &&
                (testPoint.x < (hexVerts[j].x - hexVerts[i].x) * (testPoint.y - hexVerts[i].y) / (hexVerts[j].y - hexVerts[i].y) + hexVerts[i].x))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    // Gets the topmost (visually on top) tile under the mouse (to handle overlap in isometric projections)
    private Vector3Int? GetTopmostTileAtPosition(Vector2 worldPos)
    {
        Vector3Int centerTile = mineableTilemap.WorldToCell(worldPos);
        List<Vector3Int> tilesToCheck = new List<Vector3Int>();
        // Check a 5x5 tile area around the center, since hex faces can overlap
        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                Vector3Int checkPos = centerTile + new Vector3Int(x, y, 0);
                if (mineableTilemap.HasTile(checkPos))
                    tilesToCheck.Add(checkPos);
            }
        }
        // Sort by isometric render order: lower Y first, then higher X
        tilesToCheck.Sort((a, b) =>
        {
            if (a.y != b.y) return b.y.CompareTo(a.y);
            return b.x.CompareTo(a.x);
        });
        // Check tiles from closest to furthest visually
        for (int i = tilesToCheck.Count - 1; i >= 0; i--)
        {
            if (IsPointInTileSprite(tilesToCheck[i], worldPos))
                return tilesToCheck[i];
        }
        return null;
    }

    // Handles highlighting logic for hovered tiles - with mineable check
    private void HandleTileHighlighting()
    {
        if (Mouse.current == null) return;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, 0));
        mouseWorldPos.z = 0;
        
        Vector3Int? hoveredTile = GetTopmostTileAtPosition(mouseWorldPos);
        
        // Determine what tile should be highlighted (if any)
        Vector3Int? tileToHighlight = null;
        
        if (hoveredTile.HasValue)
        {
            Vector3Int tilePos = hoveredTile.Value;
            currentHoveredTile = tilePos;
            
            TileBase tile = mineableTilemap.GetTile(tilePos);
            if (tile != null && tileToBlockData.ContainsKey(tile))
            {
                // Check if block is mineable (in range and has line-of-sight to any edge)
                if (IsBlockMineable(tilePos))
                {
                    tileToHighlight = tilePos;
                }
            }
        }
        else
        {
            currentHoveredTile = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        }
        
        // Apply/remove highlight based on what should be highlighted
        if (tileToHighlight.HasValue)
        {
            if (!isHighlighting || lastHighlightedTile != tileToHighlight.Value)
            {
                RemoveHighlight(); // Remove old highlight first
                ApplyHighlight(tileToHighlight.Value);
            }
        }
        else
        {
            RemoveHighlight();
        }
    }

    // Handles mining logic: input, coroutine for progress, and cancelling on mouse release
    private void HandleMining()
    {
        if (Mouse.current == null) return;
        
        // Start mining if: mouse pressed OR (mouse held AND hovering new block AND not currently mining)
        bool shouldStartMining = false;
        
        if (Mouse.current.leftButton.wasPressedThisFrame && isHighlighting)
        {
            shouldStartMining = true;
        }
        else if (Mouse.current.leftButton.isPressed && isHighlighting && miningCoroutine == null)
        {
            // Mouse is held down, we're highlighting a block, and we're not currently mining
            shouldStartMining = true;
        }
        
        if (shouldStartMining)
        {
            TileBase tile = mineableTilemap.GetTile(currentHoveredTile);
            if (tile != null && tileToBlockData.TryGetValue(tile, out BlockData blockData))
            {
                // Double-check mineability before starting coroutine
                if (IsBlockMineable(currentHoveredTile))
                {
                    if (miningCoroutine != null)
                        StopMining(); // Properly stop any existing mining
                    miningCoroutine = StartCoroutine(MineBlock(currentHoveredTile, blockData));
                }
            }
        }
        
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            if (miningCoroutine != null)
            {
                StopMining(); // Properly stop mining with reset
            }
        }
    }

    // Stops mining and ensures tile is reset to original state
    private void StopMining()
    {
        if (miningCoroutine != null)
        {
            StopCoroutine(miningCoroutine);
            miningCoroutine = null;
        }
        
        // Reset tile to original if we were mining
        ResetMiningTile();
    }

    // Resets the currently mining tile back to its original state
    private void ResetMiningTile()
    {
        if (currentMiningTile.x != int.MinValue && originalMiningTile != null)
        {
            mineableTilemap.SetTile(currentMiningTile, originalMiningTile);
            mineableTilemap.SetTileFlags(currentMiningTile, TileFlags.LockTransform | TileFlags.LockColor);
        }
        
        // Clear mining state
        currentMiningTile = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        originalMiningTile = null;
    }

    // Coroutine: tracks mining time and checks conditions with mining animation
    private IEnumerator MineBlock(Vector3Int tilePos, BlockData blockData)
    {
        float elapsedTime = 0f;
        float lastStageProgress = -1f;
        
        // Store original tile for reset tracking
        currentMiningTile = tilePos;
        originalMiningTile = mineableTilemap.GetTile(tilePos);
        
        while (elapsedTime < blockData.miningTime)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / blockData.miningTime;
            
            // Update sprite if we've crossed a stage threshold (only if blockData supports it)
            float currentStageProgress = Mathf.Floor(progress * 4f); // 4 stages: 0, 1, 2, 3
            
            if (currentStageProgress != lastStageProgress && blockData.miningStageSprites != null)
            {
                Sprite stageSprite = blockData.GetMiningStageSprite(progress);
                if (stageSprite != null)
                {
                    // Create or get cached tile with the new sprite
                    Tile tempTile = GetOrCreateTile(stageSprite);
                    mineableTilemap.SetTile(tilePos, tempTile);
                }
                lastStageProgress = currentStageProgress;
            }
            
            // Check all conditions including mineability
            if (!IsBlockMineable(tilePos) || currentHoveredTile != tilePos || 
                !Mouse.current.leftButton.isPressed)
            {
                // Mining cancelled - reset will be handled by ResetMiningTile()
                ResetMiningTile();
                miningCoroutine = null;
                yield break;
            }
            yield return null;
        }
        
        // Mining completed successfully - clear mining state before completing
        currentMiningTile = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        originalMiningTile = null;
        
        SpawnDrops(tilePos, blockData);
        mineableTilemap.SetTile(tilePos, null);
        RemoveHighlight();
        
        // Clear cache since we modified the tilemap
        mineableCache.Clear();
        visibilityCache.Clear();
        
        // Clear the coroutine reference so we can start mining a new block
        miningCoroutine = null;
    }

    // Helper method to create or get cached tile for sprite
    private Tile GetOrCreateTile(Sprite sprite)
    {
        // Check if we already have a tile for this sprite
        if (spriteTileCache.TryGetValue(sprite, out Tile cachedTile))
        {
            return cachedTile;
        }
        
        // Create a new tile with the sprite
        Tile newTile = ScriptableObject.CreateInstance<Tile>();
        newTile.sprite = sprite;
        newTile.colliderType = Tile.ColliderType.Grid; // Keep same collider behavior
        
        // Cache it for future use
        spriteTileCache[sprite] = newTile;
        
        return newTile;
    }

    // Spawns block drops at the correct position
    private void SpawnDrops(Vector3Int tilePos, BlockData blockData)
    {
        if (blockData.dropPrefab == null) return;
        Vector3 worldPos = mineableTilemap.CellToWorld(tilePos) + mineableTilemap.cellSize / 2;
        worldPos.z = -1f;
        int dropCount = blockData.GetRandomDropAmount();
        for (int i = 0; i < dropCount; i++)
            Instantiate(blockData.dropPrefab, worldPos, Quaternion.identity);
    }

    // Changes the color of a tile to show highlight
    private void ApplyHighlight(Vector3Int tilePos)
    {
        mineableTilemap.SetTileFlags(tilePos, TileFlags.None);
        mineableTilemap.SetColor(tilePos, new Color(1.2f, 1.2f, 1.2f, 1f));
        lastHighlightedTile = tilePos;
        isHighlighting = true;
    }

    // Removes the highlight color from a tile
    private void RemoveHighlight()
    {
        if (isHighlighting)
        {
            mineableTilemap.SetColor(lastHighlightedTile, Color.white);
            isHighlighting = false;
        }
    }

    // Draws the mining range as a yellow wire sphere in the Editor (optional for tuning)
    private void OnDrawGizmosSelected()
    {
        if (playerTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(playerTransform.position, miningRange);
        }
    }
    
    // --- Draw a black outline around the selected (highlighted) tile in Scene view ---
    private void OnDrawGizmos()
    {
        if (mineableTilemap != null && isHighlighting && currentHoveredTile.x != int.MinValue)
        {
            Vector3 tileWorldPos = mineableTilemap.CellToWorld(currentHoveredTile);
            float verticalOffset = isometricVerticalOffset * mineableTilemap.cellSize.y;
            float horizontalOffset = isometricHorizontalOffset * mineableTilemap.cellSize.x;
            Vector2 basePos = new Vector2(tileWorldPos.x, tileWorldPos.y) + new Vector2(horizontalOffset, verticalOffset);
            Vector2[] hexVerts = new Vector2[] {
                new Vector2(0f, 0.75f),
                new Vector2(0f, 0.25f),
                new Vector2(0.5f, 0f),
                new Vector2(1f, 0.25f),
                new Vector2(1f, 0.75f),
                new Vector2(0.5f, 1f)
            };
            for (int i = 0; i < hexVerts.Length; i++)
                hexVerts[i] += basePos;
            Gizmos.color = Color.black;
            for (int i = 0; i < hexVerts.Length; i++)
            {
                Vector3 a = hexVerts[i];
                Vector3 b = hexVerts[(i + 1) % hexVerts.Length];
                Gizmos.DrawLine(a, b);
            }
            
            // Draw debug rays from player to all 4 diamond edge points
            if (playerTransform != null)
            {
                Vector2[] edgePoints = GetDiamondEdgePoints(currentHoveredTile);
                List<bool> visibility = visibilityCache.ContainsKey(currentHoveredTile) ? 
                    visibilityCache[currentHoveredTile] : new List<bool> { false, false, false, false };
                
                for (int i = 0; i < edgePoints.Length; i++)
                {
                    // Set color based on visibility of this specific edge
                    Gizmos.color = (i < visibility.Count && visibility[i]) ? Color.green : Color.red;
                    Gizmos.DrawLine(playerTransform.position, new Vector3(edgePoints[i].x, edgePoints[i].y, playerTransform.position.z));
                }
            }
        }
    }
}