using AForge;
using AForge.Math.Geometry;
using BruTile;
using BruTile;
using Cairo;
using Gdk;
using Gtk;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
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
using static NetVips.Enums;
using static OpenSlideGTK.Stitch;
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;
namespace OpenSlideGTK
{
    public class Stitch
    {
        private OpenGLStitcher stitcher;
        private TileTextureCache textureCache = new TileTextureCache();
        public bool initialized = false;
        public List<GpuTile> gpuTiles = new();
        public TileCopyGL tileCopy;
        public class TileCopyGL : GameWindow
        {
            private int computeShaderProgram;
            private int computeShader;

            // Uniform locations
            private int canvasWidthLocation;
            private int canvasHeightLocation;
            private int offsetXLocation;
            private int offsetYLocation;
            public int canvasTexture;
            public TileCopyGL(GameWindowSettings gws, NativeWindowSettings nws)
            : base(gws, nws)
            {
                InitializeShaders();
            }

            private void InitializeShaders()
            {
                // Create compute shader
                //if (glArea.Error != null)
                //    throw new Exception("OpenGL context creation failed");
                computeShader = GL.CreateShader(ShaderType.ComputeShader);

                // Load shader source
                string shaderSource = LoadShaderSource("tile_copy.comp");
                GL.ShaderSource(computeShader, shaderSource);
                GL.CompileShader(computeShader);

                // Check compilation errors
                GL.GetShader(computeShader, ShaderParameter.CompileStatus, out int success);
                if (success == 0)
                {
                    string infoLog = GL.GetShaderInfoLog(computeShader);
                    throw new Exception($"Compute shader compilation failed: {infoLog}");
                }

                // Create program
                computeShaderProgram = GL.CreateProgram();
                GL.AttachShader(computeShaderProgram, computeShader);
                GL.LinkProgram(computeShaderProgram);

                // Check linking errors
                GL.GetProgram(computeShaderProgram, GetProgramParameterName.LinkStatus, out success);
                if (success == 0)
                {
                    string infoLog = GL.GetProgramInfoLog(computeShaderProgram);
                    throw new Exception($"Shader program linking failed: {infoLog}");
                }

                // Get uniform locations
                canvasWidthLocation = GL.GetUniformLocation(computeShaderProgram, "canvasWidth");
                canvasHeightLocation = GL.GetUniformLocation(computeShaderProgram, "canvasHeight");
                offsetXLocation = GL.GetUniformLocation(computeShaderProgram, "offsetX");
                offsetYLocation = GL.GetUniformLocation(computeShaderProgram, "offsetY");
                canvasTexture = CreateCanvasTexture(ClientRectangle.Size.X, ClientRectangle.Size.Y);
            }

            private string LoadShaderSource(string filename)
            {
                // Load from embedded resource or file
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
                if (System.IO.File.Exists(path))
                {
                    return System.IO.File.ReadAllText(path);
                }

                // Embedded shader source as fallback
                return @"
#version 430
layout(local_size_x = 16, local_size_y = 16) in;

layout(rgba8, binding = 0) uniform readonly image2D tile;
layout(rgba8, binding = 1) uniform writeonly image2D canvas;

uniform int canvasWidth;
uniform int canvasHeight;
uniform int offsetX;
uniform int offsetY;

void main()
{
    ivec2 id = ivec2(gl_GlobalInvocationID.xy);

    ivec2 canvasPos = id + ivec2(offsetX, offsetY);

    if (canvasPos.x < 0 || canvasPos.y < 0 ||
        canvasPos.x >= canvasWidth || canvasPos.y >= canvasHeight)
        return;

    vec4 pixel = imageLoad(tile, id);
    imageStore(canvas, canvasPos, pixel);
}"
;
            }


            /// <summary>
            /// Create a canvas texture
            /// </summary>
            public int CreateCanvasTexture(int width, int height)
            {
                int texture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, texture);

                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba8,
                    width,
                    height,
                    0,
                    PixelFormat.Bgra,
                    PixelType.UnsignedByte,
                    IntPtr.Zero);

                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                return texture;
            }

            public class GlWidget : GLArea
            {
                private bool initialized;

                public GlWidget()
                {
                    HasDepthBuffer = true;
                    AutoRender = true;

                    Realized += OnRealized;
                    Render += GlWidget_Render;
                }

                private void GlWidget_Render(object o, RenderArgs args)
                {

                }

