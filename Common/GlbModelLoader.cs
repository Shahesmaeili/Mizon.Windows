using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Common;

/// <summary>Loads local glTF 2 binary meshes, including the initial pose of a skinned model.</summary>
internal static class GlbModelLoader
{
    private const int MaximumFileBytes = 32 * 1024 * 1024;
    private static readonly Dictionary<string, Task<GlbModelData>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static async Task<GlbModelData> LoadAsync(string modelPath, CancellationToken cancellationToken)
    {
        var path = ResolvePath(modelPath);
        Task<GlbModelData> task;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(path, out task!))
            {
                // Keep parsed assets, never compositor resources, in a small shared cache.
                if (Cache.Count >= 4) Cache.Clear();
                task = Task.Run(async () =>
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaximumFileBytes) throw new InvalidDataException("The model exceeds the 32 MB preview limit.");
                    var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    return Parse(bytes);
                });
                Cache[path] = task;
            }
        }

        try { return await task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch when (task.IsFaulted)
        {
            lock (CacheLock)
                if (Cache.TryGetValue(path, out var cached) && ReferenceEquals(cached, task)) Cache.Remove(path);
            throw;
        }
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A model path is required.", nameof(path));
        if (path.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Uri.UnescapeDataString(path[11..]).Replace('/', Path.DirectorySeparatorChar)));
        if (path.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile) throw new NotSupportedException("Artist models must be local GLB assets.");
            return Path.GetFullPath(uri.LocalPath);
        }
        return Path.GetFullPath(path);
    }

    // JsonDocument and explicit binary reads work under NativeAOT without reflected model types.
    internal static GlbModelData Parse(byte[] file)
    {
        if (file.Length < 20 || file.Length > MaximumFileBytes || ReadUInt(file, 0) != 0x46546C67 || ReadUInt(file, 4) != 2 || ReadUInt(file, 8) != file.Length)
            throw new InvalidDataException("Invalid glTF 2 binary header.");
        ReadOnlyMemory<byte> json = default, binary = default;
        var offset = 12;
        while (offset < file.Length)
        {
            if (file.Length - offset < 8) throw new InvalidDataException("Incomplete GLB chunk.");
            var length = checked((int)ReadUInt(file, offset));
            var type = ReadUInt(file, offset + 4);
            offset += 8;
            if (length < 0 || length > file.Length - offset) throw new InvalidDataException("Invalid GLB chunk length.");
            if (type == 0x4E4F534A) json = file.AsMemory(offset, length);
            if (type == 0x004E4942) binary = file.AsMemory(offset, length);
            offset += length;
        }
        if (json.IsEmpty || binary.IsEmpty) throw new InvalidDataException("The GLB needs JSON and binary chunks.");
        using var document = JsonDocument.Parse(json);
        return new Reader(document.RootElement, binary).Read();
    }

    private static uint ReadUInt(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private sealed class Reader(JsonElement root, ReadOnlyMemory<byte> binary)
    {
        private readonly List<GlbMeshData> _meshes = [];
        private readonly Dictionary<int, byte[]> _textures = [];
        private Matrix4x4[] _world = [];
        private JsonElement[] _nodes = [];
        private bool[] _visited = [];
        private bool[] _active = [];
        private int _totalVertices;

        public GlbModelData Read()
        {
            if (root.TryGetProperty("extensionsRequired", out var extensions) && extensions.GetArrayLength() > 0)
                throw new NotSupportedException("This preview supports uncompressed core glTF 2 models.");
            var nodes = root.GetProperty("nodes");
            if (nodes.GetArrayLength() > 4096) throw new InvalidDataException("Too many model nodes.");
            _nodes = new JsonElement[nodes.GetArrayLength()];
            _world = new Matrix4x4[_nodes.Length];
            _visited = new bool[_nodes.Length];
            _active = new bool[_nodes.Length];
            for (var i = 0; i < _nodes.Length; i++) _nodes[i] = nodes[i];
            var scene = root.GetProperty("scenes")[GetInt(root, "scene", 0)];
            foreach (var node in scene.GetProperty("nodes").EnumerateArray()) Visit(node.GetInt32(), Matrix4x4.Identity, 0);
            for (var i = 0; i < _nodes.Length; i++)
                if (_visited[i] && _nodes[i].TryGetProperty("mesh", out var mesh)) ReadMesh(i, mesh.GetInt32());
            if (_meshes.Count == 0) throw new InvalidDataException("The model has no triangle meshes.");
            var minimum = new Vector3(float.MaxValue);
            var maximum = new Vector3(float.MinValue);
            foreach (var mesh in _meshes)
                foreach (var point in mesh.Positions)
                {
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }
            var size = maximum - minimum;
            var extent = Math.Max(size.Y, Math.Max(size.X, size.Z));
            if (!float.IsFinite(extent) || extent < 0.00001f) throw new InvalidDataException("The model has invalid bounds.");
            var center = (minimum + maximum) * .5f;
            foreach (var mesh in _meshes)
                for (var i = 0; i < mesh.Positions.Length; i++) mesh.Positions[i] = (mesh.Positions[i] - center) * (2 / extent);
            return new GlbModelData(_meshes.ToArray(), minimum, maximum);
        }

        private void Visit(int index, Matrix4x4 parent, int depth)
        {
            if ((uint)index >= (uint)_nodes.Length || depth > 128 || _active[index]) throw new InvalidDataException("Invalid model hierarchy.");
            if (_visited[index]) throw new InvalidDataException("A model node has multiple parents.");
            _active[index] = true;
            _visited[index] = true;
            var node = _nodes[index];
            Matrix4x4 local;
            if (node.TryGetProperty("matrix", out var matrix)) local = Matrix(ReadValues(matrix, 16));
            else
            {
                var scale = Vector(node, "scale", Vector3.One);
                var translation = Vector(node, "translation", Vector3.Zero);
                var rotation = node.TryGetProperty("rotation", out var quaternion) ? ReadValues(quaternion, 4) : [0f, 0f, 0f, 1f];
                local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]))) * Matrix4x4.CreateTranslation(translation);
            }
            // glTF stores column-major matrices. The same ordered values represent their
            // transpose for System.Numerics' row-vector convention: local * parent.
            _world[index] = local * parent;
            if (node.TryGetProperty("children", out var children))
                foreach (var child in children.EnumerateArray()) Visit(child.GetInt32(), _world[index], depth + 1);
            _active[index] = false;
        }

        private void ReadMesh(int nodeIndex, int meshIndex)
        {
            foreach (var primitive in root.GetProperty("meshes")[meshIndex].GetProperty("primitives").EnumerateArray())
            {
                if (GetInt(primitive, "mode", 4) != 4) throw new NotSupportedException("Only triangle meshes are supported.");
                var attributes = primitive.GetProperty("attributes");
                var positionValues = ReadAccessor(attributes.GetProperty("POSITION").GetInt32(), 3);
                var count = positionValues.Length / 3;
                _totalVertices = checked(_totalVertices + count);
                if (_totalVertices > 500_000) throw new InvalidDataException("The model exceeds the preview vertex limit.");
                var positions = new Vector3[count];
                var normals = new Vector3[count];
                var normalValues = attributes.TryGetProperty("NORMAL", out var normal) ? ReadAccessor(normal.GetInt32(), 3) : null;
                if (normalValues is not null && normalValues.Length != positionValues.Length) throw new InvalidDataException("Normal count mismatch.");
                for (var i = 0; i < count; i++)
                {
                    positions[i] = new Vector3(positionValues[i * 3], positionValues[i * 3 + 1], positionValues[i * 3 + 2]);
                    if (normalValues is not null) normals[i] = new Vector3(normalValues[i * 3], normalValues[i * 3 + 1], normalValues[i * 3 + 2]);
                }
                uint[] indices;
                if (primitive.TryGetProperty("indices", out var indexAccessor)) indices = ReadIndices(indexAccessor.GetInt32());
                else
                {
                    indices = new uint[count];
                    for (var i = 0; i < count; i++) indices[i] = (uint)i;
                }
                if (indices.Length % 3 != 0) throw new InvalidDataException("Invalid triangle index count.");
                foreach (var index in indices)
                    if (index >= count) throw new InvalidDataException("A triangle index is outside its vertex buffer.");
                if (normalValues is null) GenerateNormals(positions, normals, indices);
                if (_nodes[nodeIndex].TryGetProperty("skin", out var skin)) ApplySkin(skin.GetInt32(), attributes, positions, normals);
                else Transform(positions, normals, _world[nodeIndex]);

                var textureCoordinates = new Vector2[count];
                if (attributes.TryGetProperty("TEXCOORD_0", out var uv))
                {
                    var values = ReadAccessor(uv.GetInt32(), 2);
                    if (values.Length != count * 2) throw new InvalidDataException("Texture coordinate count mismatch.");
                    for (var i = 0; i < count; i++) textureCoordinates[i] = new Vector2(values[i * 2], values[i * 2 + 1]);
                }
                var factor = Vector4.One;
                var metallic = 0f;
                var roughness = .85f;
                var alphaMode = "OPAQUE";
                var alphaCutoff = .5f;
                byte[]? texture = null;
                if (primitive.TryGetProperty("material", out var materialIndex))
                {
                    var material = root.GetProperty("materials")[materialIndex.GetInt32()];
                    if (material.TryGetProperty("alphaMode", out var alpha)) alphaMode = alpha.GetString() ?? "OPAQUE";
                    if (alphaMode is not ("OPAQUE" or "MASK" or "BLEND")) throw new InvalidDataException("Invalid material alpha mode.");
                    alphaCutoff = GetFloat(material, "alphaCutoff", .5f);
                    if (!float.IsFinite(alphaCutoff) || alphaCutoff < 0 || alphaCutoff > 1) throw new InvalidDataException("Invalid material alpha cutoff.");
                    if (material.TryGetProperty("pbrMetallicRoughness", out var pbr))
                    {
                        if (pbr.TryGetProperty("baseColorFactor", out var color))
                        {
                            var values = ReadValues(color, 4);
                            factor = new Vector4(values[0], values[1], values[2], values[3]);
                        }
                        metallic = GetFloat(pbr, "metallicFactor", 1);
                        roughness = GetFloat(pbr, "roughnessFactor", 1);
                        if (pbr.TryGetProperty("baseColorTexture", out var colorTexture))
                        {
                            if (GetInt(colorTexture, "texCoord", 0) != 0) throw new NotSupportedException("Only the first texture coordinate set is supported.");
                            var textureInfo = root.GetProperty("textures")[colorTexture.GetProperty("index").GetInt32()];
                            var imageIndex = textureInfo.GetProperty("source").GetInt32();
                            if (!_textures.TryGetValue(imageIndex, out texture))
                            {
                                var image = root.GetProperty("images")[imageIndex];
                                if (!image.TryGetProperty("bufferView", out var view)) throw new NotSupportedException("Model textures must be embedded in the GLB.");
                                texture = ReadBufferView(view.GetInt32()).ToArray();
                                _textures.Add(imageIndex, texture);
                            }
                        }
                    }
                }
                _meshes.Add(new GlbMeshData(positions, normals, textureCoordinates, indices, texture, factor, metallic, roughness, alphaMode, alphaCutoff));
            }
        }

        private void ApplySkin(int skinIndex, JsonElement attributes, Vector3[] positions, Vector3[] normals)
        {
            var skin = root.GetProperty("skins")[skinIndex];
            var joints = skin.GetProperty("joints");
            var matrices = new Matrix4x4[joints.GetArrayLength()];
            var normalMatrices = new Matrix4x4[matrices.Length];
            var inverseBinds = skin.TryGetProperty("inverseBindMatrices", out var accessor) ? ReadAccessor(accessor.GetInt32(), 16) : null;
            if (inverseBinds is not null && inverseBinds.Length != matrices.Length * 16) throw new InvalidDataException("Invalid inverse bind matrix count.");
            for (var i = 0; i < matrices.Length; i++)
            {
                var joint = joints[i].GetInt32();
                if ((uint)joint >= _world.Length || !_visited[joint]) throw new InvalidDataException("A skin joint is outside the scene.");
                // Bake the initial skeletal pose once; dragging only rotates the GPU scene.
                matrices[i] = (inverseBinds is null ? Matrix4x4.Identity : Matrix(inverseBinds.AsSpan(i * 16, 16))) * _world[joint];
                if (!Matrix4x4.Invert(matrices[i], out var inverse)) throw new InvalidDataException("Singular skin transform.");
                normalMatrices[i] = Matrix4x4.Transpose(inverse);
            }
            var jointValues = ReadAccessor(attributes.GetProperty("JOINTS_0").GetInt32(), 4);
            var weights = ReadAccessor(attributes.GetProperty("WEIGHTS_0").GetInt32(), 4);
            if (jointValues.Length != positions.Length * 4 || weights.Length != jointValues.Length) throw new InvalidDataException("Skin weight count mismatch.");
            for (var i = 0; i < positions.Length; i++)
            {
                var position = Vector3.Zero;
                var normal = Vector3.Zero;
                var total = 0f;
                for (var j = 0; j < 4; j++)
                {
                    var weight = weights[i * 4 + j];
                    if (weight <= 0) continue;
                    var joint = checked((int)jointValues[i * 4 + j]);
                    if ((uint)joint >= matrices.Length) throw new InvalidDataException("Invalid joint index.");
                    position += Vector3.Transform(positions[i], matrices[joint]) * weight;
                    normal += Vector3.TransformNormal(normals[i], normalMatrices[joint]) * weight;
                    total += weight;
                }
                if (total <= 0) throw new InvalidDataException("A skinned vertex has no joint weight.");
                positions[i] = position / total;
                normals[i] = Normalize(normal);
            }
        }

        private static void Transform(Vector3[] positions, Vector3[] normals, Matrix4x4 matrix)
        {
            if (!Matrix4x4.Invert(matrix, out var inverse)) throw new InvalidDataException("Singular mesh transform.");
            var normalMatrix = Matrix4x4.Transpose(inverse);
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = Vector3.Transform(positions[i], matrix);
                normals[i] = Normalize(Vector3.TransformNormal(normals[i], normalMatrix));
            }
        }

        private static void GenerateNormals(Vector3[] positions, Vector3[] normals, uint[] indices)
        {
            for (var i = 0; i < indices.Length; i += 3)
            {
                var a = indices[i]; var b = indices[i + 1]; var c = indices[i + 2];
                var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                normals[a] += normal; normals[b] += normal; normals[c] += normal;
            }
            for (var i = 0; i < normals.Length; i++) normals[i] = Normalize(normals[i]);
        }

        private float[] ReadAccessor(int index, int components)
        {
            var accessor = root.GetProperty("accessors")[index];
            var actualComponents = accessor.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 0 };
            if (actualComponents != components || accessor.TryGetProperty("sparse", out _)) throw new NotSupportedException("Unsupported GLB accessor layout.");
            var count = accessor.GetProperty("count").GetInt32();
            if (count < 0 || count > 2_000_000) throw new InvalidDataException("Invalid accessor count.");
            var componentType = accessor.GetProperty("componentType").GetInt32();
            var componentSize = componentType switch { 5121 => 1, 5123 => 2, 5125 or 5126 => 4, _ => throw new NotSupportedException("Unsupported GLB component type.") };
            var viewIndex = accessor.GetProperty("bufferView").GetInt32();
            var view = root.GetProperty("bufferViews")[viewIndex];
            var bytes = ReadBufferView(viewIndex).Span;
            var stride = GetInt(view, "byteStride", componentSize * components);
            var start = GetInt(accessor, "byteOffset", 0);
            if (stride < componentSize * components || start < 0 || (long)start + (long)Math.Max(0, count - 1) * stride + components * componentSize > bytes.Length)
                throw new InvalidDataException("Accessor exceeds its buffer view.");
            var normalized = accessor.TryGetProperty("normalized", out var normalize) && normalize.GetBoolean();
            var result = new float[checked(count * components)];
            for (var i = 0; i < count; i++)
                for (var j = 0; j < components; j++)
                {
                    var position = start + i * stride + j * componentSize;
                    var value = componentType switch
                    {
                        5121 => normalized ? bytes[position] / 255f : bytes[position],
                        5123 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position, 2)) / (normalized ? 65535f : 1f),
                        5125 => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4)),
                        _ => BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(position, 4))
                    };
                    if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite model attribute.");
                    result[i * components + j] = value;
                }
            return result;
        }

        private uint[] ReadIndices(int index)
        {
            var accessor = root.GetProperty("accessors")[index];
            var type = accessor.GetProperty("componentType").GetInt32();
            if (type is not (5121 or 5123 or 5125) || (accessor.TryGetProperty("normalized", out var normalized) && normalized.GetBoolean()))
                throw new InvalidDataException("Invalid mesh index format.");
            // The preview vertex limit is below float's exact integer range.
            var values = ReadAccessor(index, 1);
            var result = new uint[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = checked((uint)values[i]);
            return result;
        }

        private ReadOnlyMemory<byte> ReadBufferView(int index)
        {
            var view = root.GetProperty("bufferViews")[index];
            if (GetInt(view, "buffer", 0) != 0) throw new NotSupportedException("External GLB buffers are not supported.");
            var offset = GetInt(view, "byteOffset", 0);
            var length = view.GetProperty("byteLength").GetInt32();
            if (offset < 0 || length < 0 || (long)offset + length > binary.Length) throw new InvalidDataException("Invalid buffer view.");
            return binary.Slice(offset, length);
        }

        private static int GetInt(JsonElement value, string name, int fallback) => value.TryGetProperty(name, out var property) ? property.GetInt32() : fallback;
        private static float GetFloat(JsonElement value, string name, float fallback) => value.TryGetProperty(name, out var property) ? property.GetSingle() : fallback;
        private static Vector3 Vector(JsonElement value, string name, Vector3 fallback)
        {
            if (!value.TryGetProperty(name, out var array)) return fallback;
            var values = ReadValues(array, 3);
            return new Vector3(values[0], values[1], values[2]);
        }
        private static float[] ReadValues(JsonElement value, int count)
        {
            if (value.GetArrayLength() != count) throw new InvalidDataException("Invalid transform length.");
            var result = new float[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = value[i].GetSingle();
                if (!float.IsFinite(result[i])) throw new InvalidDataException("Non-finite transform.");
            }
            return result;
        }
        private static Matrix4x4 Matrix(ReadOnlySpan<float> v) => new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
        private static Vector3 Normalize(Vector3 value) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : Vector3.UnitY;
    }
}

internal sealed record GlbModelData(GlbMeshData[] Meshes, Vector3 OriginalMinimum, Vector3 OriginalMaximum);
internal sealed record GlbMeshData(Vector3[] Positions, Vector3[] Normals, Vector2[] TextureCoordinates, uint[] Indices, byte[]? TextureBytes, Vector4 BaseColor, float Metallic, float Roughness, string AlphaMode, float AlphaCutoff);
