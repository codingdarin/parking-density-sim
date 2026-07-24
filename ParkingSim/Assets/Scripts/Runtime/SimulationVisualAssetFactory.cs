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
        public const string CarResourcePath = "ParkingSim/Vehicles/Car";
        public const string TransportUnitResourcePath =
            "ParkingSim/Vehicles/TransportUnit";
        public const string ApartmentResourcePath =
            "ParkingSim/Environment/Apartment";
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

        private static void PrepareMaterials(GameObject instance)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) return;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.materials)
                {
                    if (material == null || material.shader == urpLit) continue;
                    Texture baseMap = material.HasProperty("_MainTex")
                        ? material.GetTexture("_MainTex")
                        : null;
                    Texture normalMap = material.HasProperty("_BumpMap")
                        ? material.GetTexture("_BumpMap")
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
                    material.SetColor("_BaseColor", baseColor);
                    material.SetFloat("_Smoothness", smoothness);
                }
            }
        }
    }
}
