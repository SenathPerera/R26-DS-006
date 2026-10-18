using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class TempleReferenceGardenAssetTests
    {
        private const string PrefabPath =
            "Assets/Laminar VR/Art/TemplePond/ReferenceGarden/TempleReferenceGarden.prefab";

        [Test]
        public void DecorativeGarden_HasBoundedGeometryAndValidOpaqueMaterials()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var filters = prefab.GetComponentsInChildren<MeshFilter>();
            Assert.That(filters.Length, Is.InRange(1, 24));
            long triangles = 0;
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                Assert.That(mesh, Is.Not.Null, filter.name);
                Assert.That(mesh.vertexCount, Is.InRange(1, 65535), filter.name);
                Assert.That(mesh.subMeshCount, Is.EqualTo(1), filter.name);
                triangles += mesh.GetIndexCount(0) / 3;
                foreach (var vertex in mesh.vertices)
                {
                    Assert.That(float.IsNaN(vertex.sqrMagnitude)
                        || float.IsInfinity(vertex.sqrMagnitude), Is.False, filter.name);
                }

                var renderer = filter.GetComponent<MeshRenderer>();
                Assert.That(renderer.sharedMaterial, Is.Not.Null, filter.name);
                Assert.That(renderer.sharedMaterial.shader, Is.Not.Null, filter.name);
                Assert.That(renderer.sharedMaterial.GetFloat("_Surface"), Is.Zero, filter.name);
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off), filter.name);
            }
            // Includes seven denser trees, a cabin and doorway. This content regression
            // guard is not a measured Quest FPS budget or a research safety parameter.
            Assert.That(triangles, Is.LessThan(45000));
        }

        [Test]
        public void DecorativeGarden_AddsNoRuntimeScriptsLightsOrPhysics()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        }

        [Test]
        public void Mountain_IsOneConnectedSurfaceInsteadOfDetachedPeaks()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab.transform.Find("Distant Mist Peaks"), Is.Null);
            Assert.That(prefab.transform.Find("Jade Mountain Silhouettes"), Is.Null);
            var mountain = prefab.transform.Find("Continuous Mountain");
            Assert.That(mountain, Is.Not.Null);
            var mesh = mountain.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.colors.Length, Is.EqualTo(mesh.vertexCount), "Mountain rock shading needs vertex colors.");
            Assert.That(ShaderUtil.ShaderHasError(mountain.GetComponent<MeshRenderer>().sharedMaterial.shader), Is.False);
            Assert.That(mesh.bounds.size.x, Is.GreaterThan(100));
            Assert.That(mesh.bounds.min.z, Is.GreaterThan(50), "Keep the mountain outside the courtyard.");
            var parents = new int[mesh.vertexCount];
            for (var i = 0; i < parents.Length; i++) parents[i] = i;
            var indices = mesh.triangles;
            for (var i = 0; i < indices.Length; i += 3)
            {
                parents[Root(parents, indices[i + 1])] = Root(parents, indices[i]);
                parents[Root(parents, indices[i + 2])] = Root(parents, indices[i]);
            }
            for (var i = 1; i < parents.Length; i++)
                Assert.That(Root(parents, i), Is.EqualTo(Root(parents, 0)), "Detached mountain geometry.");
        }

        [Test]
        public void TreeCanopies_LeaveTheAuthoredSeatViewClear()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var seatedCamera = new Vector3(15.13f, 3.6486f, 2.16f);
            var count = 0;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.name.StartsWith("Tree Canopy")) continue;
                count++;
                foreach (var vertex in filter.sharedMesh.vertices)
                    Assert.That(Vector3.Distance(vertex, seatedCamera), Is.GreaterThan(2f),
                        "Static foliage overlaps the authored seating clearance.");
            }
            Assert.That(count, Is.EqualTo(3));
        }

        [Test]
        public void Cabin_RoofCoversExistingSeatWithHeadroom_AndDoorwayIsAtRearWall()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var roof = prefab.transform.Find("Meditation Cabin Roof");
            Assert.That(roof, Is.Not.Null);
            var roofBounds = roof.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(roofBounds.min.x, Is.LessThan(15.13f));
            Assert.That(roofBounds.max.x, Is.GreaterThan(15.13f));
            Assert.That(roofBounds.min.z, Is.LessThan(2.16f));
            Assert.That(roofBounds.max.z, Is.GreaterThan(2.16f));
            Assert.That(roofBounds.min.y, Is.GreaterThan(5.4f));
            var door = prefab.transform.Find("Temple Doorway");
            Assert.That(door, Is.Not.Null);
            var doorBounds = door.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(doorBounds.center.x, Is.EqualTo(15.1f).Within(.1f));
            Assert.That(doorBounds.min.z, Is.GreaterThan(35));
            Assert.That(doorBounds.max.z, Is.LessThan(35.82f), "Door must sit in front of the blank wall.");
        }

        [Test]
        public void TreeTrunks_IncludeThreeBeyondWalls_AndNoneAtSeat()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var mesh = prefab.transform.Find("Sculpted Tree Branches").GetComponent<MeshFilter>().sharedMesh;
            var left = false; var right = false; var rear = false;
            foreach (var vertex in mesh.vertices)
            {
                if (vertex.y > 1) continue;
                left |= vertex.x < 0;
                right |= vertex.x > 30;
                rear |= vertex.z > 45 && vertex.z < 55;
                Assert.That(vertex.x > 12 && vertex.x < 18 && vertex.z < 4, Is.False,
                    "The tree behind the seat must be removed.");
            }
            Assert.That(left && right && rear, Is.True, "Missing one of the three backdrop trees.");
        }

        [Test]
        public void WeatheredWalls_UseBoundedCompressedTextureAndOpaqueShader()
        {
            const string texturePath = "Assets/Laminar VR/Art/TemplePond/Textures/Wall/T_TempleWeatheredPlaster.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.LessThanOrEqualTo(1024));
            Assert.That(texture.height, Is.LessThanOrEqualTo(1024));
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp));
            var android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.maxTextureSize, Is.EqualTo(1024));
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Laminar VR/Art/TemplePond/Materials/Wall/M_Wall.mat");
            Assert.That(material.GetTexture("_BaseMap"), Is.EqualTo(texture));
            Assert.That(material.shader.name, Is.EqualTo("LaminarVR/Temple Weathered Wall"));
            Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
            Assert.That(material.GetFloat("_Surface"), Is.Zero);
            Assert.That(material.FindPass("ShadowCaster"), Is.GreaterThanOrEqualTo(0));
            Assert.That(material.FindPass("DepthOnly"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void WallFoliage_StaysAgainstWallsAndAboveGround()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab.transform.Find("Weathered Wall Footings"), Is.Null);
            foreach(var name in new[] {"Trailing Wall Foliage","Wall Foliage Tips"})
            {
                var child = prefab.transform.Find(name);
                Assert.That(child, Is.Not.Null);
                foreach(var vertex in child.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    Assert.That(vertex.y, Is.InRange(2.3f,6.2f));
                    Assert.That(vertex.x < 1.5f || vertex.x > 28.4f || vertex.z > 30.3f, Is.True,
                        "Trailing foliage must remain beside a wall, away from the bridge and seat.");
                }
            }
        }

        private static int Root(int[] parents, int vertex)
        {
            while (parents[vertex] != vertex)
            {
                parents[vertex] = parents[parents[vertex]];
                vertex = parents[vertex];
            }
            return vertex;
        }
    }
}
