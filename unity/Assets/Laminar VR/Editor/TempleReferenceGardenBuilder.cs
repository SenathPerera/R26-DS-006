using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LaminarVR.AdaptiveMeditation.Editor
{
    // Authoring only: the result is ordinary static meshes, with no runtime generator.
    public static class TempleReferenceGardenBuilder
    {
        public const string Output = "Assets/Laminar VR/Art/TemplePond/ReferenceGarden";
        private static readonly Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
        private static System.Random random;

        [MenuItem("Adaptive Meditation/Rebuild Temple Reference Garden Accents")]
        public static void Build()
        {
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            ConfigureWeatheredWalls();
            batches.Clear();
            random = new System.Random(26018);

            // Distant landscape stays beyond the garden walls and never moves.
            var mountain = Group("Continuous Mountain", new Color(.64f,.66f,.70f));
            mountain.material.shader = Shader.Find("LaminarVR/Temple Mountain Vertex Lit");
            Mountain(mountain);

            var jade = Group("Sculptural Jade Rocks", new Color(0.53f, 0.54f, 0.53f));
            TorusRock(jade, new Vector3(6.4f, 3.3f, 26.6f));
            Rock(jade, new Vector3(5.2f, 0.9f, 26.8f), new Vector3(1.6f, 2.8f, 1.3f), 10, 6);
            Rock(jade, new Vector3(8.1f, 0.15f, 23.7f), new Vector3(1.1f, 1.3f, 0.9f), 9, 5);
            Rock(jade, new Vector3(21.9f, 0.1f, 19.2f), new Vector3(1.3f, 1.1f, 1f), 9, 5);

            // The wall albedo now supplies the weathered masonry base, without overlay boxes.

            // Opaque folded leaves avoid another large alpha-blended vegetation layer.
            for (var side = 0; side < 2; side++)
            {
                var dark = Group((side == 0 ? "Left" : "Right") + " Deep Shrubs", new Color(0.18f, 0.30f, 0.12f), true);
                var green = Group((side == 0 ? "Left" : "Right") + " Fresh Shrubs", new Color(0.31f, 0.46f, 0.17f), true);
                var tips = Group((side == 0 ? "Left" : "Right") + " Sunlit Leaves", new Color(0.46f, 0.57f, 0.23f), true);
                // Authored world-space curves follow the OUTSIDE of the pond coping.
                // Keep the stone lip readable and the bridge/pond free of shrubs.
                var bank = side == 0 ? new[] {
                    new Vector3(11.0f,.64f,3), new Vector3(10.1f,.64f,5),
                    new Vector3(8.4f,.64f,7), new Vector3(5.2f,.64f,9.5f),
                    new Vector3(3.4f,.64f,13), new Vector3(3.35f,.64f,17),
                    new Vector3(3.35f,.64f,21) } : new[] {
                    new Vector3(19.5f,.62f,3), new Vector3(20.3f,.62f,5),
                    new Vector3(22.95f,.62f,8), new Vector3(25.5f,.62f,11),
                    new Vector3(26.35f,.62f,15), new Vector3(25.45f,.62f,19),
                    new Vector3(23.9f,.62f,23), new Vector3(23.85f,.62f,26) };
                PlantBorder(dark, green, tips, bank);
                var terrace = side == 0 ? new[] {
                    new Vector3(2.0f,1.4f,23.0f), new Vector3(4.3f,1.4f,23.5f),
                    new Vector3(6.8f,1.4f,24.7f), new Vector3(9.0f,1.4f,26.4f),
                    new Vector3(10.1f,1.4f,28.2f) } : new[] {
                    new Vector3(19.6f,1.48f,29.0f), new Vector3(23.5f,1.48f,29.0f),
                    new Vector3(27.8f,1.48f,29.0f) };
                PlantBorder(dark, green, tips, terrace);
                foreach (var basePosition in side == 0 ? new[] {
                    new Vector3(3.9f,1.4f,25.34f), new Vector3(4.37f,.3f,4.83f) } : new[] {
                    new Vector3(25.19f,.62f,25.63f), new Vector3(26.83f,.45f,7.18f) })
                    for (var i = 0; i < 5; i++)
                    {
                        var a = i * Mathf.PI * 2f / 5f;
                        Bush(dark, green, tips, basePosition + new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)) * .9f, .65f);
                    }
            }

            var bark = Group("Sculpted Tree Branches", new Color(.46f,.33f,.22f));
            var leafShade = Group("Tree Canopy Shade", new Color(.19f,.34f,.13f), true);
            var leafMid = Group("Tree Canopy Green", new Color(.32f,.49f,.18f), true);
            var leafLight = Group("Tree Canopy Light", new Color(.48f,.61f,.25f), true);
            Tree(bark, leafShade, leafMid, leafLight, new Vector3(3.9052f,.64f,25.343f), 1.05f, -18);
            Tree(bark, leafShade, leafMid, leafLight, new Vector3(4.3723f,.27f,4.8326f), 1.10f, 38);
            Tree(bark, leafShade, leafMid, leafLight, new Vector3(25.19f,.4f,25.63f), 1.05f, 155);
            Tree(bark, leafShade, leafMid, leafLight, new Vector3(26.83f,.43f,7.18f), 1.20f, 202);
            // Three larger trees stand beyond the courtyard walls. The seat tree is removed.
            Tree(bark, leafShade, leafMid, leafLight, new Vector3(-5f,0,27f), 2.4f, -18);
            Tree(bark, leafShade, leafMid, leafLight, new Vector3(35f,0,29f), 2.65f, 155);
            Tree(bark, leafShade, leafMid, leafLight, new Vector3(18f,0,50f), 3.2f, -8);

            var cabinWood = Group("Meditation Cabin Timber", new Color(.39f,.26f,.17f));
            var cabinRoof = Group("Meditation Cabin Roof", new Color(.25f,.27f,.29f));
            var screens = Group("Meditation Cabin Screens", new Color(.77f,.71f,.57f));
            var doorway = Group("Temple Doorway", new Color(.28f,.17f,.10f));
            Cabin(cabinWood,cabinRoof,screens);
            Doorway(doorway,cabinWood,screens);

            var pads = Group("Lotus Pads", new Color(0.28f, 0.48f, 0.16f), true);
            var flowers = Group("Ivory Lotus Petals", new Color(0.89f, 0.79f, 0.44f), true);
            var centers = Group("Lotus Golden Centers", new Color(0.73f, 0.48f, 0.12f));
            var padPositions = new[] { new Vector3(8.8f, 0.09f, 12f), new Vector3(7f, 0.09f, 18.5f),
                new Vector3(10.2f, 0.09f, 21.2f), new Vector3(20.8f, 0.09f, 10f),
                new Vector3(22f, 0.09f, 16.5f), new Vector3(20f, 0.09f, 23f) };
            foreach (var p in padPositions)
            {
                Lily(pads, p, 0.58f);
                Lily(pads, p + new Vector3(0.85f, 0.01f, 0.25f), 0.4f);
                Lily(pads, p + new Vector3(-0.35f, 0.015f, 0.8f), 0.32f);
                for (var petal = 0; petal < 9; petal++)
                {
                    var a = petal * Mathf.PI * 2f / 9f;
                    var direction = new Vector3(Mathf.Cos(a), 0.55f, Mathf.Sin(a));
                    Leaf(flowers, p + Vector3.up * 0.08f, direction, 0.4f, 0.16f);
                }
                Rock(centers, p + Vector3.up * 0.09f, new Vector3(0.12f, 0.17f, 0.12f), 8, 3);
            }

            var timber = Group("Lantern Timber", new Color(0.24f, 0.16f, 0.1f));
            var paper = Group("Lantern Warm Paper", new Color(1f, 0.71f, 0.32f), false, true);
            foreach (var p in new[] {new Vector3(12.35f,0.42f,9f),new Vector3(17.85f,0.42f,9f),
                new Vector3(12.35f,0.42f,22f),new Vector3(17.85f,0.42f,22f)})
                Lantern(timber, paper, p);

            WallFoliage(bark);

            var root = new GameObject("Reference Garden Accents");
            try
            {
                foreach (var pair in batches)
                {
                    var batch = pair.Value;
                    var meshPath = Output + "/" + pair.Key + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (mesh == null)
                    {
                        mesh = new Mesh { name = pair.Key };
                        AssetDatabase.CreateAsset(mesh, meshPath);
                    }
                    // Update through Mesh APIs so both saved data and GPU buffers refresh.
                    // CopySerialized can leave an already loaded mesh rendering old geometry.
                    mesh.Clear();
                    mesh.SetVertices(batch.vertices);
                    mesh.SetTriangles(batch.indices, 0);
                    if (batch.colors.Count > 0) mesh.SetColors(batch.colors);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    EditorUtility.SetDirty(mesh);
                    var child = new GameObject(pair.Key);
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = batch.material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    GameObjectUtility.SetStaticEditorFlags(child, StaticEditorFlags.BatchingStatic);
                }
                PrefabUtility.SaveAsPrefabAsset(root, Output + "/TempleReferenceGarden.prefab");
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); batches.Clear(); }
        }

        private static Batch Group(string name, Color color, bool twoSided = false, bool unlit = false)
        {
            var path = Output + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetColor("_SpecColor", Color.black);
            mat.SetFloat("_Cull", twoSided ? 0f : 2f);
            // Each category is already one combined draw; SRP batching handles materials.
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            var batch = new Batch { material = mat };
            batches.Add(name, batch);
            return batch;
        }

        private static float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        private static void ConfigureWeatheredWalls()
        {
            const string texturePath = "Assets/Laminar VR/Art/TemplePond/Textures/Wall/T_TempleWeatheredPlaster.png";
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing weathered wall texture: " + texturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 1024;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Laminar VR/Art/TemplePond/Materials/Wall/M_Wall.mat");
            var shader = Shader.Find("LaminarVR/Temple Weathered Wall");
            if (material == null || shader == null) throw new InvalidOperationException("Missing Temple wall material or shader.");
            material.shader = shader;
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_TextureWorldWidth",5f);
            material.SetFloat("_WallHeight",5.4f);
            EditorUtility.SetDirty(material);
        }

        private static void WallFoliage(Batch stems)
        {
            var leaves = Group("Trailing Wall Foliage",new Color(.16f,.29f,.10f),true);
            var tips = Group("Wall Foliage Tips",new Color(.32f,.43f,.16f),true);
            // Sparse patches leave most plaster exposed and keep foliage well away from the seat.
            foreach(var z in new[] {8f,16f,24f})
                HangingPatch(stems,leaves,tips,new Vector3(.96f,5.25f,z),Vector3.forward,Vector3.right);
            foreach(var z in new[] {10.5f,19.5f,26f})
                HangingPatch(stems,leaves,tips,new Vector3(28.92f,5.25f,z),Vector3.forward,Vector3.left);
            HangingPatch(stems,leaves,tips,new Vector3(4.60f,5.95f,30.64f),Vector3.right,Vector3.back);
            HangingPatch(stems,leaves,tips,new Vector3(25.41f,5.98f,30.64f),Vector3.right,Vector3.back);
        }

        private static void HangingPatch(Batch stems, Batch leaves, Batch tips, Vector3 top, Vector3 along, Vector3 outward)
        {
            for(var strand=0;strand<4;strand++)
            {
                var length = Range(1.35f,2.45f);
                var start = top+along*((strand-1.5f)*.34f+Range(-.12f,.12f));
                var last = start;
                for(var node=0;node<8;node++)
                {
                    var t = node/7f;
                    var p = start + Vector3.down*(length*t+Range(-.04f,.04f)) + along*(Mathf.Sin(t*7+strand)*.21f)
                        + outward*(.035f+.10f*Mathf.Sin(t*Mathf.PI));
                    if(node>0)
                    {
                        if(Vector3.Dot(Vector3.Cross(along,Vector3.down),outward)>0)
                            stems.Quad(last-along*.012f,last+along*.012f,p+along*.009f,p-along*.009f);
                        else
                            stems.Quad(last+along*.012f,last-along*.012f,p-along*.009f,p+along*.009f);
                    }
                    for(var side=-1;side<=1;side+=2)
                    {
                        var direction = (along*(side*Range(.35f,1f))+Vector3.down*Range(.3f,1f)).normalized;
                        var width = Vector3.Cross(outward,direction).normalized;
                        var leafLength = Range(.24f,.4f)*(1-.25f*t);
                        var origin = p + outward*Range(.01f,.07f)+Vector3.up*Range(-.08f,.08f);
                        var middle = origin+direction*leafLength*.5f+outward*.02f;
                        var end = origin+direction*leafLength;
                        var broad = width*leafLength*Range(.32f,.44f);
                        var shoulder = origin+direction*leafLength*.23f;
                        var lower = origin+direction*leafLength*.65f;
                        var batch = (node+strand+side)%4==0 ? tips : leaves;
                        // An oval blade with a small central fold and varied angle breaks
                        // the regular paired-leaf pattern without transparent texture cards.
                        var edge = new[] {origin,shoulder-broad*.8f,lower-broad,end,lower+broad,shoulder+broad*.8f};
                        for(var i=0;i<edge.Length;i++) batch.Triangle(middle,edge[i],edge[(i+1)%edge.Length]);
                    }
                    last = p;
                }
            }
        }

        private static void Bush(Batch dark, Batch green, Batch tips, Vector3 center, float size)
        {
            for (var i = 0; i < 20; i++)
            {
                var a = Range(0, Mathf.PI * 2f);
                var radius = Range(0.08f, size);
                var y = Range(0.05f, 0.7f) * size * (1f - radius / size * 0.3f);
                var origin = center + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
                Leaf(i % 5 == 0 ? tips : (i % 2 == 0 ? dark : green), origin,
                    new Vector3(Mathf.Cos(a), Range(0.2f, 0.9f), Mathf.Sin(a)), Range(0.24f, 0.42f), Range(0.11f, 0.18f));
            }
        }

        private static void PlantBorder(Batch dark, Batch green, Batch tips, Vector3[] path)
        {
            for (var segment = 1; segment < path.Length; segment++)
            {
                var steps = Mathf.CeilToInt(Vector3.Distance(path[segment - 1], path[segment]) / 1.25f);
                for (var i = 0; i < steps; i++)
                    Bush(dark, green, tips, Vector3.Lerp(path[segment - 1], path[segment], (float)i / steps), Range(.48f,.65f));
            }
        }

        private static void Mountain(Batch batch)
        {
            // One connected heightfield with shared vertices: a broad massif, not six cones.
            const int columns = 48, rows = 28;
            for (var z = 0; z <= rows; z++)
                for (var x = 0; x <= columns; x++)
                {
                    var u = (float)x / columns;
                    var v = (float)z / rows;
                    var px = Mathf.Lerp(-70,100,u);
                    var pz = Mathf.Lerp(62,160,v);
                    var dx = (px - 8) / 58;
                    var dz = (pz - 112) / 38;
                    var envelope = Mathf.Pow(Mathf.Max(0, 1 - dx*dx - dz*dz), 1.1f);
                    var ridge = 1 + .12f*Mathf.Sin(px*.12f + pz*.08f) + .10f*Mathf.Sin(px*.31f - pz*.17f);
                    batch.vertices.Add(new Vector3(px, -4 + 65*envelope*ridge, pz));
                    var strata = Mathf.Clamp01(.50f + .24f*Mathf.Sin(px*.31f-pz*.17f)
                        + .10f*Mathf.Sin(px*.71f+pz*.25f));
                    var rockColor = Color.Lerp(new Color(.42f,.45f,.49f), new Color(.75f,.74f,.71f), strata);
                    batch.colors.Add(Color.Lerp(new Color(.43f,.47f,.43f),rockColor,Mathf.Clamp01(envelope*2)));
                }
            for (var z = 0; z < rows; z++)
                for (var x = 0; x < columns; x++)
                {
                    var a = z*(columns+1)+x;
                    batch.indices.AddRange(new[] { a,a+columns+1,a+1, a+1,a+columns+1,a+columns+2 });
                }
        }

        private static void Tree(Batch wood, Batch shade, Batch green, Batch light, Vector3 root, float scale, float yaw)
        {
            var rotation = Quaternion.Euler(0,yaw,0);
            Func<Vector3,Vector3> world = p => root + rotation*p*scale;
            var spine = new[] { Vector3.zero, new Vector3(-.45f,1.4f,.1f),
                new Vector3(-.32f,2.8f,.05f), new Vector3(.32f,4.2f,.12f),
                new Vector3(.45f,5.6f,.05f), new Vector3(.05f,7.1f,.2f) };
            for (var i = 1; i < spine.Length; i++)
                for (var segment = 0; segment < 3; segment++)
                {
                    var p0 = spine[Mathf.Max(0,i-2)]; var p1 = spine[i-1];
                    var p2 = spine[i]; var p3 = spine[Mathf.Min(spine.Length-1,i+1)];
                    var t0 = segment/3f; var t1 = (segment+1)/3f;
                    Branch(wood,world(Curve(p0,p1,p2,p3,t0)),world(Curve(p0,p1,p2,p3,t1)),
                        Mathf.Lerp(.22f,.055f,(i-1+t0)/5f)*scale,Mathf.Lerp(.22f,.055f,(i-1+t1)/5f)*scale);
                }
            // Alternating lateral limbs leave clear air between horizontal foliage tiers.
            var tips = new[] { new Vector3(-2.0f,2.8f,.1f), new Vector3(1.7f,3.5f,.45f),
                new Vector3(-2.25f,4.6f,-.2f), new Vector3(2.35f,5.0f,.2f),
                new Vector3(-1.3f,5.8f,1.15f), new Vector3(.8f,6.2f,-1.5f),
                new Vector3(-1.7f,6.9f,-.45f), new Vector3(1.65f,7.1f,.35f),
                new Vector3(.05f,7.6f,.3f) };
            for (var i = 0; i < tips.Length; i++)
            {
                var tip = tips[i];
                var start = spine[Mathf.Clamp(i/2+1,1,4)];
                var elbow = Vector3.Lerp(start,tip,.53f) - Vector3.up*.3f;
                Branch(wood,world(start),world(elbow),.095f*scale,.055f*scale);
                Branch(wood,world(elbow),world(tip),.055f*scale,.012f*scale);
                // Small fork visible below each airy leaf pad.
                var fork = tip + new Vector3(.4f,.18f,.65f);
                Branch(wood,world(elbow),world(fork),.035f*scale,.009f*scale);
                CanopyCore(green,world(tip+Vector3.up*.12f),new Vector3(.8f,.28f,.6f)*scale,rotation);
                CanopyCore(green,world(tip+new Vector3(.5f,.26f,.25f)),new Vector3(.55f,.2f,.4f)*scale,rotation);
                CanopyCore(green,world(tip+new Vector3(-.48f,.18f,-.2f)),new Vector3(.58f,.24f,.44f)*scale,rotation);
                for (var leaf = 0; leaf < 144; leaf++)
                {
                    var angle = Range(0,Mathf.PI*2);
                    var radius = Mathf.Sqrt(Range(0,1));
                    var offset = new Vector3(Mathf.Cos(angle)*radius*1.16f,
                        Range(-.2f,.58f)*(1-radius*.45f), Mathf.Sin(angle)*radius*.82f);
                    var direction = rotation*new Vector3(Mathf.Cos(angle),Range(-.4f,.9f),Mathf.Sin(angle));
                    TreeLeaf(leaf % 5 == 0 ? light : leaf % 3 == 0 ? shade : green,
                        world(tip+offset),direction,Range(.25f,.43f)*scale,Range(.075f,.14f)*scale);
                }
            }
        }

        private static Vector3 Curve(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            return .5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
        }

        private static void Cabin(Batch wood, Batch roof, Batch screens)
        {
            // The existing platform top is y=1.94. Keep its stairs and the XR pose intact.
            const float centerX = 15.13f, backZ = 1.04f, frontZ = 3.02f;
            foreach (var x in new[] {centerX-3.4f,centerX+3.4f})
            {
                foreach (var z in new[] {backZ,frontZ})
                {
                    Box(wood,new Vector3(x,3.85f,z),new Vector3(.29f,3.82f,.29f));
                    Box(wood,new Vector3(x,2.04f,z),new Vector3(.43f,.2f,.43f));
                    Box(wood,new Vector3(x,5.57f,z),new Vector3(.48f,.22f,.48f));
                }
                // Low side walls and slim upper lattice leave an airy, open-front shelter.
                Box(screens,new Vector3(x,2.57f,2.03f),new Vector3(.09f,1.05f,1.7f));
                foreach(var y in new[] {2.0f,3.14f,5.5f})
                    Box(wood,new Vector3(x,y,2.03f),new Vector3(.18f,.13f,2.0f));
                for (var i = 0; i < 5; i++)
                    Box(wood,new Vector3(x,4.36f,1.22f+i*.4f),new Vector3(.075f,2.28f,.075f));
            }
            // The existing boundary wall forms the rear enclosure. A shallow timber screen
            // sits on its garden side instead of placing the camera inside new solid walls.
            Box(screens,new Vector3(centerX,3.65f,1.01f),new Vector3(6.45f,3.2f,.08f));
            for (var i = -4; i <= 4; i++)
                Box(wood,new Vector3(centerX+i*.78f,3.65f,1.09f),new Vector3(.07f,3.2f,.06f));
            foreach (var y in new[] {2.02f,3.12f,4.22f,5.27f})
                Box(wood,new Vector3(centerX,y,1.1f),new Vector3(6.65f,.1f,.09f));

            foreach(var z in new[] {backZ,frontZ})
                Box(wood,new Vector3(centerX,5.62f,z),new Vector3(7.12f,.24f,.3f));
            // Extruded curved gable roof, with an opaque timber underside and capped fascia.
            var profile = new[] {new Vector2(-4.15f,6.0f),new Vector2(-3.4f,5.76f),
                new Vector2(-2.25f,5.98f),new Vector2(-1.15f,6.55f),new Vector2(0,7.35f),
                new Vector2(1.15f,6.55f),new Vector2(2.25f,5.98f),new Vector2(3.4f,5.76f),new Vector2(4.15f,6.0f)};
            const float roofBack = .16f, roofFront = 3.92f;
            for(var i=1;i<profile.Length;i++)
            {
                var a = new Vector3(centerX+profile[i-1].x,profile[i-1].y,roofBack);
                var b = new Vector3(centerX+profile[i].x,profile[i].y,roofBack);
                var c = new Vector3(b.x,b.y,roofFront); var d = new Vector3(a.x,a.y,roofFront);
                var drop = Vector3.down*.18f;
                roof.Quad(a,d,c,b);
                wood.Quad(a+drop,b+drop,c+drop,d+drop);
                wood.Quad(d,d+drop,c+drop,c);
                wood.Quad(a,b,b+drop,a+drop);
                // Sparse raised tile ribs suggest roof courses without a tile texture array.
                for(var rib=0;rib<7;rib++)
                {
                    var z = Mathf.Lerp(roofBack,roofFront,rib/6f);
                    Branch(roof,new Vector3(a.x,a.y+.045f,z),new Vector3(b.x,b.y+.045f,z),.035f,.035f);
                }
            }
            foreach(var x in new[] {centerX-4.15f,centerX+4.15f})
                Box(wood,new Vector3(x,5.91f,2.04f),new Vector3(.12f,.18f,3.76f));
            Box(roof,new Vector3(centerX,7.38f,2.04f),new Vector3(.22f,.16f,4.02f));
        }

        private static void Doorway(Batch frame, Batch wood, Batch panels)
        {
            // The blank rear wall's front face is z=35.8165; layer the door on its near side.
            const float x = 15.1f, z = 35.65f, floor = 2.14f;
            Box(frame,new Vector3(x,4.59f,z),new Vector3(4.9f,4.9f,.16f));
            foreach(var side in new[] {-1f,1f})
            {
                var leafX = x+side*1.06f;
                Box(wood,new Vector3(leafX,4.55f,z-.11f),new Vector3(2.04f,4.56f,.09f));
                Box(panels,new Vector3(leafX,5.25f,z-.17f),new Vector3(1.67f,2.6f,.035f));
                // Timber lattice and lower panels make the closed double door readable.
                for(var slat=-2;slat<=2;slat++)
                    Box(frame,new Vector3(leafX+slat*.31f,5.25f,z-.21f),new Vector3(.055f,2.63f,.055f));
                foreach(var y in new[] {3.92f,4.8f,5.68f,6.55f})
                    Box(frame,new Vector3(leafX,y,z-.22f),new Vector3(1.74f,.07f,.06f));
                Box(frame,new Vector3(x+side*.19f,4.05f,z-.28f),new Vector3(.075f,.38f,.11f));
                Box(wood,new Vector3(x+side*2.39f,4.6f,z-.1f),new Vector3(.23f,5.0f,.32f));
            }
            Box(wood,new Vector3(x,floor,z-.18f),new Vector3(5.1f,.18f,.48f));
            Box(wood,new Vector3(x,7.12f,z-.1f),new Vector3(5.28f,.24f,.38f));
        }

        private static void TreeLeaf(Batch batch, Vector3 origin, Vector3 direction, float length, float width)
        {
            direction.Normalize();
            var side = Vector3.Cross(Vector3.up,direction).normalized*width;
            var middle = origin+direction*length*.45f;
            batch.Quad(origin,middle-side,origin+direction*length,middle+side);
        }

        private static void CanopyCore(Batch batch, Vector3 center, Vector3 size, Quaternion rotation)
        {
            // Small opaque foliage heart adds volume; individual leaves break up its edge.
            const int sides = 8;
            var start = batch.vertices.Count;
            for (var ring = 0; ring < 4; ring++)
                for (var i = 0; i < sides; i++)
                {
                    var latitude = Mathf.Lerp(-Mathf.PI*.5f,Mathf.PI*.5f,ring/3f);
                    var a = i*Mathf.PI*2/sides;
                    var radius = Mathf.Cos(latitude)*(1+.12f*Mathf.Sin(a*3));
                    batch.vertices.Add(center+rotation*Vector3.Scale(size,
                        new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(latitude),Mathf.Sin(a)*radius)));
                }
            for (var ring = 0; ring < 3; ring++)
                for (var i = 0; i < sides; i++)
                {
                    var a = start+ring*sides+i; var b = start+ring*sides+(i+1)%sides;
                    if (ring < 2) batch.indices.AddRange(new[] {a,a+sides,b+sides});
                    if (ring > 0) batch.indices.AddRange(new[] {a,b+sides,b});
                }
        }

        private static void Branch(Batch batch, Vector3 start, Vector3 end, float startRadius, float endRadius)
        {
            const int sides = 7;
            var axis = (end-start).normalized;
            var tangent = Vector3.Cross(axis,Vector3.forward).normalized;
            var normal = Vector3.Cross(axis,tangent);
            var first = batch.vertices.Count;
            for (var ring = 0; ring < 2; ring++)
                for (var i = 0; i < sides; i++)
                {
                    var a = i*Mathf.PI*2/sides;
                    batch.vertices.Add((ring == 0 ? start : end) + (tangent*Mathf.Cos(a)+normal*Mathf.Sin(a))*(ring == 0 ? startRadius : endRadius));
                }
            for (var i = 0; i < sides; i++)
            {
                var a = first+i; var b = first+(i+1)%sides;
                batch.indices.AddRange(new[] { a,b,b+sides,a,b+sides,a+sides });
            }
        }

        private static void Leaf(Batch batch, Vector3 origin, Vector3 direction, float length, float width)
        {
            direction.Normalize();
            var right = Vector3.Cross(Vector3.up, direction).normalized * width;
            var middle = origin + direction * length * 0.48f;
            var tip = origin + direction * length;
            batch.Triangle(origin, middle - right, middle + Vector3.up * 0.035f);
            batch.Triangle(middle - right, tip, middle + Vector3.up * 0.035f);
            batch.Triangle(origin, middle + Vector3.up * 0.035f, middle + right);
            batch.Triangle(middle + Vector3.up * 0.035f, tip, middle + right);
        }

        private static void Lily(Batch batch, Vector3 center, float radius)
        {
            for (var i = 1; i < 18; i++)
            {
                var a = i * Mathf.PI * 2f / 19f;
                var b = (i + 1) * Mathf.PI * 2f / 19f;
                batch.Triangle(center, center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius,
                    center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius);
            }
        }

        private static void Rock(Batch batch, Vector3 bottom, Vector3 size, int sides, int rings)
        {
            var points = new Vector3[(rings + 1) * sides];
            var seed = Range(0, 6);
            for (var j = 0; j <= rings; j++)
            {
                var t = (float)j / rings;
                var taper = j == rings ? 0.07f : Mathf.Lerp(1f, 0.36f, t) * (0.9f + 0.14f * Mathf.Sin(t * 15f + seed));
                for (var i = 0; i < sides; i++)
                {
                    var a = i * Mathf.PI * 2f / sides;
                    var irregularity = 1f + 0.12f * Mathf.Sin(a * 3 + seed + t * 7);
                    points[j * sides + i] = bottom + new Vector3(
                        Mathf.Cos(a) * size.x * taper * irregularity + Mathf.Sin(t * 5 + seed) * size.x * 0.12f,
                        t * size.y, Mathf.Sin(a) * size.z * taper * irregularity);
                }
            }
            for (var j = 0; j < rings; j++)
                for (var i = 0; i < sides; i++)
                {
                    var a = j * sides + i; var b = j * sides + (i + 1) % sides;
                    batch.Quad(points[a], points[a + sides], points[b + sides], points[b]);
                }
        }

        private static void TorusRock(Batch batch, Vector3 center)
        {
            const int around = 16, tube = 9;
            var points = new Vector3[around * tube];
            for (var i = 0; i < around; i++)
                for (var j = 0; j < tube; j++)
                {
                    var a = i * Mathf.PI * 2f / around;
                    var b = j * Mathf.PI * 2f / tube;
                    var thickness = 0.58f + 0.2f * Mathf.Sin(a * 3 + 0.4f);
                    points[i * tube + j] = center + new Vector3(
                        (1f + thickness * Mathf.Cos(b)) * Mathf.Cos(a) + 0.25f * Mathf.Sin(a * 2),
                        (1.45f + thickness * Mathf.Cos(b)) * Mathf.Sin(a) + 0.15f * Mathf.Sin(a * 3),
                        thickness * Mathf.Sin(b) + 0.15f * Mathf.Sin(a * 2));
                }
            for (var i = 0; i < around; i++)
                for (var j = 0; j < tube; j++)
                    batch.Quad(points[i * tube + j], points[((i + 1) % around) * tube + j],
                        points[((i + 1) % around) * tube + (j + 1) % tube], points[i * tube + (j + 1) % tube]);
        }

        private static void Lantern(Batch wood, Batch paper, Vector3 bottom)
        {
            Box(wood, bottom + Vector3.up * 0.05f, new Vector3(0.64f, 0.1f, 0.64f));
            Box(paper, bottom + Vector3.up * 0.4f, new Vector3(0.4f, 0.55f, 0.4f));
            for (var x = -1; x <= 1; x += 2)
                for (var z = -1; z <= 1; z += 2)
                    Box(wood, bottom + new Vector3(x * 0.24f, 0.4f, z * 0.24f), new Vector3(0.07f, 0.66f, 0.07f));
            Box(wood, bottom + Vector3.up * 0.74f, new Vector3(0.72f, 0.1f, 0.72f));
            Rock(wood, bottom + Vector3.up * 0.79f, new Vector3(0.48f, 0.22f, 0.48f), 4, 1);
        }

        private static void Box(Batch batch, Vector3 center, Vector3 size)
        {
            var s = size * 0.5f;
            var v = new[] {new Vector3(-s.x,-s.y,-s.z),new Vector3(s.x,-s.y,-s.z),
                new Vector3(s.x,s.y,-s.z),new Vector3(-s.x,s.y,-s.z),
                new Vector3(-s.x,-s.y,s.z),new Vector3(s.x,-s.y,s.z),
                new Vector3(s.x,s.y,s.z),new Vector3(-s.x,s.y,s.z)};
            for (var i = 0; i < v.Length; i++) v[i] += center;
            batch.Quad(v[0],v[3],v[2],v[1]); batch.Quad(v[4],v[5],v[6],v[7]);
            batch.Quad(v[0],v[4],v[7],v[3]); batch.Quad(v[1],v[2],v[6],v[5]);
            batch.Quad(v[3],v[7],v[6],v[2]); batch.Quad(v[0],v[1],v[5],v[4]);
        }

        private sealed class Batch
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> indices = new List<int>();
            public readonly List<Color> colors = new List<Color>();
            public Material material;
            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                indices.Add(n); indices.Add(n + 1); indices.Add(n + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            { Triangle(a,b,c); Triangle(a,c,d); }
        }
    }
}
