using UnityEngine;

public class PlanetTextureAssigner : MonoBehaviour
{
    public Texture2D[] planetTextures; // Array to hold the textures for different planets
    public Material planetMaterialTemplate; // Material to which the textures will be assigned

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (planetTextures.Length == 0 || planetMaterialTemplate == null)
        {
            Debug.LogError("Planet textures or material instance is not set.");
            return;
        }

        // Create a unique material instance
        Material materialInstance = new Material(planetMaterialTemplate);

        // Pick a random texture from the array
        Texture2D texture2D = planetTextures[Random.Range(0, planetTextures.Length)];

        // Assign the selected texture to the material's _BaseTexture property (from Shader Graph)
        materialInstance.SetTexture("_PlanetTexture", texture2D);

        // Apply material to renderer
        GetComponent<Renderer>().material = materialInstance;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
