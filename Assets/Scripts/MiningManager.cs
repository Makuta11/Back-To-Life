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
    [Range(-3f, 3f)]
    public float isometricHorizontalOffset = 0f; // Adjust this to match your sprite rendering
    [Range(-3f, 3f)]
    public float isometricVerticalOffset = -2f; // Adjust this to match your sprite rendering
    public Vector2 mouseDetectionOffset = Vector2.zero; // Additional tweak if detection feels off

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
        // Debug layer info
        Debug.Log($"Mineable Tilemap Layer: {mineableTilemap.gameObject.layer} ({LayerMask.LayerToName(mineableTilemap.gameObject.layer)})");
        Debug.Log($"Player Layer Number: {LayerMask.NameToLayer("Player")}");
        
        // Check if tilemap has collider
        TilemapCollider2D collider = mineableTilemap.GetComponent<TilemapCollider2D>();
        Debug.Log($"Tilemap has collider: {collider != null}");
        
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

    private void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;

        GUI.Label(new Rect(10, 10, 300, 20), $"Mouse Screen Pos: {Mouse.current?.position.ReadValue()}");
        GUI.Label(new Rect(10, 30, 300, 20), $"Current Hovered Tile: {currentHoveredTile}");
        GUI.Label(new Rect(10, 50, 300, 20), $"Is Highlighting: {isHighlighting}");
    }

    private bool IsPointInTileSprite(Vector3Int tilePos, Vector2 worldPoint)
    {
        TileBase tile = mineableTilemap.GetTile(tilePos);
        if (tile == null) return false;

        Sprite sprite = mineableTilemap.GetSprite(tilePos);
        if (sprite == null) return false;

        Vector3 tileWorldPos = mineableTilemap.CellToWorld(tilePos);

        // Apply both offsets
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

        // Point-in-polygon
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

        if (showDebugInfo)
        {
            Color debugColor = inside ? Color.green : Color.gray;
            for (int i = 0; i < hexVerts.Length; i++)
            {
                Vector3 a = hexVerts[i];
                Vector3 b = hexVerts[(i + 1) % hexVerts.Length];
                Debug.DrawLine(a, b, debugColor, 0f, false);
            }
        }
        return inside;
    }


    private Vector3Int? GetTopmostTileAtPosition(Vector2 worldPos)
    {
        // Get a range of tiles that could potentially be at this position
        Vector3Int centerTile = mineableTilemap.WorldToCell(worldPos);

        List<Vector3Int> tilesToCheck = new List<Vector3Int>();

        // Check in a 3x3 area around the center (you might need to adjust this range)
        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                Vector3Int checkPos = centerTile + new Vector3Int(x, y, 0);
                if (mineableTilemap.HasTile(checkPos))
                {
                    tilesToCheck.Add(checkPos);
                }
            }
        }

        // Sort tiles by isometric rendering order (back to front)
        tilesToCheck.Sort((a, b) =>
        {
            if (a.y != b.y) return b.y.CompareTo(a.y);
            return b.x.CompareTo(a.x);
        });

        // Check tiles from front to back
        for (int i = tilesToCheck.Count - 1; i >= 0; i--)
        {
            if (IsPointInTileSprite(tilesToCheck[i], worldPos))
            {
                return tilesToCheck[i];
            }
        }

        return null;
    }

    private void HandleTileHighlighting()
    {
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, 0));
        mouseWorldPos.z = 0;

        lastMouseWorldPos = mouseWorldPos;

        // Get the topmost tile at this position
        Vector3Int? hoveredTile = GetTopmostTileAtPosition(mouseWorldPos);

        if (hoveredTile.HasValue)
        {
            Vector3Int tilePos = hoveredTile.Value;

            if (showDebugInfo)
            {
                Debug.Log($"Hovering over tile at: {tilePos}");
            }

            if (tilePos != currentHoveredTile)
            {
                currentHoveredTile = tilePos;

                if (isHighlighting)
                {
                    RemoveHighlight();
                }

                TileBase tile = mineableTilemap.GetTile(tilePos);
                if (tile != null && tileToBlockData.ContainsKey(tile))
                {
                    Vector3 tileWorldPos = mineableTilemap.CellToWorld(tilePos) + new Vector3(mineableTilemap.cellSize.x / 2, mineableTilemap.cellSize.y / 2, 0);
                    float distance = Vector2.Distance(playerTransform.position, tileWorldPos);

                    if (distance <= miningRange)
                    {
                        ApplyHighlight(tilePos);
                    }
                }
            }
        }
        else
        {
            if (isHighlighting)
            {
                RemoveHighlight();
                currentHoveredTile = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
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
        }
    }

    private IEnumerator MineBlock(Vector3Int tilePos, BlockData blockData)
    {
        float elapsedTime = 0f;

        while (elapsedTime < blockData.miningTime)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / blockData.miningTime;

            // Check if still in range and hovering same tile
            Vector3 tileWorldPos = mineableTilemap.CellToWorld(tilePos) + mineableTilemap.cellSize / 2;
            float distance = Vector2.Distance(playerTransform.position, tileWorldPos);

            if (distance > miningRange || currentHoveredTile != tilePos || !Mouse.current.leftButton.isPressed)
            {
                yield break;
            }

            yield return null;
        }

        // Mining complete - spawn drops
        SpawnDrops(tilePos, blockData);

        // Remove the tile
        mineableTilemap.SetTile(tilePos, null);

        // Clean up
        RemoveHighlight();
    }

    private void SpawnDrops(Vector3Int tilePos, BlockData blockData)
    {
        if (blockData.dropPrefab == null) return;

        Vector3 worldPos = mineableTilemap.CellToWorld(tilePos) + mineableTilemap.cellSize / 2;
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
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(lastMouseWorldPos, 0.1f);
        if (mineableTilemap != null && currentHoveredTile.x != int.MinValue)
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
            Gizmos.color = Color.yellow;
            for (int i = 0; i < hexVerts.Length; i++)
            {
                Vector3 a = hexVerts[i];
                Vector3 b = hexVerts[(i + 1) % hexVerts.Length];
                Gizmos.DrawLine(a, b);
            }
        }
    }

}
