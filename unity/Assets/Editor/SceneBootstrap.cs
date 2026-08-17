using System;
using Hellfire.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hellfire.EditorTools
{
    /// <summary>
    /// Procedural scene construction (GDD §3): scenes are BUILT, never hand-edited
    /// — .unity/.prefab YAML stays machine-generated so the repo remains a text
    /// project a fresh session can rebuild from the spec.
    ///
    /// CLI: Unity -batchmode -quit -projectPath unity -executeMethod Hellfire.EditorTools.SceneBootstrap.Build
    /// </summary>
    public static class SceneBootstrap
    {
        public static void Build()
        {
            try
            {
                BuildInner();
                Debug.Log("[SceneBootstrap] OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneBootstrap] FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void BuildInner()
        {
            EnsureFolder("Assets/Settings");
            EnsureFolder("Assets/Scenes");

            // --- URP pipeline assets, assigned project-wide. ---
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, "Assets/Settings/HellfireRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            pipeline.supportsHDR = true;
            AssetDatabase.CreateAsset(pipeline, "Assets/Settings/HellfireURP.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            // --- Default doctrine asset. ---
            var doctrine = ScriptableObject.CreateInstance<DoctrineAsset>();
            AssetDatabase.CreateAsset(doctrine, "Assets/Settings/DefaultDoctrine.asset");

            // --- Scene. ---
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            cam.orthographic = true;
            cam.orthographicSize = 290f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 600f;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            camGo.transform.position = new Vector3(256f, 300f, 256f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;

            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(60f, 30f, 0f);

            // --- Post stack: global volume with bloom + vignette. ---
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Settings/HellfirePost.asset");
            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0.8f);
            bloom.threshold.Override(0.9f);
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.25f);
            EditorUtility.SetDirty(profile);
            var volumeGo = new GameObject("PostVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // --- Swarm rig. ---
            var swarmGo = new GameObject("Swarm");
            var driver = swarmGo.AddComponent<SimDriver>();
            driver.doctrine = doctrine;
            var explosions = swarmGo.AddComponent<ExplosionPool>();
            var swarmRenderer = swarmGo.AddComponent<SwarmRenderer>();
            swarmRenderer.explosions = explosions;
            swarmGo.AddComponent<FieldRenderer>();

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var slash = path.LastIndexOf('/');
                AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
            }
        }
    }
}
