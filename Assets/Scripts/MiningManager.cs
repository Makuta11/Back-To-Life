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
    }

    private void Update()
    {
        HandleTileHighlighting();
        HandleMining();
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

    // Handles highlighting logic for hovered tiles
    private void HandleTileHighlighting()
    {
        if (Mouse.current == null) return;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, 0));
        mouseWorldPos.z = 0;
        Vector3Int? hoveredTile = GetTopmostTileAtPosition(mouseWorldPos);
        if (hoveredTile.HasValue)
        {
            Vector3Int tilePos = hoveredTile.Value;
            if (tilePos != currentHoveredTile)
            {
                currentHoveredTile = tilePos;
                if (isHighlighting)
                    RemoveHighlight();
                TileBase tile = mineableTilemap.GetTile(tilePos);
                if (tile != null && tileToBlockData.ContainsKey(tile))
                {
                    Vector3 tileWorldPos = mineableTilemap.CellToWorld(tilePos) + new Vector3(mineableTilemap.cellSize.x / 2, mineableTilemap.cellSize.y / 2, 0);
                    float distance = Vector2.Distance(playerTransform.position, tileWorldPos);
                    if (distance <= miningRange)
                        ApplyHighlight(tilePos);
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

    // Handles mining logic: input, coroutine for progress, and cancelling on mouse release
    private void HandleMining()
    {
        if (Mouse.current == null) return;
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
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            if (miningCoroutine != null)
            {
                StopCoroutine(miningCoroutine);
                miningCoroutine = null;
            }
        }
    }

    // Coroutine: tracks mining time and checks conditions
    private IEnumerator MineBlock(Vector3Int tilePos, BlockData blockData)
    {
        float elapsedTime = 0f;
        while (elapsedTime < blockData.miningTime)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / blockData.miningTime;
            Vector3 tileWorldPos = mineableTilemap.CellToWorld(tilePos) + mineableTilemap.cellSize / 2;
            float distance = Vector2.Distance(playerTransform.position, tileWorldPos);
            if (distance > miningRange || currentHoveredTile != tilePos || !Mouse.current.leftButton.isPressed)
            {
                yield break;
            }
            yield return null;
        }
        SpawnDrops(tilePos, blockData);
        mineableTilemap.SetTile(tilePos, null);
        RemoveHighlight();
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
        }
    }
}
