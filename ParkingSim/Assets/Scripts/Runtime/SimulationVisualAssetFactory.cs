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
            Material source = Resources.Load<Material>(AsphaltResourcePath);
            if (source == null) return null;
            Material instance = new Material(source);
            PrepareMaterial(instance);
            PrepareAsphaltMaterial(instance);
            return instance;
        }

        private static void PrepareAsphaltMaterial(Material material)
        {
            Texture baseMap = material.HasProperty("_BaseMap")
                ? material.GetTexture("_BaseMap")
                : null;
            if (baseMap != null)
            {
                baseMap.filterMode = FilterMode.Trilinear;
                baseMap.anisoLevel = 8;
                baseMap.mipMapBias = 0.75f;
            }
            Texture normalMap = material.HasProperty("_BumpMap")
                ? material.GetTexture("_BumpMap")
                : null;
            if (normalMap != null)
            {
                normalMap.filterMode = FilterMode.Trilinear;
                normalMap.anisoLevel = 8;
                normalMap.mipMapBias = 0.75f;
            }
            if (material.HasProperty("_BumpScale"))
                material.SetFloat("_BumpScale", 0.22f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.12f);
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
