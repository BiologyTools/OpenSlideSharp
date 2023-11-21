
![openslide](./openslide_logo.png)

# OpenSlideGTK
.NET6 Cross Platform version of OpenSlideSharp (http://openslide.org/).

Thank you to @IOL0ol1 for his work on [OpenSlideSharp](https://github.com/IOL0ol1/OpenSlideSharp).
Thank you to @yigolden for his work on [OpenSlideNET](https://github.com/yigolden/OpenSlideNET).

## Index

1.  [OpenSlideSharp](/src/OpenSlideSharp)    
    openslide warpper, include DeepZoomGenerator, but no native *.dll.

2.  [OpenSlideSharp.BitmapExtensions](/src/OpenSlideSharp.BitmapExtensions)    
    -Removed System.Drawing Dependency to support all platforms.

3.  [OpenSlideSharp.OpencvExtensions](/src/OpenSlideSharp.OpencvExtensions)       
    OpenSlideSharp with OpenCV mat extensions.    
    bgra raw data    
    -ToJepg    
    -ToPng    
    -ToMat    
    ...

4.  [OpenSlideSharp.BruTile](/src/OpenSlideSharp.BruTile)    
    OpenSlideSharp adapter [BruTile](https://github.com/BruTile/BruTile) (OpenSlideImage -> ITileSource).

5.  [OpenSlideSharp.runtime.win](/src/OpenSlideSharp.runtime.win)    
    OpenSlide runtime, include native *.dll files.

6.  [OpenSlideSharp.Windows](/src/OpenSlideSharp.Windows)    
    OpenSlideSharp for windows all in one, include [1],[2],[5].

## Suggestion
1.  The GIS (Geographic Information System) suite (include layer, editor) makes it easy to develop features related to medical image slides. It would be better to use [openlayer](https://openlayers.org/) or [Leaflet](https://leafletjs.com/) for web.    

2.  If you want to customize a digital medical imaging format, Perhaps refer to the implementation of **MBTiles**, using the **SQLite** database. Sometimes it is necessary to encrypt the data, and that's okay, use a **SQLCipher** database(compatible with sqlite). Databases are highly scalable and easy to use.


## Example 
A slide viewer by [Mapsui](https://github.com/Mapsui/Mapsui)    
Download [here](https://github.com/IOL0ol1/OpenSlideSharp/releases)    
![mapsui](./preview.gif)
