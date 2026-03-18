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
    internal enum DistantTerrainLightingProfile
    {
        Far,
        UltraFar
    }

    private readonly record struct FaceDefinition(
        Vector3 Normal,
        Vector3 V0,
        Vector3 V1,
        Vector3 V2,
        Vector3 V3,
        WorldTextureAtlas.WorldAtlasTile Tile,
        byte Shade,
        byte Sun,
        byte Accent,
        byte Material);

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
            var daylight = Math.Clamp(surface.Daylight / (float)WorldMap.MaxDaylight, 0f, 1f);
            var localLight = Math.Clamp(surface.LocalLight / (float)WorldMap.MaxLocalLight, 0f, 1f);
            var sunVisibility = Math.Clamp(surface.SunVisibility / (float)WorldMap.MaxSunVisibility, 0f, 1f);
            var combinedLight = MathF.Max(daylight * 0.96f, localLight);
            var bouncedLocalLight = localLight * (surface.TopVisible ? 0.12f : 0.20f);
            var sunLight = 0.06f
                + daylight * 0.34f
                + sunVisibility * 0.20f
                + localLight * 0.28f
                + combinedLight * 0.14f
                + bouncedLocalLight * 0.08f;
            var occlusion = 1f - surface.AmbientOcclusion * 0.045f;
            var relief = 0.92f + surface.ReliefExposure * 0.02f;

            AddFaceIfVisible(new FaceDefinition(
                new Vector3(1f, 0f, 0f),
                center + new Vector3(0.5f, -0.5f, -0.5f),
                center + new Vector3(0.5f, -0.5f, 0.5f),
                center + new Vector3(0.5f, 0.5f, 0.5f),
                center + new Vector3(0.5f, 0.5f, -0.5f),
                tiles.Side,
                ApplyLight(206, sunLight, occlusion),
                EncodeSunChannel(surface, topBias: -0.02f, visibilityBias: 0.05f),
                EncodeAccentChannel(surface, ridgeBias: 0.04f, cavityBias: 0.09f),
                EncodeMaterialChannel(surface.Block)),
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
                ApplyLight(188, sunLight, occlusion),
                EncodeSunChannel(surface, topBias: -0.04f, visibilityBias: 0.02f),
                EncodeAccentChannel(surface, ridgeBias: 0.02f, cavityBias: 0.11f),
                EncodeMaterialChannel(surface.Block)),
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
                ApplyLight(255, sunLight, relief),
                EncodeSunChannel(surface, topBias: 0.18f, visibilityBias: 0.12f),
                EncodeAccentChannel(surface, ridgeBias: 0.18f, cavityBias: 0.05f),
                EncodeMaterialChannel(surface.Block)),
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
                ApplyLight(142, sunLight, occlusion),
                EncodeSunChannel(surface, topBias: -0.28f, visibilityBias: -0.08f),
                EncodeAccentChannel(surface, ridgeBias: -0.10f, cavityBias: 0.14f),
                EncodeMaterialChannel(surface.Block)),
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
                ApplyLight(222, sunLight, occlusion),
                EncodeSunChannel(surface, topBias: 0.02f, visibilityBias: 0.08f),
                EncodeAccentChannel(surface, ridgeBias: 0.05f, cavityBias: 0.08f),
                EncodeMaterialChannel(surface.Block)),
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
                ApplyLight(172, sunLight, occlusion),
                EncodeSunChannel(surface, topBias: -0.08f, visibilityBias: -0.02f),
                EncodeAccentChannel(surface, ridgeBias: 0.01f, cavityBias: 0.12f),
                EncodeMaterialChannel(surface.Block)),
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

    public static ChunkSurfaceMeshData BuildDistantTerrainProxy(
        IReadOnlyList<WorldMap.SurfaceBlock> surfaces,
        int sampleStep,
        DistantTerrainLightingProfile lightingProfile)
    {
        var step = Math.Max(1, sampleStep);
        var vertices = new List<float>(surfaces.Count * 6);
        var texCoords = new List<float>(surfaces.Count * 4);
        var normals = new List<float>(surfaces.Count * 6);
        var colors = new List<byte>(surfaces.Count * 8);
        var indices = new List<ushort>(surfaces.Count * 6);
        var halfSpan = Math.Clamp(step * 0.5f, 0.5f, 2.5f);

        for (var i = 0; i < surfaces.Count; i++)
        {
            var surface = surfaces[i];
            if (!surface.TopVisible
                || !GameApp.IsTerrainSurfaceBlock(surface.Block)
                || surface.X % step != 0
                || surface.Z % step != 0)
            {
                continue;
            }

            var center = new Vector3(surface.X + 0.5f, surface.Y + 0.5f, surface.Z + 0.5f);
            var tiles = WorldTextureAtlas.GetFaceTiles(surface.Block);
            var daylight = Math.Clamp(surface.Daylight / (float)WorldMap.MaxDaylight, 0f, 1f);
            var localLight = Math.Clamp(surface.LocalLight / (float)WorldMap.MaxLocalLight, 0f, 1f);
            var sunVisibility = Math.Clamp(surface.SunVisibility / (float)WorldMap.MaxSunVisibility, 0f, 1f);
            var relief = 0.94f + Math.Clamp(surface.ReliefExposure / 6f, 0f, 1f) * 0.10f;
            var cheapLocalLight = lightingProfile == DistantTerrainLightingProfile.Far
                ? MathF.Min(localLight, 0.40f) * 0.26f
                : 0f;
            var daylightEnvelope = MathF.Max(daylight * 0.94f, sunVisibility * 0.72f);
            var sunLight = lightingProfile == DistantTerrainLightingProfile.Far
                ? 0.22f + daylightEnvelope * 0.32f + cheapLocalLight * 0.06f + sunVisibility * 0.10f
                : 0.18f + daylightEnvelope * 0.28f + sunVisibility * 0.10f + Math.Clamp(surface.SkyExposure / 5f, 0f, 1f) * 0.02f;

            AddFaceIfVisible(new FaceDefinition(
                new Vector3(0f, 1f, 0f),
                center + new Vector3(-halfSpan, 0.5f, -halfSpan),
                center + new Vector3(halfSpan, 0.5f, -halfSpan),
                center + new Vector3(halfSpan, 0.5f, halfSpan),
                center + new Vector3(-halfSpan, 0.5f, halfSpan),
                tiles.Top,
                ApplyLight(212, sunLight, relief),
                EncodeDistantTerrainSunChannel(surface, lightingProfile),
                EncodeDistantTerrainAccentChannel(surface, lightingProfile),
                EncodeMaterialChannel(surface.Block)),
                visible: true,
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

        AddVertex(face.V0, face.Normal, face.Shade, face.Sun, face.Accent, face.Material, uv.U0, uv.V1, vertices, texCoords, normals, colors);
        AddVertex(face.V1, face.Normal, face.Shade, face.Sun, face.Accent, face.Material, uv.U1, uv.V1, vertices, texCoords, normals, colors);
        AddVertex(face.V2, face.Normal, face.Shade, face.Sun, face.Accent, face.Material, uv.U1, uv.V0, vertices, texCoords, normals, colors);
        AddVertex(face.V3, face.Normal, face.Shade, face.Sun, face.Accent, face.Material, uv.U0, uv.V0, vertices, texCoords, normals, colors);

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
        byte sun,
        byte accent,
        byte material,
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
        colors.Add(sun);
        colors.Add(accent);
        colors.Add(material);
    }

    private static byte ApplyLight(byte baseShade, float sunLight, float accent)
    {
        var value = baseShade * Math.Clamp(sunLight * accent, 0.10f, 1.25f);
        return (byte)Math.Clamp((int)MathF.Round(value), 12, 255);
    }

    private static byte EncodeSunChannel(WorldMap.SurfaceBlock surface, float topBias, float visibilityBias)
    {
        var daylight = Math.Clamp(surface.Daylight / (float)WorldMap.MaxDaylight, 0f, 1f);
        var localLight = Math.Clamp(surface.LocalLight / (float)WorldMap.MaxLocalLight, 0f, 1f);
        var sunVisibility = Math.Clamp(surface.SunVisibility / (float)WorldMap.MaxSunVisibility, 0f, 1f);
        var cavity = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f);
        var sky = Math.Clamp(surface.SkyExposure / 5f, 0f, 1f);
        var value = 0.04f
            + daylight * 0.30f
            + localLight * 0.24f
            + sunVisibility * (0.26f + visibilityBias)
            + sky * 0.04f
            + (surface.TopVisible ? 0.04f : -0.05f)
            + topBias
            - cavity * 0.18f;
        return EncodeUnit(value);
    }

    private static byte EncodeAccentChannel(WorldMap.SurfaceBlock surface, float ridgeBias, float cavityBias)
    {
        var ridge = Math.Clamp(surface.ReliefExposure / 4f, 0f, 1f);
        var cavity = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f);
        var sunVisibility = Math.Clamp(surface.SunVisibility / (float)WorldMap.MaxSunVisibility, 0f, 1f);
        var value = 0.42f
            + ridge * (0.22f + ridgeBias)
            + sunVisibility * 0.08f
            - cavity * (0.20f + cavityBias)
            + (surface.TopVisible ? 0.06f : -0.02f);
        return EncodeUnit(value);
    }

    private static byte EncodeMaterialChannel(BlockType block)
    {
        return block switch
        {
            BlockType.Grass => 32,
            BlockType.Dirt => 72,
            BlockType.Stone => 128,
            BlockType.Wood => 184,
            BlockType.Leaves => 232,
            _ => 255
        };
    }

    private static byte EncodeUnit(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0.05f, 1f) * 255f), 18, 255);
    }

    private static byte EncodeDistantTerrainSunChannel(WorldMap.SurfaceBlock surface, DistantTerrainLightingProfile lightingProfile)
    {
        var daylight = Math.Clamp(surface.Daylight / (float)WorldMap.MaxDaylight, 0f, 1f);
        var localLight = Math.Clamp(surface.LocalLight / (float)WorldMap.MaxLocalLight, 0f, 1f);
        var sunVisibility = Math.Clamp(surface.SunVisibility / (float)WorldMap.MaxSunVisibility, 0f, 1f);
        var sky = Math.Clamp(surface.SkyExposure / 5f, 0f, 1f);
        var cavity = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f);
        var localContribution = lightingProfile == DistantTerrainLightingProfile.Far
            ? MathF.Min(localLight, 0.40f) * 0.05f
            : 0f;
        var value = 0.07f
            + daylight * 0.26f
            + sunVisibility * (lightingProfile == DistantTerrainLightingProfile.Far ? 0.20f : 0.22f)
            + sky * 0.02f
            + localContribution
            + (surface.TopVisible ? 0.03f : -0.03f)
            - cavity * (lightingProfile == DistantTerrainLightingProfile.Far ? 0.14f : 0.12f);
        return EncodeUnit(value);
    }

    private static byte EncodeDistantTerrainAccentChannel(WorldMap.SurfaceBlock surface, DistantTerrainLightingProfile lightingProfile)
    {
        var ridge = Math.Clamp(surface.ReliefExposure / 4f, 0f, 1f);
        var cavity = Math.Clamp(surface.AmbientOcclusion / 8f, 0f, 1f);
        var sunVisibility = Math.Clamp(surface.SunVisibility / (float)WorldMap.MaxSunVisibility, 0f, 1f);
        var value = lightingProfile == DistantTerrainLightingProfile.Far
            ? 0.42f + ridge * 0.34f + sunVisibility * 0.05f - cavity * 0.16f
            : 0.40f + ridge * 0.30f + sunVisibility * 0.03f - cavity * 0.14f;
        return EncodeUnit(value);
    }
}
