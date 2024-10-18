__global__ void copyTileToCanvas(unsigned char* canvas, int canvasWidth, int canvasHeight,
    unsigned char* tile, int tileWidth, int tileHeight,
    int offsetX, int offsetY, int canvasTileWidth, int canvasTileHeight)
{
    // Calculate the global x and y index for the thread
    int canvasX = blockIdx.x * blockDim.x + threadIdx.x;
    int canvasY = blockIdx.y * blockDim.y + threadIdx.y;

    // Check if the thread is within the bounds of the canvas
    if (canvasX < canvasTileWidth && canvasY < canvasTileHeight) {
        // Translate canvas coordinates into tile coordinates
        float tileX = ((float)canvasX / canvasTileWidth) * tileWidth;
        float tileY = ((float)canvasY / canvasTileHeight) * tileHeight;

        // Find nearest tile pixel for scaling (nearest neighbor scaling)
        int srcX = (int)tileX;
        int srcY = (int)tileY;

        // Ensure the tile indices are within bounds
        if (srcX < tileWidth && srcY < tileHeight) {
            // Calculate the destination index for the canvas
            int canvasIdx = ((canvasY + offsetY) * canvasWidth + (canvasX + offsetX)) * 3;

            // Calculate the source index for the tile
            int tileIdx = (srcY * tileWidth + srcX) * 3;

            // Ensure the canvas indices are within bounds
            if (canvasX + offsetX < canvasWidth && canvasY + offsetY < canvasHeight) {
                // Copy the pixel (RGB components) from tile to canvas
                canvas[canvasIdx] = tile[tileIdx];
                canvas[canvasIdx + 1] = tile[tileIdx + 1];
                canvas[canvasIdx + 2] = tile[tileIdx + 2];
            }
        }
    }
}
