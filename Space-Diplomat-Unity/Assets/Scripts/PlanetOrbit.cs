using UnityEngine;

public class PlanetOrbit : MonoBehaviour
{
    public Transform planet; // Reference to the planet around which the object will orbit
    public float orbitSpeed = 10f; // Speed of the orbiting object
    public float selfRotationSpeed = 20f; // Speed of the object's own rotation

    // Update is called once per frame
    void Update()
    {
        // Orbit: rotate around the parent (Sun) at a constant speed
        transform.Rotate(Vector3.up, orbitSpeed * Time.deltaTime, Space.Self);

        // Self-rotation: rotate the planet around its own Y axis
        if (planet != null)
        {
            planet.Rotate(Vector3.up, selfRotationSpeed * Time.deltaTime, Space.Self);
        }
    }
}
