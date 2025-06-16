using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "New Block Data", menuName = "Mining/Block Data")]
public class BlockData : ScriptableObject
{
    [Header("Block Properties")]
    public string blockName = "Stone Block";
    public TileBase blockTile; // Reference to the tile asset
    
    [Header("Mining Properties")]
    [Range(0.5f, 10f)]
    public float miningTime = 3f; // Time in seconds to mine this block
    
    [Header("Drops")]
    public GameObject dropPrefab; // What item drops when mined
    [Range(1, 10)]
    public int minDropAmount = 1;
    [Range(1, 10)]
    public int maxDropAmount = 3;
    
    [Header("Visual/Audio")]
    public Color highlightColor = new Color(1f, 1f, 1f, 0.5f); // Tint when hovering
    
    public int GetRandomDropAmount()
    {
        return Random.Range(minDropAmount, maxDropAmount + 1);
    }
}