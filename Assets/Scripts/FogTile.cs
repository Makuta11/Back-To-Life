using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "FogTile", menuName = "Tiles/FogTile")]
public class FogTile : TileBase
{
    [SerializeField] private Sprite fogSprite;
    [SerializeField] private Color tintColor = Color.black;
    
    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        tileData.sprite = fogSprite;
        tileData.color = tintColor;
        tileData.transform = Matrix4x4.identity;
        tileData.flags = TileFlags.None;
        tileData.colliderType = Tile.ColliderType.None;
    }
}