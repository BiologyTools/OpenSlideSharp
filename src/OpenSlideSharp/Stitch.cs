using AForge;
using BruTile;
using Gtk;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
namespace OpenSlideGTK
{
    public class Stitch
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct TileData
        {
            public Extent Extent;        // Struct representing the Extent
            public CUdeviceptr DevTilePtr; // CUDA device pointer to the tile data

            public TileData(Extent extent, CUdeviceptr devTilePtr)
            {
                this.Extent = extent;
                this.DevTilePtr = devTilePtr;
            }
        }
        // Initialize CUDA context
        private const int maxTiles = 100;
        private CudaContext context;
        public List<Tuple<TileInfo, CudaDeviceVariable<byte>>> gpuTiles = new List<Tuple<TileInfo, CudaDeviceVariable<byte>>>();
        private CudaKernel kernel;
        private bool initialized = false;
        public Stitch()
        {
            Initialize();
            
        }
        public bool HasTile(Extent ex)
        {
            foreach (var item in gpuTiles)
            {
                if (item.Item1.Extent == ex)
                    return true;
            }
            return false;
        }
        public bool HasTile(TileInfo t)
        {
            foreach (var item in gpuTiles)
            {
                if (item.Item1.Index == t.Index && item.Item1.Extent == t.Extent)
                    return true;
            }
            return false;
        }
        public void DisposeLevel(int lev)
        {
            foreach(var item in gpuTiles)
            {
                if(item.Item1.Index.Level == lev)
                {
                    item.Item2?.Dispose();
                    gpuTiles.Remove(item);
                }
            }
        }
        public void AddTile(Tuple<TileInfo, byte[]> tile)
        {
            if (HasTile(tile.Item1))
                return;
            byte[] tileData;
            if (gpuTiles.Count > 0)
            {
                for (int i = 0; i < gpuTiles.Count; i++)
                {
                    if(gpuTiles[i].Item1.Index.Level != tile.Item1.Index.Level)
                    DisposeLevel(i);
                }
                tileData = tile.Item2;
            }
            else
                tileData = tile.Item2;
            try
            {
                CudaDeviceVariable<byte> devTile = new CudaDeviceVariable<byte>(tileData.Length);
                devTile.CopyToDevice(tileData);
                gpuTiles.Add(new Tuple<TileInfo, CudaDeviceVariable<byte>>(tile.Item1, devTile));
            }
            catch (Exception e)
            {
                Initialize();
                Console.WriteLine(e.Message);
            }

        }
        public static List<string> Args = new List<string>();
        public static void RestartApplication()
        {
            string exePath = Process.GetCurrentProcess().MainModule!.FileName!;
            string args = Environment.CommandLine;

            // Remove executable path from args
            int firstSpace = args.IndexOf(' ');
            if (firstSpace > 0)
                args = args.Substring(firstSpace + 1);
            else
                args = string.Empty;
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true
            });
            Application.Quit();
            Environment.Exit(0);
        }
        public string Initialize()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                OpenSlideBase.useGPU = false;
                SlideSourceBase.useGPU = false;
                return "";
            }
            try
            {
                context = new CudaContext();
                // Load the CUDA kernel
                kernel = context.LoadKernelPTX(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/tile_copy.ptx", "copyTileToCanvas");
                initialized = true;
                return "";
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                if (e.Message.Contains("IllegalAddress"))
                {
                    RestartApplication();
                    return "GPU IllegalAddress requires restart.";
                }
                OpenSlideBase.useGPU = false;
                SlideSourceBase.useGPU = false;
                return e.Message;
            }
        }
       
        public static Bitmap ConvertCudaDeviceVariableToBitmap(CudaDeviceVariable<byte> deviceVar, int width, int height, PixelFormat pixelFormat)
        {
            // Step 1: Allocate a byte array on the CPU (host)
            byte[] hostArray = new byte[deviceVar.Size];

            // Step 2: Copy the data from the GPU (device) to the CPU (host)
            deviceVar.CopyToHost(hostArray);

            // Step 3: Create a Bitmap object from the byte array
            Bitmap bitmap = new Bitmap(width, height, pixelFormat);

            // Step 4: Lock the bitmap's bits for writing
            BitmapData bmpData = bitmap.LockBits(new AForge.Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, pixelFormat);

            // Step 5: Copy the byte array to the bitmap's pixel buffer
            System.Runtime.InteropServices.Marshal.Copy(hostArray, 0, bmpData.Scan0, hostArray.Length);

            // Step 6: Unlock the bitmap's bits
            bitmap.UnlockBits(bmpData);

            // Return the Bitmap object
            return bitmap;
        }
        public byte[] StitchImages(List<TileInfo> tiles, int pxwidth, int pxheight, double x, double y, double resolution)
        {
            try
            {
                if (gpuTiles.Count == 0)
                    return null;
                // Convert world coordinates of tile extents to pixel space based on resolution
                foreach (var item in tiles)
                {
                    item.Extent = item.Extent.WorldToPixelInvertedY(resolution);
                }

                if (!initialized)
                {
                    Initialize();
                }

                // Calculate the bounding box (min/max extents) of the stitched image
                double maxX = tiles.Max(t => t.Extent.MaxX);
                double maxY = tiles.Max(t => t.Extent.MaxY);
                double minX = tiles.Min(t => t.Extent.MinX);
                double minY = tiles.Min(t => t.Extent.MinY);

                // Calculate canvas size in pixels
                int canvasWidth = (int)(maxX - minX);
                int canvasHeight = (int)(maxY - minY);

                // Allocate memory for the output stitched image on the GPU
                using (CudaDeviceVariable<byte> devCanvas = new CudaDeviceVariable<byte>(canvasWidth * canvasHeight * 3)) // 3 channels for RGB
                {
                    // Set block and grid sizes for kernel launch
                    dim3 blockSize = new dim3(16, 16, 1);
                    dim3 gridSize = new dim3((uint)((canvasWidth + blockSize.x - 1) / blockSize.x), (uint)((canvasHeight + blockSize.y - 1) / blockSize.y), 1);

                    // Iterate through each tile and copy it to the GPU canvas
                    foreach (var tile in tiles)
                    {
                        Extent extent = tile.Extent;

                        // Find the corresponding GPU tile (already loaded into GPU memory)
                        CudaDeviceVariable<byte> devTile = null;
                        foreach (var t in gpuTiles)
                        {
                            if (t.Item1.Index == tile.Index)
                            {
                                devTile = t.Item2;
                                break;
                            }
                        }

                        if (devTile != null)
                        {
                            // Calculate the start position on the canvas and the dimensions of the tile
                            int startX = (int)Math.Ceiling(extent.MinX - minX);
                            int startY = (int)Math.Ceiling(extent.MinY - minY);
                            int tileWidth = (int)Math.Ceiling(extent.MaxX - extent.MinX);
                            int tileHeight = (int)Math.Ceiling(extent.MaxY - extent.MinY);

                            // canvasTileWidth and canvasTileHeight handle the scaling of the tile to the canvas 
                            int canvasTileWidth = tileWidth;
                            int canvasTileHeight = tileHeight;

                            // Run the CUDA kernel to copy the tile to the canvas
                            kernel.BlockDimensions = blockSize;
                            kernel.GridDimensions = gridSize;

                            // Run the kernel, including scaling factors
                            kernel.Run(devCanvas.DevicePointer, canvasWidth, canvasHeight, devTile.DevicePointer, 256, 256, startX, startY, canvasTileWidth, canvasTileHeight);
                        }
                    }

                    // Download the stitched image from the GPU to the host (CPU)
                    byte[] stitchedImageData = new byte[canvasWidth * canvasHeight * 3]; // Assuming 3 channels (RGB)
                    devCanvas.CopyToHost(stitchedImageData);

                    // ========================================================================
                    // FIX: Proper viewport extraction with bounds checking
                    // ========================================================================

                    // Calculate viewport position in canvas space
                    int viewportX = (int)(x - minX);
                    int viewportY = (int)(y - minY);

                    // Clamp viewport to canvas bounds
                    int clippedX = Math.Max(0, Math.Min(viewportX, canvasWidth));
                    int clippedY = Math.Max(0, Math.Min(viewportY, canvasHeight));

                    // Calculate how much of the viewport actually fits in the canvas
                    int availableWidth = Math.Min(pxwidth, canvasWidth - clippedX);
                    int availableHeight = Math.Min(pxheight, canvasHeight - clippedY);

                    // Ensure we don't request negative or zero dimensions
                    if (availableWidth <= 0 || availableHeight <= 0)
                    {
                        Console.WriteLine($"Viewport outside canvas bounds. Canvas: {canvasWidth}x{canvasHeight}, Viewport: ({viewportX},{viewportY}) {pxwidth}x{pxheight}");
                        return new byte[pxwidth * pxheight * 3]; // Return black image
                    }

                    // Create viewport buffer (initialize to black)
                    byte[] viewportImageData = new byte[pxwidth * pxheight * 3];

                    // Copy only the available portion
                    System.Threading.Tasks.Parallel.For(0, availableHeight, row =>
                    {
                        try
                        {
                            int srcRow = clippedY + row;
                            int dstRow = row;

                            // Ensure we're within bounds
                            if (srcRow >= 0 && srcRow < canvasHeight)
                            {
                                int srcOffset = srcRow * canvasWidth * 3 + clippedX * 3;
                                int dstOffset = dstRow * pxwidth * 3;
                                int bytesToCopy = availableWidth * 3;

                                // Final safety check
                                if (srcOffset >= 0 &&
                                    srcOffset + bytesToCopy <= stitchedImageData.Length &&
                                    dstOffset >= 0 &&
                                    dstOffset + bytesToCopy <= viewportImageData.Length)
                                {
                                    Array.Copy(stitchedImageData, srcOffset, viewportImageData, dstOffset, bytesToCopy);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error copying row {row}: {ex.Message}");
                        }
                    });

                    return viewportImageData;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StitchImages error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Initialize(); // Reinitialize in case of errors
                return null;
            }
        }
    }

}
