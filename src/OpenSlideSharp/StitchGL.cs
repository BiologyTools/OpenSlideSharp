using AForge;
using BruTile;
using BruTile.Predefined;
using BruTile.Web;
using OpenTK.Graphics.OpenGL4;
using PixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;
using PixelType = OpenTK.Graphics.OpenGL4.PixelType;
using OpenTK.Windowing.Desktop;
using AForge.Imaging.Filters;
using Atk;
using System.Collections.Generic;
using System;
namespace OpenSlideGTK
{
    public class GLStitch
    {
        private int _shaderProgram;
        private List<int> _tileTextures = new List<int>();
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _texture;
        private ITileSource _tileSource;
        private Extent _extent;
        private int _zoomLevel;
        private const int maxTiles = 100;
        private int _fbo;
        private GameWindow window;
        private int _renderedTexture;
        private int _textureWidth = 1024;  // Set the desired width
        private int _textureHeight = 1024; // Set the desired height
        private List<float[]> _tileQuads = new List<float[]>();
        public static List<Extent> tiles = new List<Extent>();
        private static bool initialized = false;
        public OpenSlideBase Slide
        {
            get;set;
        }
        public GLStitch(OpenSlideBase bm)
        {
            // Initialize on the main thread
            Initialize();
            Slide = bm;
        }

        private void Initialize()
        {
            // Initialize OpenGL bindings (important for OpenTK)
            if (window == null)
            {
                window = new GameWindow(GameWindowSettings.Default, NativeWindowSettings.Default);
                window.IsVisible = false;  // Invisible window for off-screen rendering
            }

            // Set up FBO and texture for off-screen rendering
            SetupFBO();

            // Compile shaders
            _shaderProgram = CompileShaders();

            // Set up vertex data and buffers, configure vertex attributes
            SetupQuad();

            // Initialize BruTile source
            _tileSource = new HttpTileSource(new GlobalSphericalMercator(), "http://tile.openstreetmap.org/{z}/{x}/{y}.png", new[] { "a", "b", "c" });

            // Set the extent and zoom level based on BioImage properties
            _extent = new Extent(0, 0, 600, 400);  // Example extent
            _zoomLevel = 1;

            // Load tiles asynchronously
            LoadTiles();

            initialized = true;
        }
        private void InitializeBuffers()
        {
            
            // Initialize OpenGL bindings (important for OpenTK)
            if (window == null)
                window = new GameWindow(GameWindowSettings.Default, NativeWindowSettings.Default);
            window.IsVisible = false;
            // Set up the FBO and texture for off-screen rendering
            SetupFBO();
            // Compile shaders
            _shaderProgram = CompileShaders();

            // Set up vertex data and buffers and configure vertex attributes
            float[] vertices = {
            // Positions      // TexCoords
            -1.0f,  1.0f, 0.0f, 1.0f, // Top-left
            -1.0f, -1.0f, 0.0f, 0.0f, // Bottom-left
             1.0f, -1.0f, 1.0f, 0.0f, // Bottom-right
             1.0f,  1.0f, 1.0f, 1.0f  // Top-right
        };

            uint[] indices = {
            0, 1, 2,   // First triangle
            2, 3, 0    // Second triangle
        };

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_vao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            // Position attribute
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // TexCoord attribute
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            /*
            // Initialize BruTile source
            _tileSource = new HttpTileSource(new GlobalSphericalMercator(),
                "http://tile.openstreetmap.org/{z}/{x}/{y}.png",
                new[] { "a", "b", "c" }, name: "OSM");
            */
            // Set the extent to a specific geographic region (e.g., a city)
            _extent = new Extent(0, 0, 600, 400);
            _zoomLevel = (int)1; // Set a zoom level (adjust according to your needs)
            initialized = true;
        }

        private void SetupQuad()
        {
            // Set up quad vertices and indices
            float[] vertices = {
                // Positions      // TexCoords
                -1.0f,  1.0f, 0.0f, 1.0f,  // Top-left
                -1.0f, -1.0f, 0.0f, 0.0f,  // Bottom-left
                 1.0f, -1.0f, 1.0f, 0.0f,  // Bottom-right
                 1.0f,  1.0f, 1.0f, 1.0f   // Top-right
            };

            uint[] indices = {
                0, 1, 2,   // First triangle
                2, 3, 0    // Second triangle
            };

            // Generate VAO, VBO, and EBO
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_vao);

