
![openslide](./openslide_logo.png)

[![NuGet version (OpenSlideGTK)](https://img.shields.io/nuget/v/OpenSlideGTK.svg?style=flat-square)](https://www.nuget.org/packages/OpenSlideGTK/1.2.0)

# OpenSlideGTK
.NET6 cross-platform Windows, Linux, Mac, bindings for OpenSlide (http://openslide.org/). As well as cross platform .NET6 slide viewer.  

Thank you to @IOL0ol1 for his work on [OpenSlideSharp](https://github.com/IOL0ol1/OpenSlideSharp).

Thank you to @yigolden for his work on [OpenSlideNET](https://github.com/yigolden/OpenSlideNET).

Nuget    
```ps
Install-Package OpenSlideGTK -Version 1.2.0
```

## Index

1.  [OpenSlideGTK](/src/OpenSlideGTK)    
    Openslide warpper, include DeepZoomGenerator, but no native *.dlls.
2.  [SlideViewer](/example/SlideViewer/)
    .NET6 Cross-platform example project for Viewing Slide with GTK.