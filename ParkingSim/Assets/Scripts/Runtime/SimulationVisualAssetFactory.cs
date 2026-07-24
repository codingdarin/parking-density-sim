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

        public static GameObject TryCreate(string resourcePath, string name)
        {
            GameObject source = Resources.Load<GameObject>(resourcePath);
            if (source == null) return null;
            GameObject instance = Object.Instantiate(source);
            instance.name = name;
            return instance;
        }
    }
}
