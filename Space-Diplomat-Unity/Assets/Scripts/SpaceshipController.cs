using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody))]
public class SpaceshipController : MonoBehaviour
{
    public float acceleration = 10f; // Acceleration speed of the spaceship
    public float maxSpeed = 20f; // Maximum speed of the spaceship
    public float rotationSpeed = 100f; // Speed of rotation for the spaceship
    public ParticleSystem[] engineFlares; // Reference to the particle system for engine flares
    public TextMeshProUGUI messageText; // Reference to the UI element that displays interaction messages
    public BoxCollider boundaryBox; // Reference to the boundary box collider limiter

    private Rigidbody rb; // Reference to the Rigidbody component
    private Bounds bounds;
    private bool playerInPlanetRange = false; // Flag to check if the player is in range to interact with planet

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked; // Lock the cursor to the center of the screen
        Cursor.visible = false; // Hide the cursor
        PlayerData.IsInGalaxyMap = true; // Set the flag to indicate that the player is in the galaxy map

        rb = GetComponent<Rigidbody>(); // Get the Rigidbody component attached to the spaceship
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY; // Freeze rotation on X and Z axes and position on Y axis to prevent unwanted movement

        if (boundaryBox != null)
        {
            bounds = boundaryBox.bounds; // Get the bounds of the boundary box collider
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Check if the player is in range and presses the interaction key (E)
        if (playerInPlanetRange && Input.GetKeyDown(KeyCode.E))
        {
            PlayerData.savedPositionSpace = GameObject.FindWithTag("Player").transform.position; // Save the player's position
            PlayerData.savedRotationSpace = GameObject.FindWithTag("Player").transform.rotation; // Save the player's rotation
            PlayerData.hasSavedPositionSpace = true;
            PlayerData.IsInGalaxyMap = false; // Reset the flag to indicate that the player is no longer in the galaxy map

            // Load the specified scene
            SceneChanger.instance.ChangeScene("SpaceshipMovementScene");
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            PlayerData.savedPositionSpace = GameObject.FindWithTag("Player").transform.position; // Save the player's position
            PlayerData.savedRotationSpace = GameObject.FindWithTag("Player").transform.rotation; // Save the player's rotation
            PlayerData.hasSavedPositionSpace = true;
            PlayerData.IsInGalaxyMap = false; // Reset the flag to indicate that the player is no longer in the galaxy map

            // Load the spaceship scene
            SceneManager.LoadScene("SpaceshipMovementScene");
        }
    }

    // FixedUpdate is called once per frame
    void FixedUpdate()
    {
        // Forward/backward thrust
        float thrust = Input.GetAxis("Vertical"); // Get input for thrust
        float turn = Input.GetAxis("Horizontal"); // Get input for turning

        // Apply forward/backward force
        rb.AddForce(transform.forward * thrust * acceleration);

        // Apply rotation (around Y axis only)
        Quaternion deltaRotation = Quaternion.Euler(0f, turn * rotationSpeed * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * deltaRotation);

        // Limit the spaceship's speed
        if (rb.linearVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed; // Normalize the velocity and multiply by max speed
        }

        if (Mathf.Abs(thrust) > 0.1f)
        {
            foreach (var flare in engineFlares) // Loop through each engine flare particle system
            {
                if (!flare.isPlaying) // Check if the particle system is not already playing
                {
                    flare.Play(); // Start the particle system
                }
            }
        }
        else
        {
            foreach (var flare in engineFlares) // Loop through each engine flare particle system
            {
                if (flare.isPlaying) // Check if the particle system is currently playing
                {
                    flare.Stop(); // Stop the particle system
                }
            }
        }
    }

    // LateUpdate is called once per frame after all Update methods have been called
    void LateUpdate()
    {
        if (boundaryBox == null) return;

        Vector3 pos = transform.position; // Get the current position of the spaceship
        Vector3 clampedPos = pos;

        clampedPos.x = Mathf.Clamp(pos.x, bounds.min.x, bounds.max.x); // Clamp the X position within the boundary box
        clampedPos.z = Mathf.Clamp(pos.z, bounds.min.z, bounds.max.z); // Clamp the Z position within the boundary box

        // Apply only if clamped (out of bounds)
        if (clampedPos != pos)
        {
            transform.position = clampedPos; // Move the spaceship to the clamped position
            rb.linearVelocity = Vector3.zero; // Reset the linear velocity to prevent movement outside the boundaries
            rb.angularVelocity = Vector3.zero; // Reset the angular velocity to prevent rotation outside the boundaries
            Debug.Log("Out of bounds!"); // Log a message to the console
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Planet")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Planet reached!"); // Log a message to the console

            // Set the playerInRange flag to true
            playerInPlanetRange = true;

            // Show the interaction message
            messageText.gameObject.SetActive(true);
        }

        if (other.CompareTag("Sun")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Oh no, watch out, sun!"); // Log a message to the console
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Planet")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Left the planet!"); // Log a message to the console

            // Set the playerInRange flag to false
            playerInPlanetRange = false;

            // Hide the interaction message
            messageText.gameObject.SetActive(false);
        }

        if (other.CompareTag("Sun")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Left the sun!"); // Log a message to the console
        }
    }
}
