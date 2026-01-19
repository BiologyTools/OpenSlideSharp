using AForge;
using BruTile;
using Gtk;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using Pango;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static OpenSlideGTK.Stitch;
using AForge.Math.Geometry;
using BruTile;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;
namespace OpenSlideGTK
{
    public class Stitch
    {
        private OpenGLStitcher stitcher;
        private TileTextureCache textureCache;
        private bool initialized = false;

        public List<GpuTile> gpuTiles = new();

        public Stitch()
        {
            Initialize(new AForge.PointD(255, 255));
        }

        // Public tile query methods
        public bool HasTile(TileIndex ex)
        {
            return gpuTiles.Any(item => item.Index == ex);
        }

        public bool HasTile(TileInfo t)
        {
            return gpuTiles.Any(item => item.Index == t.Index && item.Extent == t.Extent);
        }

        public bool HasTile(Extent t)
        {
            return gpuTiles.Any(item => item.Extent == t);
        }

        // Tile management operations
        public void DisposeLevel(int level)
        {
            var tilesToRemove = gpuTiles.Where(item => item.Index.Level == level).ToList();
            foreach (var tile in tilesToRemove)
            {
                textureCache?.ReleaseTexture(tile.Index);
                gpuTiles.Remove(tile);
            }
        }

        public void AddTile(GpuTile tileInfo, int width, int height, byte[] pixelData)
        {
            if (HasTile(tileInfo))
                return;

            var gpuTile = new GpuTile(tileInfo, pixelData, width, height);
            gpuTiles.Add(gpuTile);

            if (initialized && textureCache != null)
            {
                textureCache.UploadTexture(tileInfo.Index, pixelData, width, height);
            }
        }

        // Initialization
        public string Initialize(AForge.PointD size)
        {
            try
            {
                textureCache = new TileTextureCache();
                stitcher = new OpenGLStitcher();
                initialized = true;
                return "";
            }
            catch (Exception e)
            {
                Console.WriteLine($"OpenGL initialization error: {e.Message}");
                return e.Message;
            }
        }

        // Main stitching operation
        public byte[] StitchImages(
            List<TileInfo> tiles,
            int pxwidth,
            int pxheight,
            double viewX,
            double viewY,
            double viewResolution)
        {
            if (!initialized)
            {
                Initialize(new AForge.PointD(pxwidth, pxheight));
            }

            try
            {
                if(stitcher == null)
                {
                    stitcher = new OpenGLStitcher();
                }
                return stitcher.Render(
                    tiles,
                    gpuTiles,
                    textureCache,
                    pxwidth,
                    pxheight,
                    viewX,
                    viewY,
                    viewResolution);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Stitching error: {ex.Message}");
                throw;
            }
        }

        // GPU tile data structure
        public sealed class GpuTile : TileInfo
        {
            public TileIndex Index;
            public int Width;
            public int Height;
            public byte[] Bytes;

            public GpuTile(TileInfo tf, byte[] bts, int pxwidth, int pxheight)
            {
                Index = tf.Index;
                Width = pxwidth;
                Height = pxheight;
                Bytes = bts;
            }

            public void Dispose()
            {
                Bytes = null;
            }
        }
    }

    // OpenGL rendering implementation
    internal class OpenGLStitcher
    {
        private ShaderProgram shaderProgram;
        private int framebuffer;
        private int renderbuffer;
        private int vao;
        private int vbo;

        private int currentFboWidth = -1;
        private int currentFboHeight = -1;

        public OpenGLStitcher()
        {
            InitializeOpenGL();
        }

        private void InitializeOpenGL()
        {
            // Compile shaders
            shaderProgram = new ShaderProgram(VertexShaderSource, FragmentShaderSource);

            // Create vertex array and buffer for quad
            CreateQuadGeometry();

            // Create framebuffer (will be resized on demand)
            GL.GenFramebuffers(1, out framebuffer);
            GL.GenRenderbuffers(1, out renderbuffer);
        }

        private void CreateQuadGeometry()
        {
            // Full-screen quad with texture coordinates
            float[] quadVertices = {
            // Position (x, y)  TexCoord (u, v)
            -1.0f, -1.0f,       0.0f, 0.0f,
                1.0f, -1.0f,       1.0f, 0.0f,
                1.0f,  1.0f,       1.0f, 1.0f,

            -1.0f, -1.0f,       0.0f, 0.0f,
                1.0f,  1.0f,       1.0f, 1.0f,
            -1.0f,  1.0f,       0.0f, 1.0f
        };

            GL.GenVertexArrays(1, out vao);
            GL.GenBuffers(1, out vbo);

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float),
                quadVertices, BufferUsageHint.StaticDraw);

