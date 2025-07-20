using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float speed = 5f; // Speed of the player movement
    public float mouseSensitivity = 2f; // Sensitivity for mouse look
    public Transform playerCamera; // Reference to the player's camera
    public float jumpHeight = 2.5f; // Height of the jump
    public float gravity = -25f; // Gravity applied to the player

    private CharacterController controller; // Reference to the CharacterController component
    private float xRotation = 0f; // Vertical rotation for mouse look
    private float verticalVelocity = 0f; // Vertical velocity for jumping and gravity

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked; // Lock the cursor to the center of the screen
        Cursor.visible = false; // Hide the cursor
    }

    // Update is called once per frame
    void Update()
    {
        // Mouse look
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f); // Clamp vertical rotation to prevent flipping

        playerCamera.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX); // Rotate the player horizontally

        // Player movement
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        // Check if the player is grounded
        if (controller.isGrounded && verticalVelocity < 0)
        {
            // Give a small push down to keep player on ground
            verticalVelocity = -3f;
        }

        // Jump
        if (Input.GetButtonDown("Jump") && controller.isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -3f * gravity);
        }

        // Let's apply gravity now
        verticalVelocity += gravity * Time.deltaTime;

        // Combine horizontal and vertical movement
        Vector3 move = transform.right * moveX + transform.forward * moveZ; // Calculate movement direction
        move *= speed;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime); // Move the player
    }
}
