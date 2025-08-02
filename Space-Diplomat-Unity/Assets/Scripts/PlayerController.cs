using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 45f; // Speed of the player movement
    public float jumpHeight = 5f; // Height of the jump
    public float gravity = -50f; // Gravity applied to the player

    [Header("Mouse Look")]
    public Transform playerCamera; // Reference to the player's camera
    public float mouseSensitivity = 2f; // Sensitivity for mouse look
    public bool invertMouseY = false; // Invert mouse Y-axis

    private CharacterController controller; // Reference to the CharacterController component
    private float xRotation = 0f; // Vertical rotation for mouse look
    private float verticalVelocity = 0f; // Vertical velocity for jumping and gravity

    private InteractObject currentInteractable; // Reference to the current interactable object

    // OnEnable is called when the script instance is being loaded
    void OnEnable()
    {
        controller = GetComponent<CharacterController>();

        // Ensure the cursor is locked and hidden when the game starts
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Ensure astronaut inputs are active
        InputService.Instance.SwitchContext(GameInputContext.Astronaut);

        // Subscribe to input events
        InputService.Instance.astroJump += OnJump;
        InputService.Instance.Interact += OnInteract;
    }

    // OnDisable is called when the script instance is being unloaded
    void OnDisable()
    {
        if (InputService.Instance == null) return; // Check if InputService is available

        // Unsubscribe from input events
        InputService.Instance.astroJump -= OnJump;
        InputService.Instance.Interact -= OnInteract;
    }

    // Update is called once per frame
    void Update()
    {
        // ----------- Mouse Look (mouse delta or right stick) -----------
        Vector2 look = InputService.Instance.astroLook;
        float lookX = look.x * mouseSensitivity;
        float lookY = look.y * mouseSensitivity * (invertMouseY ? 1f : -1f);

        xRotation = Mathf.Clamp(xRotation + lookY, -90f, 90f);
        if (playerCamera) playerCamera.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        transform.Rotate(Vector3.up, lookX); // Rotate the player horizontally

        // ----------- Move (WASD / left stick) -----------
        Vector2 mv = InputService.Instance.astroMove;
        Vector3 move = (transform.right * mv.x + transform.forward * mv.y) * speed; // Calculate movement direction

        // Check if the player is grounded
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            // Give a small push down to keep player on ground
            verticalVelocity = -3f;
        }

        // Gravity
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity; // Apply vertical velocity to movement

        controller.Move(move * Time.deltaTime); // Move the player
    }

    // --------------- Event Handlers ---------------
    private void OnJump()
    {
        if (!controller.isGrounded) return; // Prevent jumping if not grounded
        verticalVelocity = Mathf.Sqrt(jumpHeight * -3f * gravity); // Calculate jump velocity
    }

    private void OnInteract()
    {
        currentInteractable?.PerformInteraction(); // Call the interaction method on the current interactable object
    }

    // --------------- Interactable Management ---------------
    public void SetCurrentInteractable(InteractObject interactable)
    {
        currentInteractable = interactable; // Set the current interactable object
    }

    public void ClearInteractable(InteractObject interactable)
    {
        if (currentInteractable == interactable)
        {
            currentInteractable = null; // Clear the current interactable if it matches
        }
    }
}
