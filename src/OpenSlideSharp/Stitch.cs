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
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;
namespace OpenSlideGTK
{
    public class Stitch
    {
        private OpenGLStitcher stitcher;
        private TileTextureCache textureCache;
        public bool initialized = false;
        public List<GpuTile> gpuTiles = new();
        public class TileCopyGL : IDisposable
        {
            private int computeShaderProgram;
            private int computeShader;

            // Uniform locations
            private int canvasWidthLocation;
            private int canvasHeightLocation;
            private int tileWidthLocation;
            private int tileHeightLocation;
            private int offsetXLocation;
            private int offsetYLocation;
            private int canvasTileWidthLocation;
            private int canvasTileHeightLocation;

            public TileCopyGL(GLContext gL)
            {
                InitializeShaders(gL);
            }

            private void InitializeShaders(GLContext gl)
            {
                // IMPORTANT: Load bindings here
                Native.LoadBindings(gl);
                
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
                tileWidthLocation = GL.GetUniformLocation(computeShaderProgram, "tileWidth");
                tileHeightLocation = GL.GetUniformLocation(computeShaderProgram, "tileHeight");
                offsetXLocation = GL.GetUniformLocation(computeShaderProgram, "offsetX");
                offsetYLocation = GL.GetUniformLocation(computeShaderProgram, "offsetY");
                canvasTileWidthLocation = GL.GetUniformLocation(computeShaderProgram, "canvasTileWidth");
                canvasTileHeightLocation = GL.GetUniformLocation(computeShaderProgram, "canvasTileHeight");
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
uniform int tileWidth;
uniform int tileHeight;
uniform int offsetX;
uniform int offsetY;
uniform int canvasTileWidth;
uniform int canvasTileHeight;

void main()
{
    ivec2 globalID = ivec2(gl_GlobalInvocationID.xy);
    int x = globalID.x;
    int y = globalID.y;

    if (x < canvasTileWidth && y < canvasTileHeight) {
        int canvasX = x + offsetX;
        int canvasY = y + offsetY;

        if (canvasX < canvasWidth && canvasY < canvasHeight) {
            float scaleX = float(tileWidth) / float(canvasTileWidth);
            float scaleY = float(tileHeight) / float(canvasTileHeight);

            int tileX = min(int(float(x) * scaleX), tileWidth - 1);
            int tileY = min(int(float(y) * scaleY), tileHeight - 1);

            vec4 pixel = imageLoad(tile, ivec2(tileX, tileY));
            imageStore(canvas, ivec2(canvasX, canvasY), pixel);
        }
    }
}";
            }

            /// <summary>
            /// Copy a tile to canvas using OpenGL compute shader
            /// </summary>
            public void CopyTileToCanvas(
                int canvasTexture,
                int canvasWidth,
                int canvasHeight,
                int tileTexture,
                int tileWidth,
                int tileHeight,
                int offsetX,
                int offsetY,
                int canvasTileWidth,
                int canvasTileHeight)
            {
                // Use the compute shader program
                GL.UseProgram(computeShaderProgram);

                // Set uniforms
                GL.Uniform1(canvasWidthLocation, canvasWidth);
                GL.Uniform1(canvasHeightLocation, canvasHeight);
                GL.Uniform1(tileWidthLocation, tileWidth);
                GL.Uniform1(tileHeightLocation, tileHeight);
                GL.Uniform1(offsetXLocation, offsetX);
                GL.Uniform1(offsetYLocation, offsetY);
                GL.Uniform1(canvasTileWidthLocation, canvasTileWidth);
                GL.Uniform1(canvasTileHeightLocation, canvasTileHeight);

                // Bind textures as images
                GL.BindImageTexture(0, tileTexture, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba8);
                GL.BindImageTexture(1, canvasTexture, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);

                // Calculate work group counts (round up division)
                int workGroupsX = (canvasTileWidth + 15) / 16;
                int workGroupsY = (canvasTileHeight + 15) / 16;

                // Dispatch compute shader
                GL.DispatchCompute(workGroupsX, workGroupsY, 1);

                // Ensure compute shader finishes before reading
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
            }

