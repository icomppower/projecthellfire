using System;
using System.Collections.Generic;
using Hellfire.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hellfire.EditorTools
{
    /// <summary>
    /// Procedural scene construction (GDD §3): scenes are BUILT, never hand-edited
    /// — .unity/.prefab YAML stays machine-generated so the repo remains a text
    /// project a fresh session can rebuild from the spec.
    ///
    /// Step 7 presentation overhaul: perspective camera, dusk sky + fog, terrain,
    /// Kenney CC0 meshes (craft/turret/dish) baked into instancing-ready assets,
    /// engagement-legibility layer (tracers, labels, kill feed).
    ///
    /// CLI: Unity -batchmode -quit -projectPath unity -executeMethod Hellfire.EditorTools.SceneBootstrap.Build
    /// </summary>
    public static class SceneBootstrap
    {
        [MenuItem("Hellfire/Rebuild Scene")]
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
            pipeline.shadowDistance = 900f;
            AssetDatabase.CreateAsset(pipeline, "Assets/Settings/HellfireURP.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            // --- Default doctrine asset. ---
            var doctrine = ScriptableObject.CreateInstance<DoctrineAsset>();
            AssetDatabase.CreateAsset(doctrine, "Assets/Settings/DefaultDoctrine.asset");

            // --- Materials as ASSETS: a player build only includes shaders that
            // some built asset references. Runtime Shader.Find returns null in a
            // player (v1.1's black-screen bug); creating the materials here, at
            // editor time, and referencing them from the scene ships the shaders.
            var agentMat = MakeTransparentUnlit("Assets/Settings/AgentMat.mat", 3000, additive: false);
            var fieldMat = MakeTransparentUnlit("Assets/Settings/FieldMat.mat", 2900, additive: false);
            var tracerMat = MakeTransparentUnlit("Assets/Settings/TracerMat.mat", 3100, additive: true);
            var boomMat = MakeParticleAdditive("Assets/Settings/BoomMat.mat");

            // --- Kenney CC0 meshes baked into instancing-ready assets. ---
            var craft = BakeModel("Assets/Models/Kenney/craft_speederA.fbx",
                "Assets/Settings/CraftMesh.asset", "Craft");
            var turret = BakeModel("Assets/Models/Kenney/turret_double.fbx",
                "Assets/Settings/TurretMesh.asset", "Turret");
            var dish = BakeModel("Assets/Models/Kenney/satelliteDish_large.fbx",
                "Assets/Settings/DishMesh.asset", "Dish");

            // --- Scene. ---
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Perspective command camera: south of the field, tilted north over it.
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            cam.orthographic = false;
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 5f;
            cam.farClipPlane = 2500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.transform.position = new Vector3(256f, 330f, -190f);
            camGo.transform.rotation = Quaternion.Euler(43f, 0f, 0f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;

            // Dusk sky + matching haze.
            var skyShader = Shader.Find("Skybox/Procedural");
            var skyMat = new Material(skyShader);
            skyMat.SetFloat("_SunSize", 0.045f);
            skyMat.SetFloat("_AtmosphereThickness", 0.62f);
            skyMat.SetColor("_SkyTint", new Color(0.46f, 0.5f, 0.66f));
            skyMat.SetColor("_GroundColor", new Color(0.27f, 0.24f, 0.22f));
            skyMat.SetFloat("_Exposure", 1.15f);
            AssetDatabase.CreateAsset(skyMat, "Assets/Settings/SkyMat.mat");
            RenderSettings.skybox = skyMat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 500f;
            RenderSettings.fogEndDistance = 1500f;
            RenderSettings.fogColor = new Color(0.5f, 0.44f, 0.44f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.4f, 0.41f, 0.48f);

            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.89f, 0.74f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, 195f, 0f);
            RenderSettings.sun = light;

            // --- Terrain: textured ground plane + low-poly border ridges. ---
            var groundTex = MakeGroundTexture("Assets/Settings/GroundTex.asset");
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMat.SetColor("_BaseColor", new Color(0.62f, 0.58f, 0.5f));
            groundMat.SetTexture("_BaseMap", groundTex);
            groundMat.SetTextureScale("_BaseMap", new Vector2(7f, 7f));
            groundMat.SetFloat("_Smoothness", 0.06f);
            AssetDatabase.CreateAsset(groundMat, "Assets/Settings/GroundMat.mat");
            var groundMesh = MakeGroundMesh("Assets/Settings/GroundMesh.asset");
            var groundGo = new GameObject("Ground");
            groundGo.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundRenderer = groundGo.AddComponent<MeshRenderer>();
            groundRenderer.sharedMaterial = groundMat;
            groundRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var hillMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            hillMat.SetColor("_BaseColor", new Color(0.36f, 0.33f, 0.3f));
            hillMat.SetFloat("_Smoothness", 0.04f);
            AssetDatabase.CreateAsset(hillMat, "Assets/Settings/HillMat.mat");
            var hillMesh = MakeHillMesh("Assets/Settings/HillMesh.asset");
            var hillGo = new GameObject("BorderRidges");
            hillGo.AddComponent<MeshFilter>().sharedMesh = hillMesh;
            hillGo.AddComponent<MeshRenderer>().sharedMaterial = hillMat;

            // --- Post stack: bloom (HDR tracers/explosions feed it) + tonemap. ---
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Settings/HellfirePost.asset");
            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(1.1f);
            bloom.threshold.Override(1.05f);
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.28f);
            var tonemap = profile.Add<Tonemapping>();
            tonemap.mode.Override(TonemappingMode.ACES);
            var colors = profile.Add<ColorAdjustments>();
            colors.saturation.Override(8f);
            colors.contrast.Override(6f);
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
            explosions.particleMaterial = boomMat;
            var engagement = swarmGo.AddComponent<EngagementRenderer>();
            engagement.material = tracerMat;
            engagement.muzzleHeight = turret.Mesh.bounds.max.y * FieldRenderer.TurretSize * 0.85f;
            var swarmRenderer = swarmGo.AddComponent<SwarmRenderer>();
            swarmRenderer.explosions = explosions;
            swarmRenderer.material = agentMat;
            swarmRenderer.engagement = engagement;
            swarmRenderer.craftMesh = craft.Mesh;
            swarmRenderer.craftMaterials = craft.Materials;
            swarmRenderer.accentSubmesh = craft.AccentIndex;
            var fieldRenderer = swarmGo.AddComponent<FieldRenderer>();
            fieldRenderer.material = fieldMat;
            fieldRenderer.turretMesh = turret.Mesh;
            fieldRenderer.turretMaterials = turret.Materials;
            fieldRenderer.dishMesh = dish.Mesh;
            fieldRenderer.dishMaterials = dish.Materials;
            var labels = swarmGo.AddComponent<TacticalLabels>();
            labels.engagement = engagement;
            swarmGo.AddComponent<CommanderUI>();

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
            AssetDatabase.SaveAssets();
        }

        private readonly struct BakedModel
        {
            public readonly Mesh Mesh;
            public readonly Material[] Materials;
            public readonly int AccentIndex;
            public BakedModel(Mesh mesh, Material[] mats, int accent)
            {
                Mesh = mesh; Materials = mats; AccentIndex = accent;
            }
        }

        /// <summary>
        /// Bakes an imported FBX into one mesh asset (one submesh per source
        /// material, grouped by material name) plus instancing-enabled URP Lit
        /// material assets carrying the source flat colors. Normalized so the
        /// larger horizontal extent is 1 and the base sits at y = 0 — renderers
        /// scale by a single size constant. The imported FBX materials are never
        /// used at runtime (no instancing, importer-owned); these copies are.
        /// </summary>
        private static BakedModel BakeModel(string fbxPath, string meshAssetPath, string matPrefix)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (root == null) throw new InvalidOperationException($"model missing: {fbxPath}");

            var groups = new List<KeyValuePair<Material, List<CombineInstance>>>();
            var groupIndex = new Dictionary<string, int>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var meshRenderer = filter.GetComponent<MeshRenderer>();
                if (meshRenderer == null || filter.sharedMesh == null) continue;
                var sourceMesh = filter.sharedMesh;
                for (int s = 0; s < sourceMesh.subMeshCount; s++)
                {
                    var mat = meshRenderer.sharedMaterials[
                        Math.Min(s, meshRenderer.sharedMaterials.Length - 1)];
                    string key = mat != null ? mat.name : "default";
                    if (!groupIndex.TryGetValue(key, out int g))
                    {
                        groupIndex[key] = g = groups.Count;
                        groups.Add(new KeyValuePair<Material, List<CombineInstance>>(
                            mat, new List<CombineInstance>()));
                    }
                    groups[g].Value.Add(new CombineInstance
                    {
                        mesh = sourceMesh,
                        subMeshIndex = s,
                        transform = filter.transform.localToWorldMatrix,
                    });
                }
            }
            if (groups.Count == 0) throw new InvalidOperationException($"no geometry in {fbxPath}");

            var perGroup = new CombineInstance[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                var groupMesh = new Mesh();
                groupMesh.CombineMeshes(groups[g].Value.ToArray(), true, true);
                perGroup[g] = new CombineInstance { mesh = groupMesh, transform = Matrix4x4.identity };
            }
            var combined = new Mesh { name = matPrefix + "Baked" };
            combined.CombineMeshes(perGroup, false, true);

            // Normalize: center XZ, base at y=0, larger horizontal extent = 1.
            combined.RecalculateBounds();
            var b = combined.bounds;
            float extent = Mathf.Max(b.size.x, b.size.z);
            if (extent < 1e-5f) extent = 1f;
            float inv = 1f / extent;
            var verts = combined.vertices;
            var offset = new Vector3(b.center.x, b.min.y, b.center.z);
            for (int i = 0; i < verts.Length; i++) verts[i] = (verts[i] - offset) * inv;
            combined.vertices = verts;
            combined.RecalculateBounds();
            AssetDatabase.CreateAsset(combined, meshAssetPath);

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            var mats = new Material[groups.Count];
            int accent = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                var src = groups[g].Key;
                var color = Color.white;
                if (src != null)
                {
                    if (src.HasProperty("_BaseColor")) color = src.GetColor("_BaseColor");
                    else if (src.HasProperty("_Color")) color = src.GetColor("_Color");
                }
                var mat = new Material(litShader) { enableInstancing = true };
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", 0.25f);
                mat.SetFloat("_Metallic", 0.15f);
                AssetDatabase.CreateAsset(mat, $"Assets/Settings/{matPrefix}Sub{g}.mat");
                mats[g] = mat;
                if (src != null && src.name.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    accent = g;
                }
            }
            return new BakedModel(combined, mats, accent);
        }

        /// <summary>Seeded value-noise desert texture — editor-time only, so plain
        /// System.Random is fine (nothing here touches the sim's determinism).</summary>
        private static Texture2D MakeGroundTexture(string path)
        {
            const int size = 512;
            var rng = new System.Random(20260817);
            // Coarse noise lattice, bilinear-sampled.
            const int lattice = 33;
            var cells = new float[lattice, lattice];
            for (int y = 0; y < lattice; y++)
                for (int x = 0; x < lattice; x++)
                    cells[x, y] = (float)rng.NextDouble();

            var tex = new Texture2D(size, size, TextureFormat.RGB24, true)
            {
                name = "GroundTex",
                wrapMode = TextureWrapMode.Repeat,
            };
            var baseA = new Color(0.52f, 0.47f, 0.38f);
            var baseB = new Color(0.42f, 0.4f, 0.34f);
            var speck = new Color(0.33f, 0.31f, 0.28f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x * (lattice - 1f) / size;
                    float v = y * (lattice - 1f) / size;
                    int x0 = (int)u, y0 = (int)v;
                    float fu = u - x0, fv = v - y0;
                    float n = Mathf.Lerp(
                        Mathf.Lerp(cells[x0, y0], cells[x0 + 1, y0], fu),
                        Mathf.Lerp(cells[x0, y0 + 1], cells[x0 + 1, y0 + 1], fu), fv);
                    var c = Color.Lerp(baseA, baseB, n);
                    // Fine grain.
                    float g = (float)rng.NextDouble();
                    if (g > 0.985f) c = speck;
                    else c *= 0.96f + 0.08f * g;
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        private static Mesh MakeGroundMesh(string path)
        {
            const float lo = -600f, hi = 1112f;
            var mesh = new Mesh
            {
                name = "Ground",
                vertices = new[]
                {
                    new Vector3(lo, 0f, lo), new Vector3(hi, 0f, lo),
                    new Vector3(lo, 0f, hi), new Vector3(hi, 0f, hi),
                },
                uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Low-poly ridge cones ringing the field (skipping the south
        /// sector where the camera sits) — horizon depth for the tilted view.</summary>
        private static Mesh MakeHillMesh(string path)
        {
            var rng = new System.Random(7);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var center = new Vector3(256f, 0f, 256f);
            for (int k = 0; k < 46; k++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float radius = 560f + (float)rng.NextDouble() * 320f;
                var pos = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (pos.z < 60f) continue; // keep the camera's southern approach clear
                float height = 30f + (float)rng.NextDouble() * 75f;
                float baseR = height * (1.6f + (float)rng.NextDouble() * 1.4f);
                int segments = 7;
                int apex = verts.Count;
                verts.Add(pos + Vector3.up * height);
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    float wobble = 0.75f + (float)rng.NextDouble() * 0.5f;
                    verts.Add(pos + new Vector3(Mathf.Cos(a) * baseR * wobble, 0f, Mathf.Sin(a) * baseR * wobble));
                }
                for (int s = 0; s < segments; s++)
                {
                    tris.Add(apex);
                    tris.Add(apex + 1 + (s + 1) % segments);
                    tris.Add(apex + 1 + s);
                }
            }
            var mesh = new Mesh { name = "BorderRidges", vertices = verts.ToArray(), triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>URP Unlit configured for transparency — the full
        /// keyword/blend/tag set; _Surface alone is not sufficient. Additive is
        /// the tracer/glow variant (SrcAlpha/One).</summary>
        private static Material MakeTransparentUnlit(string path, int renderQueue, bool additive)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                enableInstancing = true,
            };
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = renderQueue;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Additive particle material — explosions should glow, not blot.</summary>
        private static Material MakeParticleAdditive(string path)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3050;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
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