                private void OnRealized(object sender, EventArgs e)
                {
                    MakeCurrent();

                }
            }
        }

        // Public tile query methods
        public bool HasTile(TileIndex ex)
        {
            return gpuTiles.Any(item => item.Index == ex);
        }

        public bool HasTile(TileInfo t)
        {
            return gpuTiles.Any(item => item.Index == t.Index);
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
        public void AddTile(GpuTile tfi)
        {
            if (HasTile(tfi.Index))
                return;
            gpuTiles.Add(tfi);
            textureCache.UploadTexture(tfi.Index, tfi.Bytes, 256, 256);
        }

        // Initialization
        public bool Initialize(Stitch.TileCopyGL tileCopy)
        {
            try
            {
                if (initialized)
                {
                    return true;
                }
                stitcher = new OpenGLStitcher();
                this.tileCopy = tileCopy;
                initialized = true;
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
        }

        // Main stitching operation
        public byte[] StitchImages(
            List<TileInfo> tiles,
            int pxwidth,
            int pxheight,
            double viewX,
            double viewY,
            double viewResolution
            )
        {
            try
            {
                Initialize(tileCopy);
                return stitcher.Render(
                    tiles,
                    gpuTiles,
                    textureCache,
                    pxwidth,
                    pxheight,
                    viewX,
                    viewY,
                    viewResolution
                    );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Stitching error: {ex.Message}");
                return null;
            }
        }
        // GPU tile data structure
        public class GpuTile
        {
            public TileIndex Index;
            public const int Width = 256;
            public const int Height = 256;
            public byte[] Bytes;
            public Extent Extent;

            /// <summary>
            /// Create GpuTile with explicit dimensions (preferred for edge tiles)
            /// </summary>
            public GpuTile(TileInfo tf, byte[] bts)
            {
                Index = tf.Index;
                Extent = tf.Extent;
                Bytes = bts;
            }

            public void Dispose()
            {
                Bytes = null;
            }
        }
    }
    internal class OpenGLStitcher
    {
        private int fbo;
        private int colorTex;
        private int vao;
        private int vbo;
        private ShaderProgram shader;
        private int width = -1;
        private int height = -1;

        public OpenGLStitcher()
        {
            InitGL();
        }

        private string VertexShader = @"
#version 330 core

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;

uniform vec2 pos;   // pixel-space top-left of tile
uniform vec2 size;  // pixel-space size of tile
uniform vec2 viewportSize; // (pxwidth, pxheight)

out vec2 uv;

void main()
{
    // aPos is in [0,1]x[0,1]
    vec2 pixelPos = aPos * size + pos;

    // Convert from pixel (0..viewportSize) to NDC (-1..1), Y down to Y up
    vec2 ndc;
    ndc.x = (pixelPos.x / viewportSize.x) * 2.0 - 1.0;
    ndc.y = 1.0 - (pixelPos.y / viewportSize.y) * 2.0;

    gl_Position = vec4(ndc, 0.0, 1.0);
    uv = aUV;
}
";


        private string FragmentShader = @"
#version 330 core
in vec2 uv;
out vec4 FragColor;
uniform sampler2D tex;

void main()
{
    FragColor = texture(tex, vec2(uv.x, 1.0 - uv.y));
}
";
        private void InitGL()
        {
            shader = new ShaderProgram(VertexShader, FragmentShader);

            float[] quad =
            {
            // pos      uv
            0, 0,       0, 0,
            1, 0,       1, 0,
            1, 1,       1, 1,

            0, 0,       0, 0,
            1, 1,       1, 1,
            0, 1,       0, 1
        };

            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quad.Length * sizeof(float), quad, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.BindVertexArray(0);
        }

        private void EnsureFBO(int w, int h)
        {
            if (w == width && h == height)
                return;

            if (fbo != 0)
            {
                GL.DeleteFramebuffer(fbo);
                GL.DeleteTexture(colorTex);
            }

            width = w;
            height = h;

            colorTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, colorTex);
            GL.TexStorage2D(TextureTarget2d.Texture2D, 1, SizedInternalFormat.Rgba8, w, h);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            fbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                                    TextureTarget.Texture2D, colorTex, 0);

            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                throw new Exception($"FBO incomplete: {status}");

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
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
            EnsureFBO(pxwidth, pxheight);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            GL.Viewport(0, 0, pxwidth, pxheight);
            GL.ClearColor(0, 0, 0, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();
            GL.BindVertexArray(vao);

            // New: tell shader the viewport size in pixels
            shader.SetUniform("viewportSize", (float)pxwidth, (float)pxheight);

            foreach (var tile in tiles)
            {
                RenderTile(tile, gpuTiles, textureCache, pxwidth, pxheight, viewX, viewY, viewResolution);
            }

            byte[] output = new byte[pxwidth * pxheight * 4];
            GL.ReadPixels(0, 0, pxwidth, pxheight, PixelFormat.Bgra, PixelType.UnsignedByte, output);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Disable(EnableCap.Blend);
            return output;
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
            // Convert world extent to pixel extent with inverted Y once
            var pixelExtent = tile.Extent.WorldToPixelInvertedY(viewResolution);

            var gpuTile = gpuTiles.FirstOrDefault(t => t.Index == tile.Index);
            if (gpuTile == null)
                return;

            if (!textureCache.HasTexture(tile.Index))
                textureCache.UploadTexture(tile.Index, gpuTile.Bytes, Stitch.GpuTile.Width, Stitch.GpuTile.Height);

            int tex = textureCache.GetTexture(tile.Index);
            if (tex == 0)
                return;

            // pixel extent top-left relative to viewport
            double tileLeftPx = pixelExtent.MinX;
            double tileTopPx = -pixelExtent.MaxY; // because WorldToPixelInvertedY flipped Y

            double viewXPx = viewX / viewResolution;
            double viewYPx = viewY / viewResolution;

            float x = (float)(tileLeftPx - viewXPx);
            float y = (float)(tileTopPx - viewYPx);

            float w = (float)pixelExtent.Width;
            float h = (float)pixelExtent.Height;

            // Pass pure pixel-space position & size
            shader.SetUniform("pos", x, y);
            shader.SetUniform("size", w, h);
            shader.SetUniform("tex", 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }


    }
    // Texture cache management
    internal class TileTextureCache
    {
        private Dictionary<TileIndex, int> textureCache = new();
        public void UploadTexture(TileIndex index, byte[] pixelData, int width, int height)
        {
            if (textureCache.ContainsKey(index))
                return;

            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixelData);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            textureCache[index] = tex;
        }

        public int GetTexture(TileIndex index)
        {
            if (textureCache.TryGetValue(index, out int tex))
                return tex;
            return 0;  // Texture not found - caller should handle this
        }

        public bool HasTexture(TileIndex index)
        {
            return textureCache.ContainsKey(index);
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
            try
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
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

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