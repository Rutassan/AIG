using System.Numerics;
using AIG.Game.World;

namespace AIG.Game.Core;

public sealed class ChunkSurfaceMeshData
{
    public ChunkSurfaceMeshData(float[] vertices, float[] texCoords, float[] normals, byte[] colors, ushort[] indices)
    {
        Vertices = vertices;
        TexCoords = texCoords;
        Normals = normals;
        Colors = colors;
        Indices = indices;
    }

    public float[] Vertices { get; }
    public float[] TexCoords { get; }
    public float[] Normals { get; }
    public byte[] Colors { get; }
    public ushort[] Indices { get; }
    public int VertexCount => Vertices.Length / 3;
    public int TriangleCount => Indices.Length / 3;
    public bool IsEmpty => Indices.Length == 0;
}

internal static class ChunkSurfaceMeshFactory
{
    private readonly record struct FaceDefinition(
        Vector3 Normal,
        Vector3 V0,
        Vector3 V1,
        Vector3 V2,
        Vector3 V3,
        WorldTextureAtlas.WorldAtlasTile Tile,
        byte Shade);

    public static ChunkSurfaceMeshData Build(WorldMap world, IReadOnlyList<WorldMap.SurfaceBlock> surfaces)
    {
        var vertices = new List<float>(surfaces.Count * 3 * 12);
        var texCoords = new List<float>(surfaces.Count * 2 * 8);
        var normals = new List<float>(surfaces.Count * 3 * 12);
        var colors = new List<byte>(surfaces.Count * 4 * 16);
        var indices = new List<ushort>(surfaces.Count * 6 * 6);

        for (var i = 0; i < surfaces.Count; i++)
        {
            var surface = surfaces[i];
            if (!GameApp.IsTextureAtlasBlock(surface.Block))
            {
                continue;
            }

            var center = new Vector3(surface.X + 0.5f, surface.Y + 0.5f, surface.Z + 0.5f);
            var tiles = WorldTextureAtlas.GetFaceTiles(surface.Block);
            var sunLight = 0.72f + (surface.SunVisibility / (float)WorldMap.MaxSunVisibility) * 0.28f;
            var occlusion = 1f - surface.AmbientOcclusion * 0.045f;
            var relief = 0.92f + surface.ReliefExposure * 0.02f;

            AddFaceIfVisible(new FaceDefinition(
                new Vector3(1f, 0f, 0f),
                center + new Vector3(0.5f, -0.5f, -0.5f),
                center + new Vector3(0.5f, -0.5f, 0.5f),
                center + new Vector3(0.5f, 0.5f, 0.5f),
                center + new Vector3(0.5f, 0.5f, -0.5f),
                tiles.Side,
                ApplyLight(206, sunLight, occlusion)),
                !world.IsSurfaceMeshingSolid(surface.X + 1, surface.Y, surface.Z),
                vertices,
                texCoords,
                normals,
                colors,
                indices);

            AddFaceIfVisible(new FaceDefinition(
                new Vector3(-1f, 0f, 0f),
                center + new Vector3(-0.5f, -0.5f, 0.5f),
                center + new Vector3(-0.5f, -0.5f, -0.5f),
                center + new Vector3(-0.5f, 0.5f, -0.5f),
                center + new Vector3(-0.5f, 0.5f, 0.5f),
                tiles.Side,
                ApplyLight(188, sunLight, occlusion)),
                !world.IsSurfaceMeshingSolid(surface.X - 1, surface.Y, surface.Z),
                vertices,
                texCoords,
                normals,
                colors,
                indices);

            AddFaceIfVisible(new FaceDefinition(
                new Vector3(0f, 1f, 0f),
                center + new Vector3(-0.5f, 0.5f, -0.5f),
                center + new Vector3(0.5f, 0.5f, -0.5f),
                center + new Vector3(0.5f, 0.5f, 0.5f),
                center + new Vector3(-0.5f, 0.5f, 0.5f),
                tiles.Top,
                ApplyLight(255, sunLight, relief)),
                !world.IsSurfaceMeshingSolid(surface.X, surface.Y + 1, surface.Z),
                vertices,
                texCoords,
                normals,
                colors,
                indices);

            AddFaceIfVisible(new FaceDefinition(
                new Vector3(0f, -1f, 0f),
                center + new Vector3(-0.5f, -0.5f, 0.5f),
                center + new Vector3(0.5f, -0.5f, 0.5f),
                center + new Vector3(0.5f, -0.5f, -0.5f),
                center + new Vector3(-0.5f, -0.5f, -0.5f),
                tiles.Bottom,
                ApplyLight(142, sunLight, occlusion)),
                !world.IsSurfaceMeshingSolid(surface.X, surface.Y - 1, surface.Z),
                vertices,
                texCoords,
                normals,
                colors,
                indices);

            AddFaceIfVisible(new FaceDefinition(
                new Vector3(0f, 0f, 1f),
                center + new Vector3(0.5f, -0.5f, 0.5f),
                center + new Vector3(-0.5f, -0.5f, 0.5f),
                center + new Vector3(-0.5f, 0.5f, 0.5f),
                center + new Vector3(0.5f, 0.5f, 0.5f),
                tiles.Side,
                ApplyLight(222, sunLight, occlusion)),
                !world.IsSurfaceMeshingSolid(surface.X, surface.Y, surface.Z + 1),
                vertices,
                texCoords,
                normals,
                colors,
                indices);

            AddFaceIfVisible(new FaceDefinition(
                new Vector3(0f, 0f, -1f),
                center + new Vector3(-0.5f, -0.5f, -0.5f),
                center + new Vector3(0.5f, -0.5f, -0.5f),
                center + new Vector3(0.5f, 0.5f, -0.5f),
                center + new Vector3(-0.5f, 0.5f, -0.5f),
                tiles.Side,
                ApplyLight(172, sunLight, occlusion)),
                !world.IsSurfaceMeshingSolid(surface.X, surface.Y, surface.Z - 1),
                vertices,
                texCoords,
                normals,
                colors,
                indices);
        }

        return new ChunkSurfaceMeshData(
            vertices.ToArray(),
            texCoords.ToArray(),
            normals.ToArray(),
            colors.ToArray(),
            indices.ToArray());
    }

