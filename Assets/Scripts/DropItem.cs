using UnityEngine;

public class DropItem : MonoBehaviour
{
    [Header("Drop Properties")]
    public string itemName = "Stone";
    public int quantity = 1;
    
    [Header("Movement")]
    public float dropForce = 1f; // Reduced from 3f
    public float floatAmplitude = 0.1f;
    public float floatSpeed = 2f;
    
    [Header("Pickup")]
    public float pickupRadius = 1.5f;
    public float pickupSpeed = 5f;
    
    private Rigidbody2D rb;
    private Vector3 startPosition;
    private bool canBePickedUp = false;
    private bool isBeingPickedUp = false;
    private Transform playerTransform;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }
    
    private void Start()
    {
        // Find the player
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        
        if (player != null)
        {
            // Calculate direction towards player
            Vector2 directionToPlayer = (player.transform.position - transform.position).normalized;
            
            // Add some randomness to make it feel more natural
            float randomAngle = Random.Range(-30f, 30f) * Mathf.Deg2Rad;
            Vector2 randomizedDirection = new Vector2(
                directionToPlayer.x * Mathf.Cos(randomAngle) - directionToPlayer.y * Mathf.Sin(randomAngle),
                directionToPlayer.x * Mathf.Sin(randomAngle) + directionToPlayer.y * Mathf.Cos(randomAngle)
            );
            
            rb.AddForce(randomizedDirection * dropForce, ForceMode2D.Impulse);
        }
        else
        {
            // Fallback to random direction if player not found
            Vector2 randomDirection = new Vector2(Random.Range(-1f, 1f), Random.Range(0.5f, 1f));
            rb.AddForce(randomDirection.normalized * dropForce, ForceMode2D.Impulse);
        }
        
        // Set gravity to 0 for isometric game
        rb.gravityScale = 0;
        
        // Add drag to slow down the initial movement
        rb.linearDamping = 2f;
        
        // Allow pickup after a short delay
        Invoke(nameof(EnablePickup), 0.5f);
    }
    
    private void EnablePickup()
    {
        canBePickedUp = true;
        startPosition = transform.position;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
    }
    
    private void Update()
    {
        if (!canBePickedUp) return;
        
        if (!isBeingPickedUp)
        {
            // Floating animation
            float newY = startPosition.y + Mathf.Sin(Time.time * floatSpeed) * floatAmplitude;
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
            
            // Check for nearby player - find by tag instead of layer
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                float distance = Vector2.Distance(transform.position, player.transform.position);
                if (distance < pickupRadius)
                {
                    playerTransform = player.transform;
                    isBeingPickedUp = true;
                }
            }
        }
        else
        {
            // Move towards player
            if (playerTransform != null)
            {
                transform.position = Vector3.MoveTowards(transform.position, playerTransform.position, pickupSpeed * Time.deltaTime);
                
                // Check if reached player
                if (Vector3.Distance(transform.position, playerTransform.position) < 0.3f)
                {
                    CollectItem();
                }
            }
        }
    }
    
    private void CollectItem()
    {
        // TODO: Add to inventory system
        Destroy(gameObject);
    }
    
    private void OnDrawGizmos()
    {
        if (canBePickedUp && !isBeingPickedUp)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }
        else if (isBeingPickedUp)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }
    }
}