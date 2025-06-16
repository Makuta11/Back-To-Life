using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;

    private Vector2 moveInput;
    private Vector2 lastMoveDirection = Vector2.down;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    private void Update()
    {
        Vector2 movement = moveInput.normalized * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + movement);
        
        HandleAnimationAndFlip();
    }

    private void HandleAnimationAndFlip()
    {
        bool isMoving = moveInput.sqrMagnitude > 0.01f;

        if (isMoving)
        {
            Vector2 dir = moveInput.normalized;
            lastMoveDirection = dir;

            string direction = GetAnimationDirection(dir, isMoving: true);
            animator.Play(direction);

            // Flip sprite if moving left
            spriteRenderer.flipX = dir.x < -0.01f;
        }
        else
        {
            animator.Play("Idle");
            spriteRenderer.flipX = lastMoveDirection.x < -0.01f;
        }
    }

    private string GetAnimationDirection(Vector2 dir, bool isMoving)
    {
        string prefix = isMoving ? "Run" : "Idle";

        if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            return prefix + "";
        else if (dir.y > 0)
            return prefix + "";
        else
            return prefix + "";
    }
}