            // Upload vertex data
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            // Upload index data
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            // Define position attribute
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // Define texture coordinate attribute
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
        }

        private void SetupFBO()
        {
            // Create and bind the framebuffer
            _fbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

            // Create the texture to render to
            _renderedTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _renderedTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, _textureWidth, _textureHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

            // Set texture parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Attach the texture to the framebuffer
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _renderedTexture, 0);

            // Check if the framebuffer is complete
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            {
                throw new Exception("Framebuffer is not complete.");
            }

            // Unbind the framebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void LoadTiles()
        {
            var tileSchema = _tileSource.Schema;
            var tiless = tileSchema.GetTileInfos(_extent, 1);
            foreach (var tile in tiless)
            {
                int w = (int)Math.Round(_extent.Width);
                int h = (int)Math.Round(_extent.Height);
                int x = (int)Math.Round(_extent.MinX);
                int y = (int)Math.Round(_extent.MinX);
                var bytes = Slide.GetTile(tile);
                AForge.Bitmap bm = new AForge.Bitmap(w, h, AForge.PixelFormat.Format32bppArgb, bytes, new ZCT(), "");
                int texture = LoadTexture(bm);
                _tileTextures.Add(texture);
                // Calculate the position for each tile
                var vertexData = CreateQuadForTile(tile);
                _tileQuads.Add(vertexData);
                tiles.Add(tile.Extent);
            }
        }

        private int LoadTexture(AForge.Bitmap bitmap)
        {
            // Load a texture from a bitmap
            int texture;
            GL.GenTextures(1, out texture);
            GL.BindTexture(TextureTarget.Texture2D, texture);

            // Load texture data into OpenGL
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bitmap.Width, bitmap.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.ImageRGB.Bytes);

