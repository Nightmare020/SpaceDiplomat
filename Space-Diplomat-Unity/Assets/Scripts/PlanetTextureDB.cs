using System.Collections.Generic;
using UnityEngine;

public static class PlanetTextureDB
{
    // planet-id -> texture picked the first time we saw that planet
    private static readonly Dictionary<string, Texture2D> map = new Dictionary<string, Texture2D>();

    public static Texture2D GetOrAssign (string planetId, Texture2D[] texturePool)
    {
        if (map.TryGetValue(planetId, out var existingTexture))
        {
            return existingTexture; // Return the existing texture if it exists
        }
        existingTexture = texturePool[Random.Range(0, texturePool.Length)]; // Pick a random texture from the pool
        map[planetId] = existingTexture; // Assign the new texture to the planet ID
        return existingTexture; // Return the newly assigned texture
    }
}
