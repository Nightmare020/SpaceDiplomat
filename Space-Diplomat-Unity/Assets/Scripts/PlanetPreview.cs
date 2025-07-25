using UnityEngine;

public class PlanetPreview : MonoBehaviour
{
    public Material basePlanetMaterial; // Material to be used for the planet preview

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Get the renderer component attached to this GameObject
        Renderer planetRenderer = GetComponent<Renderer>(); // Get the Renderer component attached to this GameObject

        // Get all materials (cloned automatically)
        Material[] materials = planetRenderer.materials; // Get the materials array from the renderer

        // Create a unique material texture if available
        Material instance = new Material(basePlanetMaterial); // Create a new instance of the base planet material

        // Assign the shared texture if available
        if (PlanetTextureTransfer.currentPlanetTexture != null)
        {
            instance.SetTexture("_PlanetTexture", PlanetTextureTransfer.currentPlanetTexture); // Set the texture on the material instance
        }
        
        // Replace only material at index 3
        if (materials.Length > 3)
        {
            materials[3] = instance; // Replace the material at index 3 with the new instance
            planetRenderer.materials = materials; // Assign the modified materials array back to the renderer
        }
        else
        {
            Debug.LogWarning("Not enough materials in the renderer to replace at index 3."); // Log a warning if there are not enough materials
        }
    }
}