            // Set texture filtering
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            return texture;
        }

        private float[] CreateQuadForTile(TileInfo tile)
        {
            // Transform tile extent into normalized device coordinates (NDC)
            var extent = tile.Extent;
            float[] quad = new float[8];

            // Calculate quad vertices in NDC
            quad[0] = (float)(2 * (extent.MinX - _extent.MinX) / _extent.Width - 1.0);  // x1
            quad[1] = (float)(2 * (extent.MaxY - _extent.MinY) / _extent.Height - 1.0); // y1
            quad[2] = (float)(2 * (extent.MinX - _extent.MinX) / _extent.Width - 1.0);  // x2
            quad[3] = (float)(2 * (extent.MinY - _extent.MinY) / _extent.Height - 1.0); // y2
            quad[4] = (float)(2 * (extent.MaxX - _extent.MinX) / _extent.Width - 1.0);  // x3
            quad[5] = (float)(2 * (extent.MinY - _extent.MinY) / _extent.Height - 1.0); // y3
            quad[6] = (float)(2 * (extent.MaxX - _extent.MinX) / _extent.Width - 1.0);  // x4
            quad[7] = (float)(2 * (extent.MaxY - _extent.MinY) / _extent.Height - 1.0); // y4

            return quad;
        }

        private int CompileShaders()
        {
            // Compile and link shaders
            string vertexShaderCode = @"
                #version 330 core
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec2 aTexCoord;
                out vec2 TexCoord;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    TexCoord = aTexCoord;
                }
            ";

            string fragmentShaderCode = @"
                #version 330 core
                out vec4 FragColor;
                in vec2 TexCoord;
                uniform sampler2D ourTexture;
                void main()
                {
                    FragColor = texture(ourTexture, TexCoord);
                }
            ";

            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderCode);
            GL.CompileShader(vertexShader);

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderCode);
            GL.CompileShader(fragmentShader);

            int shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);

            // Delete shaders after linking
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return shaderProgram;
        }

        public void Render()
        {
            if (!initialized) return;

            // Bind framebuffer and set viewport
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
            GL.Viewport(0, 0, _textureWidth, _textureHeight);

            // Clear framebuffer
            GL.Clear(ClearBufferMask.ColorBufferBit);

            // Bind the shader program
            GL.UseProgram(_shaderProgram);

            // Bind VAO and render each tile
            GL.BindVertexArray(_vao);

            foreach (var textureId in _tileTextures)
            {
                // Bind the texture
                GL.BindTexture(TextureTarget.Texture2D, textureId);

                // Draw the quad (two triangles)
                GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
            }

            // Unbind framebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void RenderFrame()
        {
            GL.Clear(ClearBufferMask.ColorBufferBit);

            GL.UseProgram(_shaderProgram);

            for (int i = 0; i < _tileTextures.Count; i++)
            {
                GL.BindTexture(TextureTarget.Texture2D, _tileTextures[i]);
                LoadTileQuad(_tileQuads[i]);
                GL.DrawArrays(PrimitiveType.Quads, 0, 4);
            }
            //Render();
        }

        private void LoadTileQuad(float[] vertexData)
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float), vertexData, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
        }

        public byte[] StitchImages(double x, double y, int pxwidth, int pxheight)
        {
            try
            {
                if (!initialized)
                {
                    Initialize();
                }


                double maxX = 0;
                double maxY = 0;
                double minX = 0;
                double minY = 0;
                // Calculate canvas size based on extents
                for (int i = 0; i < tiles.Count; i++)
                {
                    if (maxX < tiles[i].MaxX)
                        maxX = tiles[i].MaxX;
                    if (maxY < tiles[i].MaxY)
                        maxY = tiles[i].MaxY;
                    if (minX < tiles[i].MinX)
                        minX = tiles[i].MinX;
                    if (minX < tiles[i].MinX)
                        minX = tiles[i].MinX;
                }

                // Calculate canvas width and height
                int canvasWidth = (int)(maxX - minX);
                int canvasHeight = (int)(maxY - minY);
                // Download the result back to host
                AForge.Bitmap bm = GetImage();
                ResizeBilinear rs = new ResizeBilinear(pxwidth, pxheight);
                return rs.Apply(bm).Bytes;

            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
                return null;
            }
        }

        public void RenderToTexture()
        {
            // Bind the framebuffer where the scene will be rendered
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

            // Attach the texture to the framebuffer as the color attachment
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _renderedTexture, 0);

            // Optionally, check if the framebuffer is complete (good for debugging)
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                throw new Exception($"Framebuffer is not complete: {status}");
            }

            // Clear the framebuffer (color and depth buffer)
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Use the appropriate shader program
            GL.UseProgram(_shaderProgram);

            // Render your scene (this should render to the texture attached to the framebuffer)
            RenderFrame();

            // Unbind the framebuffer (this will switch back to the default framebuffer)
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }


        private AForge.Bitmap GetImage()
        {
            // Create a byte array to hold the pixel data
            var pixels = new byte[_textureWidth * _textureHeight * 3];

            // Bind the framebuffer to read from it
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            Render();
            RenderToTexture();

            // Read the pixel data from the framebuffer (RGB format)
            GL.ReadPixels(0, 0, _textureWidth, _textureHeight, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);

            // Create a new Bitmap object with the appropriate size and pixel format
            AForge.Bitmap bitmap = new AForge.Bitmap(_textureWidth, _textureHeight, AForge.PixelFormat.Format24bppRgb);

            // Lock the bits of the bitmap so we can write to it directly
            var bitmapData = bitmap.LockBits(new AForge.Rectangle(0, 0, _textureWidth, _textureHeight),
                                             AForge.ImageLockMode.ReadWrite,
                                             AForge.PixelFormat.Format24bppRgb);

            // Copy the pixel data into the bitmap
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);

            // Unlock the bits to update the bitmap
            bitmap.UnlockBits(bitmapData);

            // Flip the bitmap vertically as OpenGL's origin is at the bottom-left corner
            bitmap.RotateFlip(AForge.RotateFlipType.RotateNoneFlipY);

            return bitmap;
        }


        public void AddTile(Extent tile)
        {
            try
            {
                foreach (var t in tiles)
                {
                    if (t == tile)
                        return;
                }
                if (tiles.Count > maxTiles)
                {
                    tiles.RemoveAt(0);
                }
                tiles.Add(tile);
                LoadTexture(new AForge.Bitmap((int)Math.Round(tile.Width), (int)Math.Round(tile.Height), AForge.PixelFormat.Format24bppRgb));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

        }

        public static bool HasTile(Extent ex)
        {
            try
            {
                foreach (var item in tiles)
                {
                    if (item == ex)
                        return true;
                }
                return false;
            }
            catch (Exception e)
            {
                return false;
            }

        }
    }
}
