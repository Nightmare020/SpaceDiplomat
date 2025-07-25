using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class SpaceshipController : MonoBehaviour
{
    public float acceleration = 20f; // Acceleration speed of the spaceship
    public float maxSpeed = 25f; // Maximum speed of the spaceship
    public float rotationSpeed = 80f; // Speed of rotation for the spaceship
    public float brakeForce = 5f; // Force applied when braking the spaceship

    public ParticleSystem[] engineFlares; // Reference to the particle system for engine flares
    public TextMeshProUGUI messageText; // Reference to the UI element that displays interaction messages
    public BoxCollider boundaryBox; // Reference to the boundary box collider limiter
    public GameObject sunWarning; // Reference to the sun warning UI element
    public Transform respawnPoint; // Reference to the respawn point in space

    public Rigidbody rb; // Reference to the Rigidbody component
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
            // Get the planet near you
            GameObject planet = GameObject.FindWithTag("Planet");

            // Get its material and texture
            Material planetMat = planet.GetComponent<Renderer>().material; // Get the material of the planet
            Texture2D planetTexture = planetMat.GetTexture("_PlanetTexture") as Texture2D; // Get the texture from the material

            // Store it globally for later use
            PlanetTextureTransfer.currentPlanetTexture = planetTexture; // Assign the texture to the static variable

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
        float thrustInput = Input.GetKey(KeyCode.W) ? 1f : 0f; // Get input for thrust
        float brakeInput = Input.GetKey(KeyCode.S) ? 1f : 0f; // Get input for braking
        float turnInput = Input.GetAxis("Horizontal"); // Get input for turning

        // Forward thrust only
        if (thrustInput > 0f)
        {
            // Apply forward/backward force
            rb.AddForce(transform.forward * thrustInput * acceleration);

            // Apply rotation (around Y axis only) only while moving forward
            transform.Rotate(Vector3.up, turnInput * rotationSpeed * Time.fixedDeltaTime); // Rotate the spaceship based on input
        }

        // Brake (reduce velocity)
        if (brakeInput > 0f)
        {
            // Apply brake force
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, brakeForce * Time.fixedDeltaTime); // Gradually reduce the velocity
            rb.angularVelocity = Vector3.zero; // Reset angular velocity to prevent rotation while braking
        }

        // Clamp spaceship's speed
        if (rb.linearVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed; // Normalize the velocity and multiply by max speed
        }

        // Engine flares control
        foreach (var flare in engineFlares) // Loop through each engine flare particle system
        {
            if (thrustInput > 0f)
            {
                if (!flare.isPlaying) // Check if the particle system is not already playing
                {
                    flare.Play(); // Start the particle system
                }
            }
            else
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

            rb.angularVelocity = Vector3.zero; // Reset the angular velocity to prevent rotation when planet reached

            // Show the interaction message
            messageText.gameObject.SetActive(true);
        }

        if (other.CompareTag("SunProximities")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Oh no, watch out, sunclose!"); // Log a message to the console

            sunWarning.SetActive(true); // Show the sun warning UI element
        }

        if (other.CompareTag("Sun")) // Check if the collider has the tag "Finish"
        {
            foreach (var flare in engineFlares) // Loop through each engine flare particle system
            {
                if (flare.isPlaying) // Check if the particle system is currently playing
                {
                    flare.Stop(); // Stop the particle system
                }
            }

            gameObject.SetActive(false); // Deactivate the spaceship game object

            FindFirstObjectByType<SpaceshipRespawner>().Respawn(0.5f); // Call the Respawn method on the SpaceshipRespawner component with a delay of 2 seconds
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

        if (other.CompareTag("SunProximities")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("You left the sun proximities!"); // Log a message to the console

            sunWarning.SetActive(false); // Hide the sun warning UI element
        }
    }
}
