using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class SpaceshipController : MonoBehaviour
{
    [Header("Motion")]
    public float acceleration = 180f; // Acceleration speed of the spaceship in units/s^2
    public float maxSpeed = 300f; // Maximum linear speed of the spaceship in units/s
    public float brakeForce = 20f; // Force applied when braking the spaceship in units/s^2

    [Header("Rotation")]
    public float yawRate = 120f; // Turn rate in deg/s
    public float yawAccel = 600f; // How quickly turn rate reached target in deg/s^2
    public bool rotateOnlyWhenThrusting = false; // Whether to allow rotation only when the spaceship is thrusting

    [Header("FX & UI")]
    public ParticleSystem[] engineFlares; // Reference to the particle system for engine flares
    public TextMeshProUGUI messageText; // Reference to the UI element that displays interaction messages
    public GameObject sunWarning; // Reference to the sun warning UI element

    [Header("World")]
    public BoxCollider boundaryBox; // Reference to the boundary box collider limiter
    public Transform respawnPoint; // Reference to the respawn point in space

    public Rigidbody rb; // Reference to the Rigidbody component
    private Bounds bounds;
    private bool playerInPlanetRange; // Flag to check if the player is in range to interact with planet
    private GameObject currentPlanet; // Reference to the current planet the player is interacting with

    // Internal state
    float currentYawRate; // In deg/s

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        rb = GetComponent<Rigidbody>(); // Get the Rigidbody component attached to the spaceship
        rb.useGravity = false; // Disable gravity for the spaceship
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY; // Freeze rotation on X and Z axes and position on Y axis to prevent unwanted movement
        rb.interpolation = RigidbodyInterpolation.Interpolate; // Enable interpolation for smoother movement
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // Set collision detection mode to continuous dynamic for better collision handling at high speeds

        if (boundaryBox != null)
        {
            bounds = boundaryBox.bounds; // Get the bounds of the boundary box collider
        }
    }

    private void OnEnable()
    {
        // Hide the mouse cursor and lock it to the center of the screen
        Cursor.lockState = CursorLockMode.Locked; // Lock the cursor to the center of the screen
        Cursor.visible = false; // Hide the cursor

        // Ensure we're using spaceship inputs when this script is enabled
        InputService.Instance.SwitchContext(GameInputContext.Spaceship); // Switch the input context to spaceship controls

        // Shared inputs
        InputService.Instance.Interact += HandleInteract; // Subscribe to the Interact event

        // Ship-only
        InputService.Instance.shipReturnToShip += HandleReturnToShip; // Subscribe to the ship return event
    }

    private void OnDisable()
    {
        if (InputService.Instance == null) return; // Check if InputService instance is available

        // Unsubscribe from all events when this script is disabled
        InputService.Instance.Interact -= HandleInteract; // Unsubscribe from the Interact event
        InputService.Instance.shipReturnToShip -= HandleReturnToShip; // Unsubscribe from the ship return event
    }

    // Update is called once per frame
    void Update()
    {
        // Toggle engine FX from thrust
        bool thrusting = InputService.Instance.shipThrust > 0.0001f; // Check if the ship is thrusting based on input

        foreach (var flare in engineFlares) // Loop through each engine flare particle system
        {
            if (!flare) continue; // Skip if the flare is null

            if (thrusting && !flare.isPlaying) // Check if the particle system is not already playing
            {
                flare.Play(); // Start the particle system
            }
            else if (!thrusting && flare.isPlaying) // Check if the particle system is currently playing
            {
                flare.Stop(); // Stop the particle system
            }
        }
    }

    // FixedUpdate is called once per frame
    void FixedUpdate()
    {
        // Forward/backward thrust
        float thrust = InputService.Instance.shipThrust; // Get input for thrust
        float brake = InputService.Instance.shipBrake; // Get input for braking
        float turn = InputService.Instance.shipTurn; // Get input for turning

        // Forward thrust only
        if (thrust > 0f)
        {
            // Apply forward/backward force
            rb.AddForce(transform.forward * (thrust * acceleration), ForceMode.Acceleration);
        }

        // Brake (reduce velocity)
        if (brake > 0f && rb.linearVelocity.sqrMagnitude > 0.0001f)
        {
            // Decelerate the spaceship opposite to current motion
            Vector3 decel = -rb.linearVelocity.normalized * brakeForce; // Calculate deceleration force in the opposite direction of current velocity

            // Apply brake force
            rb.AddForce(decel, ForceMode.Acceleration); // Apply the deceleration force to the Rigidbody

            // Bring angular velocity down as well so spaceship doesn't spin out of control
            rb.angularVelocity = Vector3.zero;
        }

        // Rotation (yaw only), smoothed toward target rate
        bool canRotate = !rotateOnlyWhenThrusting || thrust > 0f; // Check if rotation is allowed based on thrust input
        float targetYawRate = canRotate ? (turn * yawRate) : 0f; // Calculate target yaw rate based on input and whether rotation is allowed
        currentYawRate = Mathf.MoveTowards(currentYawRate, targetYawRate, yawAccel * Time.fixedDeltaTime); // Smoothly transition to the target yaw rate

        // Apply rotation
        Quaternion delta = Quaternion.Euler(0f, currentYawRate * Time.fixedDeltaTime, 0f); // Calculate the rotation delta based on the current yaw rate
        rb.MoveRotation(rb.rotation * delta); // Apply the rotation to the Rigidbody

        // Clamp speed to maxSpeed
        float sqMax = maxSpeed * maxSpeed; // Calculate the square of the maximum speed for comparison
        if (rb.linearVelocity.sqrMagnitude > sqMax) // Check if the current speed exceeds the maximum speed
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed; // Normalize the velocity and scale it to the maximum speed
        }

        // Hard-clamp any accidental pitch/roll that may leak through
        var rot = rb.rotation; // Get the current rotation of the spaceship
        rot.eulerAngles = new Vector3(0f, rot.eulerAngles.y, 0f); // Set the pitch and roll to zero while keeping the yaw intact
        rb.MoveRotation(rot); // Apply the modified rotation back to the Rigidbody

        // Boundary clamp
        if (boundaryBox == null) return; // If no boundary box is set, skip the boundary check
        Vector3 pos = rb.position; // Get the current position of the spaceship
        Vector3 clampedPos = new Vector3(
            Mathf.Clamp(pos.x, bounds.min.x, bounds.max.x),
            pos.y,
            Mathf.Clamp(pos.z, bounds.min.z, bounds.max.z)
        );

        // Apply clamped position if it differs from the current position
        if (clampedPos != pos)
        {
            rb.position = clampedPos; // Move the spaceship to the clamped position
            rb.linearVelocity = Vector3.zero; // Reset the linear velocity to prevent movement outside the boundaries
            currentYawRate = 0f; // Reset the yaw rate to prevent rotation outside the boundaries
        }
    }

    // ------------------ Triggers ------------------
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Planet")) // Check if the collider has the tag "Finish"
        {
            // Set the playerInRange flag to true
            playerInPlanetRange = true;

            currentPlanet = other.gameObject; // Store the current planet the player is interacting with

            // Show the interaction message
            if (messageText) messageText.gameObject.SetActive(true);
        }

        else if (other.CompareTag("SunProximities")) // Check if the collider has the tag "Finish"
        {
            if (sunWarning) sunWarning.SetActive(true); // Show the sun warning UI element
        }

        else if (other.CompareTag("Sun")) // Check if the collider has the tag "Finish"
        {
            foreach (var flare in engineFlares) // Loop through each engine flare particle system
            {
                if (flare && flare.isPlaying) // Check if the particle system is currently playing
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
            // Set the playerInRange flag to false
            playerInPlanetRange = false;

            if (currentPlanet == other.gameObject) currentPlanet = null; // Clear the reference to the current planet

            // Hide the interaction message
            messageText.gameObject.SetActive(false);
        }

        if (other.CompareTag("SunProximities")) // Check if the collider has the tag "Finish"
        {
            if (sunWarning) sunWarning.SetActive(false); // Hide the sun warning UI element
        }
    }

    // ------------------ Input Handlers ------------------
    private void HandleInteract()
    {
        if (!playerInPlanetRange || !currentPlanet) return; // If the player is not in range or there is no current planet, exit the method

        var renderer = currentPlanet.GetComponentInChildren<Renderer>(); // Get the Renderer component of the current planet
        
        if (renderer != null)
        {
            var mat = renderer.material;
            var tex = mat.GetTexture("_PlanetTexture") as Texture2D;
            PlanetTextureTransfer.currentPlanetTexture = tex;
        }

        var player = GameObject.FindWithTag("Player").transform;
        PlayerData.savedPositionSpace = player.position;
        PlayerData.savedRotationSpace = player.rotation;
        PlayerData.hasSavedPositionSpace = true;
        PlayerData.IsInGalaxyMap = false; // Set the flag to indicate that the player is not in the galaxy map

        // Load the ship interior scene
        SceneManager.LoadScene("SpaceshipMovementScene"); // Load the ShipInterior scene, replacing the current scene
    }

    private void HandleReturnToShip()
    {
        var player = GameObject.FindWithTag("Player").transform; // Find the player object by tag
        PlayerData.savedPositionSpace = player.position; // Save the player's position in space
        PlayerData.savedRotationSpace = player.rotation; // Save the player's rotation in space
        PlayerData.hasSavedPositionSpace = true; // Set the flag to indicate that the player has a saved position in space
        PlayerData.IsInGalaxyMap = false; // Set the flag to indicate that the player is not in the galaxy map

        SceneManager.LoadScene("SpaceshipMovementScene"); // Load the ShipInterior scene, replacing the current scene
    }
}
