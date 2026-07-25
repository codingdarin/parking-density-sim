using UnityEngine;

namespace ParkingSim.Runtime
{
    /// <summary>
    /// Optional presentation assets. A missing asset deliberately falls back to
    /// deterministic primitive geometry so the simulation stays self-contained.
    /// Imported models should face local +X and use a one-metre unit scale.
    /// </summary>
    public static class SimulationVisualAssetFactory
    {
        public const string TransportUnitResourcePath =
            "ParkingSim/Vehicles/TransportUnit";
        public const string ApartmentResourcePath =
            "ParkingSim/Environment/Apartment";
        public const string FireResourcePath =
            "ParkingSim/Effects/BuildingFire";
        public const string AsphaltResourcePath =
            "ParkingSim/Environment/Asphalt";
        private static readonly string[] CarResourcePaths =
        {
            "ParkingSim/Vehicles/Cars/Car01",
            "ParkingSim/Vehicles/Cars/Car02",
            "ParkingSim/Vehicles/Cars/Car03",
            "ParkingSim/Vehicles/Cars/Car04",
            "ParkingSim/Vehicles/Cars/Car05",
        };
        private static readonly string[] ApartmentResourcePaths =
        {
            "ParkingSim/Environment/Apartment01",
            "ParkingSim/Environment/Apartment02",
            "ParkingSim/Environment/Apartment03",
            "ParkingSim/Environment/Apartment04",
        };

        public static GameObject TryCreate(string resourcePath, string name)
        {
            GameObject source = Resources.Load<GameObject>(resourcePath);
            if (source == null) return null;
            GameObject instance = Object.Instantiate(source);
            instance.name = name;
            PrepareMaterials(instance);
            return instance;
        }

        public static GameObject TryCreateApartment(int variant, string name)
        {
            int index = Mathf.Abs(variant) % ApartmentResourcePaths.Length;
            GameObject instance = TryCreate(ApartmentResourcePaths[index], name);
            return instance ?? TryCreate(ApartmentResourcePath, name);
        }

        public static GameObject TryCreateCar(int variant, string name)
        {
            int index = Mathf.Abs(variant) % CarResourcePaths.Length;
            return TryCreate(CarResourcePaths[index], name);
        }

        public static GameObject TryCreateFire(string name)
        {
            GameObject source = Resources.Load<GameObject>(FireResourcePath);
            if (source == null) return null;
            GameObject instance = Object.Instantiate(source);
            instance.name = name;
            return instance;
        }

        public static Material TryCreateAsphaltMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var material = new Material(shader)
            {
                name = "Procedural-Asphalt"
            };
            Texture2D texture = CreateAsphaltTexture();
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            material.color = Color.white;
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.10f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.10f);
            material.DisableKeyword("_NORMALMAP");
            return material;
        }

        private static Texture2D CreateAsphaltTexture()
        {
            const int size = 64;
            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGB24,
                mipChain: true)
            {
                name = "Procedural-Asphalt-Texture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                mipMapBias = 0.35f
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    uint hash = (uint)(x * 73856093) ^
                                (uint)(y * 19349663) ^
                                0x9e3779b9u;
                    hash ^= hash >> 13;
                    hash *= 1274126177u;
                    float fine = (hash & 1023u) / 1023f;
                    float broad = Mathf.PerlinNoise(
                        x * 0.115f,
                        y * 0.115f);
                    float value = 0.29f +
                                  (broad - 0.5f) * 0.055f +
                                  (fine - 0.5f) * 0.022f;
                    pixels[y * size + x] =
                        new Color(value, value * 1.01f, value * 1.02f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        private static void PrepareMaterials(GameObject instance)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.materials)
                    PrepareMaterial(material);
            }
        }

        private static void PrepareMaterial(Material material)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (material == null || urpLit == null || material.shader == urpLit)
                return;
            Texture baseMap = material.HasProperty("_MainTex")
                ? material.GetTexture("_MainTex")
                : null;
            Texture normalMap = material.HasProperty("_BumpMap")
                ? material.GetTexture("_BumpMap")
                : null;
            Texture occlusionMap = material.HasProperty("_OcclusionMap")
                ? material.GetTexture("_OcclusionMap")
                : null;
            Color baseColor = material.HasProperty("_Color")
                ? material.GetColor("_Color")
                : Color.white;
            float smoothness = material.HasProperty("_Glossiness")
                ? material.GetFloat("_Glossiness")
                : 0.25f;
            material.shader = urpLit;
            if (baseMap != null) material.SetTexture("_BaseMap", baseMap);
            if (normalMap != null)
            {
                material.SetTexture("_BumpMap", normalMap);
                material.EnableKeyword("_NORMALMAP");
            }
            if (occlusionMap != null)
                material.SetTexture("_OcclusionMap", occlusionMap);
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Smoothness", smoothness);
        }
    }
}