            /// <summary>
            /// Copy tile from byte array to canvas texture
            /// </summary>
            public void CopyTileToCanvas(
                int canvasTexture,
                int canvasWidth,
                int canvasHeight,
                byte[] tileData,
                int tileWidth,
                int tileHeight,
                int offsetX,
                int offsetY,
                int canvasTileWidth,
                int canvasTileHeight)
            {
                // Create temporary texture for tile data
                int tileTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, tileTexture);

                // Upload tile data
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgb,
                    tileWidth,
                    tileHeight,
                    0,
                    PixelFormat.Rgb,
                    PixelType.UnsignedByte,
                    tileData);

                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                // Copy tile to canvas
                CopyTileToCanvas(
                    canvasTexture,
                    canvasWidth,
                    canvasHeight,
                    tileTexture,
                    tileWidth,
                    tileHeight,
                    offsetX,
                    offsetY,
                    canvasTileWidth,
                    canvasTileHeight);

                // Cleanup temporary texture
                GL.DeleteTexture(tileTexture);
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
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    IntPtr.Zero);

                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                return texture;
            }

            /// <summary>
            /// Read canvas texture back to CPU memory
            /// </summary>
            public byte[] ReadCanvasTexture(int canvasTexture, int width, int height)
            {
                byte[] data = new byte[width * height * 4]; // RGBA

                GL.BindTexture(TextureTarget.Texture2D, canvasTexture);
                GL.GetTexImage(
                    TextureTarget.Texture2D,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    data);

                return data;
            }

            public static class Native
            {
                // ------------------------------------------------------------------
                // Platform detection
                // ------------------------------------------------------------------

                public static readonly bool IsWindows =
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

                public static readonly bool IsLinux =
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

                public static readonly bool IsOSX =
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

                // ------------------------------------------------------------------
                // Native library names
                // ------------------------------------------------------------------

                private const string GdkLibWin = "libgdk-3-0.dll";
                private const string GdkLibLin = "libgdk-3.so.0";
                private const string GdkLibOSX = "libgdk-3.dylib";

                // ------------------------------------------------------------------
                // Windows imports
                // ------------------------------------------------------------------

                [DllImport(GdkLibWin, EntryPoint = "gdk_gl_context_get_current")]
                private static extern IntPtr gdk_gl_context_get_current();

                [DllImport(GdkLibWin, EntryPoint = "gdk_gl_context_get_proc_address")]
                private static extern IntPtr gdk_gl_context_get_proc_address_w(
                    IntPtr context,
                    string procName
                );

                // ------------------------------------------------------------------
                // Linux imports
                // ------------------------------------------------------------------

                [DllImport(GdkLibLin, EntryPoint = "gdk_gl_context_get_current")]
                private static extern IntPtr linux_gdk_gl_context_get_current();

                [DllImport(GdkLibLin, EntryPoint = "gdk_gl_context_get_proc_address")]
                private static extern IntPtr linux_gdk_gl_context_get_proc_address(
                    IntPtr context,
                    string procName
                );

                // ------------------------------------------------------------------
                // macOS imports
                // ------------------------------------------------------------------

                [DllImport(GdkLibOSX, EntryPoint = "gdk_gl_context_get_current")]
                private static extern IntPtr osx_gdk_gl_context_get_current();

                [DllImport(GdkLibOSX, EntryPoint = "gdk_gl_context_get_proc_address")]
                private static extern IntPtr osx_gdk_gl_context_get_proc_address(
                    IntPtr context,
                    string procName
                );

                // ------------------------------------------------------------------
                // Public API
                // ------------------------------------------------------------------

                public static IntPtr GetCurrentGLContextPointer()
                {
                    if (IsWindows) return gdk_gl_context_get_current();
                    if (IsLinux) return linux_gdk_gl_context_get_current();
                    if (IsOSX) return osx_gdk_gl_context_get_current();

                    return IntPtr.Zero;
                }

                public static GLContext GetCurrentGLContext()
                {
                    IntPtr ptr = GetCurrentGLContextPointer();
                    if (ptr == IntPtr.Zero)
                        return null;

                    return GLib.Object.GetObject(ptr) as GLContext;
                }

                public static void LoadBindings(GLContext context)
                {
                    if (context == null)
                        throw new ArgumentNullException(nameof(context));
                    GL.LoadBindings(new GtkBindingsContext(context.Handle));
                }
                static class WglNative
                {
                    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
                    public static extern IntPtr wglGetProcAddress(string procName);

                    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
                    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

                    [DllImport("kernel32.dll")]
                    public static extern IntPtr LoadLibrary(string lpFileName);
                }
                private sealed class GtkBindingsContext : IBindingsContext
                {
                    private readonly IntPtr gdkContext;
                    private static readonly IntPtr OpenGl32 =
                        WglNative.LoadLibrary("opengl32.dll");

                    public GtkBindingsContext(IntPtr context)
                    {
                        gdkContext = context;
                    }

                    public IntPtr GetProcAddress(string procName)
                    {
                        // ---------------- Windows ----------------
                        if (Native.IsWindows)
                        {
                            // Try WGL first
                            IntPtr addr = WglNative.wglGetProcAddress(procName);
                            if (addr != IntPtr.Zero)
                                return addr;

                            // Fallback to opengl32.dll
                            return WglNative.GetProcAddress(OpenGl32, procName);
                        }

                        // ---------------- Linux ----------------
                        if (Native.IsLinux)
                            return Native.linux_gdk_gl_context_get_proc_address(gdkContext, procName);

                        // ---------------- macOS ----------------
                        if (Native.IsOSX)
                            return Native.osx_gdk_gl_context_get_proc_address(gdkContext, procName);

                        return IntPtr.Zero;
                    }
                }

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
                    throw new NotImplementedException();
                }

                private void OnRealized(object sender, EventArgs e)
                {
                    MakeCurrent();

                    if (Error != null)
                    {
                        Native.LoadBindings(Context);
                        initialized = true;
                    }
                }
            }

            public void Dispose()
            {
                if (computeShader != 0)
                {
                    GL.DeleteShader(computeShader);
                    computeShader = 0;
                }

                if (computeShaderProgram != 0)
                {
                    GL.DeleteProgram(computeShaderProgram);
                    computeShaderProgram = 0;
                }
            }
        }
        GLContext con;
        public Stitch(GLContext co)
        {
            con = co;
            Initialize(co);
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

        public void AddTile(GpuTile tileInfo, int width, int height, byte[] pixelData)
        {
            if (HasTile(tileInfo.Index))
                return;

            var gpuTile = new GpuTile(tileInfo, pixelData, width, height);
            gpuTiles.Add(gpuTile);

            if (initialized && textureCache != null)
            {
                textureCache.UploadTexture(tileInfo.Index, pixelData, width, height);
            }
        }

        // Initialization
        public bool Initialize(GLContext con)
        {
            if (con == null)
                return false;
            St:
            try
            {
                
                if (initialized)
                    return true;
                textureCache = new TileTextureCache();
                stitcher = new OpenGLStitcher(con);
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
            double viewResolution)
        {
            try
            {
                if(stitcher == null)
                {
                    stitcher = new OpenGLStitcher(con);
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
                return null;
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
        bool init = false;
        private int currentFboWidth = -1;
        private int currentFboHeight = -1;

        public OpenGLStitcher(GLContext con)
        {
            if (!init)
            {
                InitializeOpenGL(con);
                init = true;
            }
        }

        private void InitializeOpenGL(GLContext con)
        {
            if (shaderProgram != null)
                return;
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
            try
            {
                int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
                int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

                programId = GL.CreateProgram();
                GL.AttachShader(programId, vertexShader);
                GL.AttachShader(programId, fragmentShader);
                GL.LinkProgram(programId);

                CheckProgramLinkStatus();

                //GL.DeleteShader(vertexShader);
                //GL.DeleteShader(fragmentShader);
            }
            catch (Exception e)
            {
                GLContext.Current.MakeCurrent();
                //Native.LoadBindings(context);
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
