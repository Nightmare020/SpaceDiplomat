using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody))]
public class SpaceshipController : MonoBehaviour
{
    public float acceleration = 10f; // Acceleration speed of the spaceship
    public float maxSpeed = 20f; // Maximum speed of the spaceship
    public float rotationSpeed = 100f; // Speed of rotation for the spaceship
    public ParticleSystem[] engineFlares; // Reference to the particle system for engine flares

    private Rigidbody rb; // Reference to the Rigidbody component

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked; // Lock the cursor to the center of the screen
        Cursor.visible = false; // Hide the cursor

        rb = GetComponent<Rigidbody>(); // Get the Rigidbody component attached to the spaceship
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY; // Freeze rotation on X and Z axes and position on Y axis to prevent unwanted movement

    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
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
        Vector3 pos = transform.position; // Get the current position of the spaceship

        bool hitXEdge = false; // Flag to check if the spaceship has hit the X edge
        bool hitZEdge = false; // Flag to check if the spaceship has hit the Z edge

        
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Planet")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Planet reached!"); // Log a message to the console
        }

        if (other.CompareTag("Sun")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Oh no, watch out, sun!"); // Log a message to the console
        }

        if (other.CompareTag("Boundaries")) // Check if the collider is a boundary
        {
            Debug.Log("Inside boundaries!");
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Boundaries")) // Check if the collider is a boundary
        {
            Debug.Log("Inside boundaries!");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Planet")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Left the planet!"); // Log a message to the console
        }

        if (other.CompareTag("Sun")) // Check if the collider has the tag "Finish"
        {
            Debug.Log("Left the sun!"); // Log a message to the console
        }

        if (other.CompareTag("Boundaries")) // Check if the collider is a boundary
        {
            rb.linearVelocity = Vector3.zero; // Stop the spaceship's movement
            rb.angularVelocity = Vector3.zero; // Stop the spaceship's rotation
            Debug.Log("Out of bounds!"); // Log a message to the console
        }
    }
}
