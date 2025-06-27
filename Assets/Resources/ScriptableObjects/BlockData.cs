using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "New Block Data", menuName = "Mining/Block Data")]
public class BlockData : ScriptableObject
{
    [Header("Block Properties")]
    public string blockName = "Stone Block";
    public TileBase blockTile; // Reference to the tile asset
    
    [Header("Mining Animation")]
    [Tooltip("Sprites for mining stages: [0] = 25%, [1] = 50%, [2] = 75%")]
    public Sprite[] miningStageSprites = new Sprite[3]; // Just the sprites, not tiles
    
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
    
    /// <summary>
    /// Gets the appropriate sprite based on mining progress (0.0 to 1.0)
    /// </summary>
    public Sprite GetMiningStageSprite(float progress)
    {
        if (progress <= 0f || miningStageSprites == null || miningStageSprites.Length == 0) 
            return null; // Use original sprite
        
        if (progress >= 1f) 
            return null; // Fully mined
        
        // Calculate which stage we're in
        int stageIndex = Mathf.FloorToInt(progress * miningStageSprites.Length);
        stageIndex = Mathf.Clamp(stageIndex, 0, miningStageSprites.Length - 1);
        
        return miningStageSprites[stageIndex];
    }
    
    public int GetRandomDropAmount()
    {
        return Random.Range(minDropAmount, maxDropAmount + 1);
    }
}