using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Collections;

public class MiningManager : MonoBehaviour
{
    [Header("Tilemap References")]
    public Tilemap mineableTilemap; // Drag your mineable tilemap here
    public Camera mainCamera; // Drag your main camera here
    
    [Header("Block Data")]
    public List<BlockData> blockDataList = new List<BlockData>(); // Add all your BlockData assets here
    
    [Header("Mining Settings")]
    public float miningRange = 3f; // How far player can mine from
    public Transform playerTransform; // Drag your player here
    
    [Header("Isometric Settings")]
    public Vector2 mouseDetectionOffset = Vector2.zero; // Adjust if detection feels off
    
    [Header("Visual Feedback")]
    public GameObject miningProgressPrefab; // We'll create this next - leave empty for now
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    
    // Private variables
    private Dictionary<TileBase, BlockData> tileToBlockData = new Dictionary<TileBase, BlockData>();
    private Vector3Int currentHoveredTile;
    private Vector3Int lastHighlightedTile;
    private bool isHighlighting = false;
    private Coroutine miningCoroutine;
    private GameObject currentProgressBar;
    private Vector3 lastMouseWorldPos; // For debug visualization
    
    private void Start()
    {
        // Build lookup dictionary
        foreach (var blockData in blockDataList)
        {
            if (blockData.blockTile != null)
            {
                tileToBlockData[blockData.blockTile] = blockData;
            }
        }
        
        if (mainCamera == null)
            mainCamera = Camera.main;
    }
    
    private void Update()
    {
        HandleTileHighlighting();
        HandleMining();
    }
    
    private void HandleTileHighlighting()
    {
        // Get mouse position using new Input System
        if (Mouse.current == null) return;
        
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, 10f));
        lastMouseWorldPos = mouseWorldPos; // Store for debug
        
        // Apply isometric offset to improve tile detection
        Vector3 detectionPoint = mouseWorldPos + (Vector3)mouseDetectionOffset;
        
        // Convert world position to tile position
        Vector3Int tilePos = mineableTilemap.WorldToCell(detectionPoint);
        
        // Check if we're hovering over a different tile
        if (tilePos != currentHoveredTile)
        {
            currentHoveredTile = tilePos;
            
            // Remove previous highlight
            if (isHighlighting)
            {
                RemoveHighlight();
            }
            
            // Check if this tile exists and is mineable
            TileBase tile = mineableTilemap.GetTile(tilePos);
            if (tile != null && tileToBlockData.ContainsKey(tile))
            {
                // Check if within mining range
                Vector3 tileWorldPos = mineableTilemap.CellToWorld(tilePos) + new Vector3(mineableTilemap.cellSize.x / 2, mineableTilemap.cellSize.y / 2, 0);
                float distance = Vector2.Distance(playerTransform.position, tileWorldPos);
                
                if (distance <= miningRange)
                {
                    ApplyHighlight(tilePos);
                }
            }
        }
    }
    
    private void HandleMining()
    {
        if (Mouse.current == null) return;
        
        // Start mining on mouse down
        if (Mouse.current.leftButton.wasPressedThisFrame && isHighlighting)
        {
            TileBase tile = mineableTilemap.GetTile(currentHoveredTile);
            if (tile != null && tileToBlockData.TryGetValue(tile, out BlockData blockData))
            {
                if (miningCoroutine != null)
                    StopCoroutine(miningCoroutine);
                
                miningCoroutine = StartCoroutine(MineBlock(currentHoveredTile, blockData));
            }
        }
        
        // Stop mining on mouse up
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            if (miningCoroutine != null)
            {
                StopCoroutine(miningCoroutine);
                miningCoroutine = null;
            }
            
            // if (currentProgressBar != null)
            // {
            //     Destroy(currentProgressBar);
            // }
        }
    }
    
    private IEnumerator MineBlock(Vector3Int tilePos, BlockData blockData)
    {
        float elapsedTime = 0f;
        
        // Create progress bar above player's head
        // if (miningProgressPrefab != null)
        // {
        //     currentProgressBar = Instantiate(miningProgressPrefab, playerTransform.position + Vector3.up * 1.5f, Quaternion.identity);
        //     currentProgressBar.transform.SetParent(playerTransform);
        // }
        
        while (elapsedTime < blockData.miningTime)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / blockData.miningTime;
            
            // Update progress bar (we'll implement this component next)
            // if (currentProgressBar != null)
            // {
            //     var progressComponent = currentProgressBar.GetComponent<MiningProgressBar>();
            //     if (progressComponent != null)
            //         progressComponent.SetProgress(progress);
            // }
            
            // Check if still in range and hovering same tile
            Vector3 tileWorldPos = mineableTilemap.CellToWorld(tilePos) + mineableTilemap.cellSize / 2;
            float distance = Vector2.Distance(playerTransform.position, tileWorldPos);
            
            if (distance > miningRange || currentHoveredTile != tilePos || !Mouse.current.leftButton.isPressed)
            {
                // Destroy(currentProgressBar);
                yield break;
            }
            
            yield return null;
        }
        
        // Mining complete - spawn drops
        SpawnDrops(tilePos, blockData);
        
        // Remove the tile
        mineableTilemap.SetTile(tilePos, null);
        
        // Clean up
        // Destroy(currentProgressBar);
        RemoveHighlight();
    }
    
    private void SpawnDrops(Vector3Int tilePos, BlockData blockData)
    {
        if (blockData.dropPrefab == null) return;
        
        Vector3 worldPos = mineableTilemap.CellToWorld(tilePos) + mineableTilemap.cellSize / 2;
        // Adjust Z position to ensure drops appear in front
        worldPos.z = -1f; // Bring drops forward in isometric view
        
        int dropCount = blockData.GetRandomDropAmount();
        
        for (int i = 0; i < dropCount; i++)
        {
            Instantiate(blockData.dropPrefab, worldPos, Quaternion.identity);
        }
    }
    
    private void ApplyHighlight(Vector3Int tilePos)
    {
        mineableTilemap.SetTileFlags(tilePos, TileFlags.None);
        mineableTilemap.SetColor(tilePos, new Color(1.2f, 1.2f, 1.2f, 1f));
        lastHighlightedTile = tilePos;
        isHighlighting = true;
    }
    
    private void RemoveHighlight()
    {
        if (isHighlighting)
        {
            mineableTilemap.SetColor(lastHighlightedTile, Color.white);
            isHighlighting = false;
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        if (playerTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(playerTransform.position, miningRange);
        }
    }
    
    private void OnDrawGizmos()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        
        // Show mouse world position
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(lastMouseWorldPos, 0.1f);
        
        // Show the currently hovered tile
        if (mineableTilemap != null && currentHoveredTile.x != int.MinValue)
        {
            Gizmos.color = Color.cyan;
            Vector3 tileCenter = mineableTilemap.CellToWorld(currentHoveredTile) + new Vector3(mineableTilemap.cellSize.x / 2, mineableTilemap.cellSize.y / 2, 0);
            Gizmos.DrawWireCube(tileCenter, mineableTilemap.cellSize);
        }
    }
}