using System.Numerics;
using AIG.Game.World;

namespace AIG.Game.Core;

internal sealed class TexturedBlockMeshData
{
    public TexturedBlockMeshData(float[] vertices, float[] texCoords, float[] normals, byte[] colors, ushort[] indices)
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
}

internal static class TexturedBlockMeshFactory
{
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

    public static TexturedBlockMeshData Build(BlockType block)
    {
        var tiles = WorldTextureAtlas.GetFaceTiles(block);
        var faces = new[]
        {
            new FaceDefinition(new Vector3(1f, 0f, 0f),  new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f),  new Vector3(0.5f, 0.5f, 0.5f),  new Vector3(0.5f, 0.5f, -0.5f), tiles.Side, 206, 204, 156, EncodeMaterialChannel(block)),
            new FaceDefinition(new Vector3(-1f, 0f, 0f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, 0.5f), tiles.Side, 188, 186, 148, EncodeMaterialChannel(block)),
            new FaceDefinition(new Vector3(0f, 1f, 0f),  new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),  new Vector3(0.5f, 0.5f, 0.5f),  new Vector3(-0.5f, 0.5f, 0.5f), tiles.Top, 255, 255, 228, EncodeMaterialChannel(block)),
            new FaceDefinition(new Vector3(0f, -1f, 0f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),  new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), tiles.Bottom, 142, 82, 122, EncodeMaterialChannel(block)),
            new FaceDefinition(new Vector3(0f, 0f, 1f),  new Vector3(0.5f, -0.5f, 0.5f),  new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),  new Vector3(0.5f, 0.5f, 0.5f), tiles.Side, 222, 214, 166, EncodeMaterialChannel(block)),
            new FaceDefinition(new Vector3(0f, 0f, -1f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),  new Vector3(0.5f, 0.5f, -0.5f),  new Vector3(-0.5f, 0.5f, -0.5f), tiles.Side, 172, 172, 136, EncodeMaterialChannel(block))
        };

        var vertices = new float[faces.Length * 4 * 3];
        var texCoords = new float[faces.Length * 4 * 2];
        var normals = new float[faces.Length * 4 * 3];
        var colors = new byte[faces.Length * 4 * 4];
        var indices = new ushort[faces.Length * 6];

        for (var faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            var face = faces[faceIndex];
            var uv = WorldTextureAtlas.GetTileUv(face.Tile);
            var vertexBase = faceIndex * 4;
            var vertexOffset = vertexBase * 3;
            var uvOffset = vertexBase * 2;
            var colorOffset = vertexBase * 4;
            var indexOffset = faceIndex * 6;

            WriteVertex(vertices, vertexOffset + 0, face.V0);
            WriteVertex(vertices, vertexOffset + 3, face.V1);
            WriteVertex(vertices, vertexOffset + 6, face.V2);
            WriteVertex(vertices, vertexOffset + 9, face.V3);

            WriteNormal(normals, vertexOffset + 0, face.Normal);
            WriteNormal(normals, vertexOffset + 3, face.Normal);
            WriteNormal(normals, vertexOffset + 6, face.Normal);
            WriteNormal(normals, vertexOffset + 9, face.Normal);

            texCoords[uvOffset + 0] = uv.U0;
            texCoords[uvOffset + 1] = uv.V1;
            texCoords[uvOffset + 2] = uv.U1;
            texCoords[uvOffset + 3] = uv.V1;
            texCoords[uvOffset + 4] = uv.U1;
            texCoords[uvOffset + 5] = uv.V0;
            texCoords[uvOffset + 6] = uv.U0;
            texCoords[uvOffset + 7] = uv.V0;

            for (var i = 0; i < 4; i++)
            {
                colors[colorOffset + i * 4 + 0] = face.Shade;
                colors[colorOffset + i * 4 + 1] = face.Sun;
                colors[colorOffset + i * 4 + 2] = face.Accent;
                colors[colorOffset + i * 4 + 3] = face.Material;
            }

            indices[indexOffset + 0] = (ushort)(vertexBase + 0);
            indices[indexOffset + 1] = (ushort)(vertexBase + 2);
            indices[indexOffset + 2] = (ushort)(vertexBase + 1);
            indices[indexOffset + 3] = (ushort)(vertexBase + 0);
            indices[indexOffset + 4] = (ushort)(vertexBase + 3);
            indices[indexOffset + 5] = (ushort)(vertexBase + 2);
        }

        return new TexturedBlockMeshData(vertices, texCoords, normals, colors, indices);
    }

    private static void WriteVertex(float[] target, int offset, Vector3 value)
    {
        target[offset + 0] = value.X;
        target[offset + 1] = value.Y;
        target[offset + 2] = value.Z;
    }

    private static void WriteNormal(float[] target, int offset, Vector3 value)
    {
        target[offset + 0] = value.X;
        target[offset + 1] = value.Y;
        target[offset + 2] = value.Z;
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
}