            // Position attribute
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // TexCoord attribute
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.BindVertexArray(0);
        }

        private void EnsureFramebufferSize(int width, int height)
        {
            if (currentFboWidth == width && currentFboHeight == height)
                return;

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);

            // Setup renderbuffer for color attachment
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                RenderbufferStorage.Rgba8, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                RenderbufferTarget.Renderbuffer, renderbuffer);

            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                throw new Exception($"Framebuffer incomplete: {status}");
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            currentFboWidth = width;
            currentFboHeight = height;
        }

        public byte[] Render(
            List<TileInfo> tiles,
            List<Stitch.GpuTile> gpuTiles,
            TileTextureCache textureCache,
            int pxwidth,
            int pxheight,
            double viewX,
            double viewY,
            double viewResolution)
        {
            // Ensure framebuffer matches viewport size
            EnsureFramebufferSize(pxwidth, pxheight);

            // Bind framebuffer and clear
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.Viewport(0, 0, pxwidth, pxheight);
            GL.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            // Enable blending for proper compositing
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Use shader program
            shaderProgram.Use();
            GL.BindVertexArray(vao);

            // Render each tile
            foreach (var tile in tiles)
            {
                RenderTile(tile, gpuTiles, textureCache, pxwidth, pxheight,
                    viewX, viewY, viewResolution);
            }

            // Read back pixels
            byte[] viewportData = new byte[pxwidth * pxheight * 4];
            GL.ReadPixels(0, 0, pxwidth, pxheight,
                PixelFormat.Rgba, PixelType.UnsignedByte, viewportData);

            // Cleanup
            GL.BindVertexArray(0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Disable(EnableCap.Blend);

            return viewportData;
        }

        private void RenderTile(
            TileInfo tile,
            List<Stitch.GpuTile> gpuTiles,
            TileTextureCache textureCache,
            int pxwidth,
            int pxheight,
            double viewX,
            double viewY,
            double viewResolution)
        {
            // Find matching GPU tile
            var gpuTile = gpuTiles.FirstOrDefault(t => t.Index == tile.Index);
            if (gpuTile == null)
                return;

            // Get texture from cache
            int textureId = textureCache.GetTexture(tile.Index);
            if (textureId == 0)
                return;

            // Calculate tile position in viewport
            int tileWidth = gpuTile.Width;
            int tileHeight = gpuTile.Height;
            int levelScale = 1 << tile.Index.Level;

            var extentPx = tile.Extent.WorldToPixelInvertedY(viewResolution);

            int offsetX = (int)Math.Floor(extentPx.MinX - viewX);
            int offsetY = pxheight - (int)Math.Floor(extentPx.MaxY - viewY) - tileHeight * levelScale;

            int scaledWidth = tileWidth * levelScale;
            int scaledHeight = tileHeight * levelScale;

            // Convert to normalized device coordinates (-1 to 1)
            float ndcX = (offsetX / (float)pxwidth) * 2.0f - 1.0f;
            float ndcY = (offsetY / (float)pxheight) * 2.0f - 1.0f;
            float ndcWidth = (scaledWidth / (float)pxwidth) * 2.0f;
            float ndcHeight = (scaledHeight / (float)pxheight) * 2.0f;

            // Set uniforms
            shaderProgram.SetUniform("tileTexture", 0);
            shaderProgram.SetUniform("position", ndcX, ndcY);
            shaderProgram.SetUniform("size", ndcWidth, ndcHeight);

            // Bind texture and draw
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        // Shader sources
        private const string VertexShaderSource = @"
#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;

uniform vec2 position;
uniform vec2 size;

out vec2 TexCoord;

void main()
{
vec2 scaledPos = aPosition * size * 0.5 + position + size * 0.5;
gl_Position = vec4(scaledPos, 0.0, 1.0);
TexCoord = aTexCoord;
}
";

        private const string FragmentShaderSource = @"
#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D tileTexture;

void main()
{
FragColor = texture(tileTexture, TexCoord);
}
";
    }

    // Texture cache management
    internal class TileTextureCache
    {
        private Dictionary<TileIndex, int> textureCache = new();

        public void UploadTexture(TileIndex index, byte[] pixelData, int width, int height)
        {
            if (textureCache.ContainsKey(index))
                return;

            int textureId;
            GL.GenTextures(1, out textureId);
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            // Upload pixel data
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixelData);

            // Set texture parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            textureCache[index] = textureId;
        }

        public int GetTexture(TileIndex index)
        {
            return textureCache.TryGetValue(index, out int textureId) ? textureId : 0;
        }

        public void ReleaseTexture(TileIndex index)
        {
            if (textureCache.TryGetValue(index, out int textureId))
            {
                GL.DeleteTexture(textureId);
                textureCache.Remove(index);
            }
        }

        public void Clear()
        {
            foreach (var textureId in textureCache.Values)
            {
                GL.DeleteTexture(textureId);
            }
            textureCache.Clear();
        }
    }

    // Shader program wrapper
    internal class ShaderProgram
    {
        private int programId;
        private Dictionary<string, int> uniformLocations = new();

        public ShaderProgram(string vertexSource, string fragmentSource)
        {
            int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
            int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

            programId = GL.CreateProgram();
            GL.AttachShader(programId, vertexShader);
            GL.AttachShader(programId, fragmentShader);
            GL.LinkProgram(programId);

            CheckProgramLinkStatus();

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        private int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetShaderInfoLog(shader);
                throw new Exception($"Shader compilation failed ({type}): {infoLog}");
            }

            return shader;
        }

        private void CheckProgramLinkStatus()
        {
            GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetProgramInfoLog(programId);
                throw new Exception($"Program linking failed: {infoLog}");
            }
        }

        public void Use()
        {
            GL.UseProgram(programId);
        }

        public void SetUniform(string name, int value)
        {
            int location = GetUniformLocation(name);
            GL.Uniform1(location, value);
        }

        public void SetUniform(string name, float x, float y)
        {
            int location = GetUniformLocation(name);
            GL.Uniform2(location, x, y);
        }

        private int GetUniformLocation(string name)
        {
            if (!uniformLocations.TryGetValue(name, out int location))
            {
                location = GL.GetUniformLocation(programId, name);
                uniformLocations[name] = location;
            }
            return location;
        }
    }
}
