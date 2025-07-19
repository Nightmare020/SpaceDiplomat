using UnityEngine;
using UnityEngine.SceneManagement;

public class SpaceshipController : MonoBehaviour
{
    public float acceleration = 10f; // Acceleration of the spaceship
    public float maxSpeed = 20f; // Maximum speed of the spaceship
    public float rotationSpeed = 100f; // Speed of rotation for the spaceship

    public ParticleSystem engineFlares; // Reference to the particle system for engine flares

    private Rigidbody rb; // Reference to the Rigidbody component

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>(); // Get the Rigidbody component attached to the spaceship
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

        if (Mathf.Abs(thrust) > 0.1f)
        {
            if (!engineFlares.isPlaying) // Check if the engine flares are not already playing
            {
                engineFlares.Play(); // Start the engine flares particle system
            }
        }
        else
        {
            if (engineFlares.isPlaying)
            {
                engineFlares.Stop(); // Stop the engine flares particle system if thrust is zero
            }
        }

        Vector3 force = transform.forward * thrust * acceleration; // Calculate the force based on input and acceleration
        rb.AddForce(force); // Apply the force to the Rigidbody

        // Clamp speed
        if (rb.linearVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed; // Limit the speed to maxSpeed
        }

        // Left/right rotation
        float turnInput = Input.GetAxis("Horizontal"); // Get input for rotation
        rb.MoveRotation(rb.rotation * Quaternion.Euler(Vector3.up * turnInput * rotationSpeed * Time.fixedDeltaTime)); // Rotate the spaceship
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Planet")) // Check if the spaceship collides with an asteroid
        {
            Debug.Log("Collision with asteroid detected!"); // Log the collision
            // Handle collision logic here, e.g., damage the spaceship or destroy the asteroid
        }

        if (other.CompareTag("Sun")) // Check if the spaceship collides with an asteroid
        {
            Debug.Log("Collision with sun detected!"); // Log the collision
            // Handle collision logic here, e.g., damage the spaceship or destroy the asteroid
        }
    }
}
