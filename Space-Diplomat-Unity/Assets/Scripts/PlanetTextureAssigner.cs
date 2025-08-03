using UnityEngine;

public class PlanetTextureAssigner : MonoBehaviour
{
    [SerializeField] string planetId; // Unique identifier for the planet, not used in this script but can be useful for future reference
    [SerializeField] Texture2D[] planetTextures; // Array to hold the textures for different planets
    [SerializeField] Material planetMaterialTemplate; // Material to which the textures will be assigned

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (planetTextures.Length == 0 || planetMaterialTemplate == null)
        {
            return;
        }

        // This call remembers the texture picked the first time
        Texture2D texture2D = PlanetTextureDB.GetOrAssign(planetId, planetTextures);

        // Create a unique material instance
        Material materialInstance = new Material(planetMaterialTemplate);

        // Assign the selected texture to the material's _BaseTexture property (from Shader Graph)
        materialInstance.SetTexture("_PlanetTexture", texture2D);

        // Apply material to renderer
        GetComponent<Renderer>().material = materialInstance;
    }
}
