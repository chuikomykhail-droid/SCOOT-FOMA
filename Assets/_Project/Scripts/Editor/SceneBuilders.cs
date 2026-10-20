using ScootFoma.Debugging;
using ScootFoma.InputSources;
using ScootFoma.Presentation;
using ScootFoma.Settings;
using ScootFoma.Simulation;
using ScootFoma.Spikes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScootFoma.Editor
{
    /// Сцены собираются кодом, чтобы их можно было пересоздать и не держать ручную вёрстку.
    public static class SceneBuilders
    {
        const string ScenesDir = "Assets/_Project/Scenes";
        const string SpikesDir = "Assets/_Project/Scenes/Spikes";
        const string MaterialsDir = "Assets/_Project/Materials";

        [MenuItem("Scoot Foma/Build Test Park")]
        public static void BuildTestPark()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Light();
            Camera(new Vector3(6f, 4f, -8f));
            Simulation();
            var ground = Mat("Ground", new Color(0.55f, 0.57f, 0.6f));
            var obstacle = Mat("Obstacle", new Color(0.85f, 0.55f, 0.25f));
            var root = new GameObject("Park").transform;

            Box(root, "Ground", new Vector3(0f, -0.25f, 0f), Quaternion.identity, new Vector3(80f, 0.5f, 80f), ground);
            // Рампа 30°: длина 3 м, ширина 4 м
            Wedge(root, "Ramp30", new Vector3(-8f, 0f, 10f), 30f, 3f, 4f, obstacle);
            // Кикер 15°
            Wedge(root, "Kicker15", new Vector3(0f, 0f, 10f), 15f, 1.2f, 1.5f, obstacle);
            // Ледж 40 см
            Box(root, "Ledge", new Vector3(8f, 0.2f, 10f), Quaternion.identity, new Vector3(0.5f, 0.4f, 6f), obstacle);
            // Рейл: труба Ø5 см на высоте 40 см
            var rail = Box(root, "RailSupportA", new Vector3(-4f, 0.19f, 18f), Quaternion.identity, new Vector3(0.05f, 0.38f, 0.05f), obstacle);
            Box(root, "RailSupportB", new Vector3(-4f, 0.19f, 24f), Quaternion.identity, new Vector3(0.05f, 0.38f, 0.05f), obstacle);
            var railGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            railGo.name = "Rail";
            railGo.transform.SetParent(root, false);
            railGo.transform.SetPositionAndRotation(new Vector3(-4f, 0.4f, 21f), Quaternion.Euler(90f, 0f, 0f));
            railGo.transform.localScale = new Vector3(0.05f, 3.2f, 0.05f);
            railGo.GetComponent<Renderer>().sharedMaterial = obstacle;
            railGo.layer = LayerMask.NameToLayer("Ground");

            Save(scene, $"{ScenesDir}/TestPark.unity");
        }

        const string InputActionsPath = "Assets/_Project/Input/ScootFoma.inputactions";
        const string InputTuningPath = "Assets/_Project/Settings/InputTuning.asset";

        /// Сцена этапа 1: ввод и намерения с отладочным HUD. Физики райдера пока нет.
        [MenuItem("Scoot Foma/Build Input Lab")]
        public static void BuildInputLab()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Light();
            Camera(new Vector3(0f, 1.5f, -4f));
            var (loop, _) = Simulation();

            var go = new GameObject("Rider Input");
            var driver = go.AddComponent<RiderInputDriver>();
            Assign(driver, ("loop", loop),
                ("actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath)),
                ("tuning", InputTuning()));
            var hud = go.AddComponent<IntentDebugHud>();
            Assign(hud, ("driver", driver));
            Save(scene, $"{ScenesDir}/InputLab.unity");
        }

        static InputTuningAsset InputTuning()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputTuningAsset>(InputTuningPath);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<InputTuningAsset>();
            AssetDatabase.CreateAsset(asset, InputTuningPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        [MenuItem("Scoot Foma/Spikes/Build S1 Grip Scene")]
        public static void BuildS1()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Light();
            var cam = Camera(new Vector3(2.5f, 1.8f, -3f));
            var (loop, interp) = Simulation();
            var ground = Mat("Ground", new Color(0.55f, 0.57f, 0.6f));
            Box(null, "Ground", new Vector3(0f, -0.25f, 0f), Quaternion.identity, new Vector3(40f, 0.5f, 40f), ground);

            var harness = new GameObject("S1 Grip Spike").AddComponent<S1GripSpike>();
            Assign(harness, ("loop", loop), ("interpolator", interp), ("followCamera", cam),
                ("scooterMaterial", Mat("Scooter", new Color(0.2f, 0.45f, 0.85f))),
                ("riderMaterial", Mat("Rider", new Color(0.9f, 0.8f, 0.7f))));
            Save(scene, $"{SpikesDir}/S1_Grip.unity");
        }

        [MenuItem("Scoot Foma/Spikes/Build S3 Wheel Scene")]
        public static void BuildS3()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Light();
            var cam = Camera(new Vector3(2.5f, 1.8f, -3f));
            var (loop, interp) = Simulation();
            var harness = new GameObject("S3 Wheel Spike").AddComponent<S3WheelSpike>();
            Assign(harness, ("loop", loop), ("interpolator", interp), ("followCamera", cam),
                ("scooterMaterial", Mat("Scooter", new Color(0.2f, 0.45f, 0.85f))),
                ("groundMaterial", Mat("Ground", new Color(0.55f, 0.57f, 0.6f))));
            Save(scene, $"{SpikesDir}/S3_Wheels.unity");
        }

        [MenuItem("Scoot Foma/Spikes/Build S4 Input Scene")]
        public static void BuildS4()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera(new Vector3(0f, 1f, -5f));
            new GameObject("S4 Input Spike").AddComponent<S4InputSpike>();
            Save(scene, $"{SpikesDir}/S4_Input.unity");
        }

        [MenuItem("Scoot Foma/Spikes/Build All Spike Scenes")]
        public static void BuildAll()
        {
            BuildS1();
            BuildS3();
            BuildS4();
            BuildTestPark();
        }

        static (SimulationLoop, PoseInterpolator) Simulation()
        {
            var go = new GameObject("Simulation");
            var loop = go.AddComponent<SimulationLoop>();
            var interp = go.AddComponent<PoseInterpolator>();
            Assign(interp, ("loop", loop));
            return (loop, interp);
        }

        static void Light()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        static FollowCamera Camera(Vector3 position)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<UnityEngine.Camera>();
            cam.nearClipPlane = 0.05f;
            go.AddComponent<AudioListener>();
            go.transform.position = position;
            go.transform.LookAt(Vector3.up * 0.6f);
            return go.AddComponent<FollowCamera>();
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = LayerMask.NameToLayer("Ground");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// Наклонная плоскость: нижний край на земле в точке start, подъём вдоль +Z.
        static void Wedge(Transform parent, string name, Vector3 start, float angle, float length, float width, Material mat)
        {
            const float thickness = 0.3f;
            float rad = angle * Mathf.Deg2Rad;
            Quaternion rot = Quaternion.Euler(-angle, 0f, 0f);
            Vector3 center = start + new Vector3(0f, Mathf.Sin(rad) * length * 0.5f, Mathf.Cos(rad) * length * 0.5f)
                + rot * new Vector3(0f, -thickness * 0.5f, 0f);
            Box(parent, name, center, rot, new Vector3(width, thickness, length), mat);
        }

        static Material Mat(string name, Color color)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void Assign(Object target, params (string field, Object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var prop = so.FindProperty(field);
                if (prop != null) prop.objectReferenceValue = value;
                else Debug.LogWarning($"[SceneBuilders] Нет поля {field} у {target.GetType().Name}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Save(UnityEngine.SceneManagement.Scene scene, string path)
        {
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[SceneBuilders] Сохранено: {path}");
        }
    }
}