    private static void AddFaceIfVisible(
        FaceDefinition face,
        bool visible,
        List<float> vertices,
        List<float> texCoords,
        List<float> normals,
        List<byte> colors,
        List<ushort> indices)
    {
        if (!visible)
        {
            return;
        }

        var uv = WorldTextureAtlas.GetTileUv(face.Tile);
        var vertexBase = vertices.Count / 3;
        if (vertexBase > ushort.MaxValue - 4)
        {
            return;
        }

        AddVertex(face.V0, face.Normal, face.Shade, uv.U0, uv.V1, vertices, texCoords, normals, colors);
        AddVertex(face.V1, face.Normal, face.Shade, uv.U1, uv.V1, vertices, texCoords, normals, colors);
        AddVertex(face.V2, face.Normal, face.Shade, uv.U1, uv.V0, vertices, texCoords, normals, colors);
        AddVertex(face.V3, face.Normal, face.Shade, uv.U0, uv.V0, vertices, texCoords, normals, colors);

        indices.Add((ushort)(vertexBase + 0));
        indices.Add((ushort)(vertexBase + 2));
        indices.Add((ushort)(vertexBase + 1));
        indices.Add((ushort)(vertexBase + 0));
        indices.Add((ushort)(vertexBase + 3));
        indices.Add((ushort)(vertexBase + 2));
    }

    private static void AddVertex(
        Vector3 vertex,
        Vector3 normal,
        byte shade,
        float u,
        float v,
        List<float> vertices,
        List<float> texCoords,
        List<float> normals,
        List<byte> colors)
    {
        vertices.Add(vertex.X);
        vertices.Add(vertex.Y);
        vertices.Add(vertex.Z);

        texCoords.Add(u);
        texCoords.Add(v);

        normals.Add(normal.X);
        normals.Add(normal.Y);
        normals.Add(normal.Z);

        colors.Add(shade);
        colors.Add(shade);
        colors.Add(shade);
        colors.Add(255);
    }

    private static byte ApplyLight(byte baseShade, float sunLight, float accent)
    {
        var value = baseShade * Math.Clamp(sunLight * accent, 0.45f, 1.25f);
        return (byte)Math.Clamp((int)MathF.Round(value), 48, 255);
    }
}
