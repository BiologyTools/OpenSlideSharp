﻿using OpenSlideGTK.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

namespace OpenSlideGTK
{
    /// <summary>
    /// openslide warpper
    /// </summary>
    public partial class OpenSlideImage : IDisposable
    {
        /// <summary>
        /// openslide_t*
        /// </summary>
        public IntPtr Handle { get; protected set; }

        /// <summary>
        /// Quickly determine whether a whole slide image is recognized.
        /// </summary>
        /// <remarks>
        /// If OpenSlide recognizes the file referenced by <paramref name="filename"/>, 
        /// return a string identifying the slide format vendor.This is equivalent to the
        /// value of the <see cref="NativeMethods.VENDOR"/> property. Calling
        /// <see cref="Open(string)"/> on this file will return a valid 
        /// OpenSlide object or an OpenSlide object in error state.
        ///
        /// Otherwise, return <see langword="null"/>.Calling <see cref="
        /// Open(string)"/> on this file will also
        /// return <see langword="null"/>.</remarks>
        /// <param name="filename">The filename to check. On Windows, this must be in UTF-8.</param>
        /// <returns>An identification of the format vendor for this file, or NULL.</returns>
        public static string DetectVendor(string filename)
        {
            if (NativeMethods.isWindows)
                return NativeMethods.Windows.DetectVendor(filename);
            else if (NativeMethods.isLinux)
                return NativeMethods.Linux.DetectVendor(filename);
            else return NativeMethods.OSX.DetectVendor(filename);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="handle"></param>
        /// <param name="isOwner">close handle when disposed</param>
        /// <exception cref="OpenSlideException"/>
        public OpenSlideImage(IntPtr handle, bool isOwner = true)
        {
            if (handle == IntPtr.Zero)
                throw new OpenSlideException(new FormatException().Message);
            Handle = handle;
            disposedValue = !isOwner;
            CheckIfThrow(0, isOwner);
        }

        /// <summary>
        /// Add .dll directory to PATH
        /// </summary>
        /// <param name="path"></param>
        /// <exception cref="OpenSlideException"/>
        public static void Initialize(string path = null)
        {
            var openslide = Path.Combine(Directory.GetParent(Assembly.GetCallingAssembly().Location)?.FullName, Path.Combine("openslide", $"{(IntPtr.Size == 8 ? "x64" : "x86")}"));
            path = string.IsNullOrEmpty(path) ? openslide : path;
            if (Directory.Exists(path))
            {
                var PATH = Environment.GetEnvironmentVariable("PATH");
                if (!PATH.ToUpper().Contains(path.ToUpper()))
                {
                    Environment.SetEnvironmentVariable("PATH", $"{PATH};{path}");
                }
            }
        }
        static OpenSlideImage()
        {
            Initialize();
        }

        /// <summary>
        /// Open.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        /// <exception cref="OpenSlideException"/>
        public static OpenSlideImage Open(string filename)
        {
            if (NativeMethods.isWindows)
                return new OpenSlideImage(NativeMethods.Windows.Open(filename));
            else if (NativeMethods.isLinux)
                return new OpenSlideImage(NativeMethods.Linux.Open(filename));
            else
                return new OpenSlideImage(NativeMethods.OSX.Open(filename));
        }

        /// <summary>
        /// Get the number of levels in the whole slide image.
        /// </summary>
        /// <return>The number of levels, or -1 if an error occurred.</return> 
        /// <exception cref="OpenSlideException"/>
        public int LevelCount
        {
            get
            {
                if (NativeMethods.isWindows)
                {
                    var result = NativeMethods.Windows.GetLevelCount(Handle);
                    return result != -1 ? result : CheckIfThrow(result);
                } else if (NativeMethods.isLinux)
                {
                    var result = NativeMethods.Linux.GetLevelCount(Handle);
                    return result != -1 ? result : CheckIfThrow(result);
                }
                else
                {
                    var result = NativeMethods.OSX.GetLevelCount(Handle);
                    return result != -1 ? result : CheckIfThrow(result);
                }
            }
        }

        private ImageDimension? _dimensionsRef;
        private readonly object _dimensionsSynclock = new object();

        /// <summary>
        /// Get the dimensions of level 0 (the largest level). Exactly
        /// equivalent to calling GetLevelDimensions(0).
        /// </summary>
        /// <exception cref="OpenSlideException"/>
        public ImageDimension Dimensions
        {
            get
            {
                if (_dimensionsRef == null)
                {
                    lock (_dimensionsSynclock)
                    {
                        if (_dimensionsRef == null)
                            _dimensionsRef = GetLevelDimension(0);
                    }
                }
                return _dimensionsRef.Value;
            }
        }

        /// <summary>
        /// The value of the property containing a slide's comment, if any.
        /// </summary>
        public string Comment => TryGetProperty(NativeMethods.COMMENT, out var value) ? value : null;

        /// <summary>
        /// The value of the property containing an identification of the vendor.
        /// </summary>
        public string Vendor => TryGetProperty(NativeMethods.VENDOR, out var value) ? value : null;

        /// <summary>
        /// The value of the property containing the "quickhash-1" sum.
        /// </summary>
        public string Quickhash1 => TryGetProperty(NativeMethods.QUICKHASH1, out var value) ? value : null;

        /// <summary>
        /// The value of the property containing a slide's background color, if any.
        /// It is represented as an RGB hex triplet.
        /// </summary>
        public string BackgroundColor => TryGetProperty(NativeMethods.BACKGROUND_COLOR, out var value) ? value : null;

        /// <summary>
        /// The value of the property containing a slide's objective power, if known.
        /// </summary>
        public double? ObjectivePower => TryGetProperty(NativeMethods.OBJECTIVE_POWER, out var value) && double.TryParse(value, out var result) ? (double?)result : null;

        /// <summary>
        /// The value of the property containing the number of microns per pixel in
        /// the X dimension of level 0, if known.
        /// </summary>
        public double? MicronsPerPixelX => TryGetProperty(NativeMethods.MPP_X, out var value) && double.TryParse(value, out var result) ? (double?)result : null;

        /// <summary>
        /// The value of the property containing the number of microns per pixel in
        /// the Y dimension of level 0, if known.
        /// </summary>
        public double? MicronsPerPixelY => TryGetProperty(NativeMethods.MPP_Y, out var value) && double.TryParse(value, out var result) ? (double?)result : null;

        /// <summary>
        /// The value of the property containing the X coordinate of the rectangle
        /// bounding the non-empty region of the slide, if available.
        /// </summary>
        public long? BoundsX => TryGetProperty(NativeMethods.BOUNDS_X, out var value) && long.TryParse(value, out var result) ? (long?)result : null;

        /// <summary>
        /// The value of the property containing the Y coordinate of the rectangle
        /// bounding the non-empty region of the slide, if available.
        /// </summary>
        public long? BoundsY => TryGetProperty(NativeMethods.BOUNDS_Y, out var value) && long.TryParse(value, out var result) ? (long?)result : null;

        /// <summary>
        /// The value of the property containing the width of the rectangle bounding
        /// the non-empty region of the slide, if available.
        /// </summary>
        public long? BoundsWidth => TryGetProperty(NativeMethods.BOUNDS_WIDTH, out var value) && long.TryParse(value, out var result) ? (long?)result : null;

        /// <summary>
        /// The value of the property containing the height of the rectangle bounding
        /// the non-empty region of the slide, if available.
        /// </summary>
        public long? BoundsHeight => TryGetProperty(NativeMethods.BOUNDS_HEIGHT, out var value) && long.TryParse(value, out var result) ? (long?)result : null;

        /// <summary>
        /// Get the dimensions of a level.
        /// </summary>
        /// <param name="level">The desired level.</param>
        /// <exception cref="OpenSlideException"/>
        public ImageDimension GetLevelDimension(int level)
        {
            if (NativeMethods.isWindows)
            {
                ImageDimension dimensions = new ImageDimension();
                NativeMethods.Windows.GetLevelDimensions(Handle, level, out dimensions.width, out dimensions.height);
                return dimensions.Height >= 0 && dimensions.Width >= 0 ? dimensions : CheckIfThrow(dimensions);
            }
            else if (NativeMethods.isLinux)
            {
                ImageDimension dimensions = new ImageDimension();
                NativeMethods.Linux.GetLevelDimensions(Handle, level, out dimensions.width, out dimensions.height);
                return dimensions.Height >= 0 && dimensions.Width >= 0 ? dimensions : CheckIfThrow(dimensions);
            }
            else
            {
                ImageDimension dimensions = new ImageDimension();
                NativeMethods.OSX.GetLevelDimensions(Handle, level, out dimensions.width, out dimensions.height);
                return dimensions.Height >= 0 && dimensions.Width >= 0 ? dimensions : CheckIfThrow(dimensions);
            }
        }

        /// <summary>
        /// Get all level dimensions.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="OpenSlideException"/>
        public IEnumerable<ImageDimension> GetLevelDimensions()
        {
            var count = LevelCount;
            for (int i = 0; i < count; i++)
            {
                yield return GetLevelDimension(i);
            }
        }

        /// <summary>
        /// Get the downsampling factor of a given level.
        /// </summary>
        /// <param name="level">The desired level.</param>
        /// <return>
        /// The downsampling factor for this level, or -1.0 if an error occurred
        /// or the level was out of range.
        /// </return> 
        /// <exception cref="OpenSlideException"/>
        public double GetLevelDownsample(int level)
        {
            if (NativeMethods.isWindows)
            {
                var result = NativeMethods.Windows.GetLevelDownsample(Handle, level);
                return result != -1.0d ? result : CheckIfThrow(result);
            } else if (NativeMethods.isLinux)
            {
                var result = NativeMethods.Linux.GetLevelDownsample(Handle, level);
                return result != -1.0d ? result : CheckIfThrow(result);
            }
            else
            {
                var result = NativeMethods.OSX.GetLevelDownsample(Handle, level);
                return result != -1.0d ? result : CheckIfThrow(result);
            }
        }

        /// <summary>
        /// Get the best level to use for displaying the given downsample.
        /// </summary>
        /// <param name="downsample">The downsample factor.</param> 
        /// <return>The level identifier, or -1 if an error occurred.</return> 
        /// <exception cref="OpenSlideException"/>
        public int GetBestLevelForDownsample(double downsample)
        {
            if (NativeMethods.isWindows)
            {
                var result = NativeMethods.Windows.GetBestLevelForDownsample(Handle, downsample);
                return result != -1 ? result : CheckIfThrow(result);
            } else if (NativeMethods.isLinux)
            {
                var result = NativeMethods.Linux.GetBestLevelForDownsample(Handle, downsample);
                return result != -1 ? result : CheckIfThrow(result);
            }
            else
            {
                var result = NativeMethods.OSX.GetBestLevelForDownsample(Handle, downsample);
                return result != -1 ? result : CheckIfThrow(result);
            }
        }

        /// <summary>
        /// Copy pre-multiplied BGRA data from a whole slide image.
        /// </summary>
        /// <param name="level">The desired level.</param>
        /// <param name="x">The top left x-coordinate, in the level 0 reference frame.</param>
        /// <param name="y">The top left y-coordinate, in the level 0 reference frame.</param>
        /// <param name="width">The width of the region. Must be non-negative.</param>
        /// <param name="height">The height of the region. Must be non-negative.</param>
        /// <returns>The pixel data of this region.</returns>
        /// <exception cref="ArgumentOutOfRangeException"/>
        /// <exception cref="OpenSlideException"/>
        public unsafe byte[] ReadRegion(int level, long x, long y, long width, long height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (NativeMethods.isWindows)
            {
                var data = new byte[width * height * 4];
                NativeMethods.Windows.ReadRegion(Handle, data, x, y, level, width, height);
                CheckIfThrow(0);
                return data;
            }
            else if (NativeMethods.isLinux)
            {
                var data = new byte[width * height * 4];
                NativeMethods.Linux.ReadRegion(Handle, data, x, y, level, width, height);
                CheckIfThrow(0);
                return data;
            }
            else
            {
                var data = new byte[width * height * 4];
                NativeMethods.OSX.ReadRegion(Handle, data, x, y, level, width, height);
                CheckIfThrow(0);
                return data;
            }

        }

        /// <summary>
        /// Copy pre-multiplied BGRA data from a whole slide image.
        /// </summary>
        /// <param name="level">The desired level.</param>
        /// <param name="x">The top left x-coordinate, in the level 0 reference frame.</param>
        /// <param name="y">The top left y-coordinate, in the level 0 reference frame.</param>
        /// <param name="width">The width of the region. Must be non-negative.</param>
        /// <param name="height">The height of the region. Must be non-negative.</param>
        /// <param name="data">The BGRA pixel data of this region.</param>
        /// <returns></returns>
        public unsafe bool TryReadRegion(int level, long x, long y, long width, long height, out byte[] data)
        {
            data = null;
            if (width <= 0) return false;
            if (height <= 0) return false;
            if (NativeMethods.isWindows)
            {
                data = new byte[width * height * 4];
                NativeMethods.Windows.ReadRegion(Handle, data, x, y, level, width, height);
                if (NativeMethods.Windows.GetError(Handle) is string)
                    return false;
                return true;
            }
            else if (NativeMethods.isLinux)
            {
                data = new byte[width * height * 4];
                NativeMethods.Linux.ReadRegion(Handle, data, x, y, level, width, height);
                if (NativeMethods.Linux.GetError(Handle) is string)
                    return false;
                return true;
            }
            else
            {
                data = new byte[width * height * 4];
                NativeMethods.OSX.ReadRegion(Handle, data, x, y, level, width, height);
                if (NativeMethods.OSX.GetError(Handle) is string)
                    return false;
                return true;
            }
        }

        ///<summary>
        ///Close an OpenSlide object.
        ///</summary>
        ///<remarks>
        ///No other threads may be using the object.
        ///After this call returns, the object cannot be used anymore.
        ///</remarks>
        public void Close()
        {
            if (NativeMethods.isWindows)
            {
                NativeMethods.Windows.Close(Handle);
                Handle = IntPtr.Zero;
            } else if (NativeMethods.isLinux)
            {
                NativeMethods.Linux.Close(Handle);
                Handle = IntPtr.Zero;
            }
            else
            {
                NativeMethods.OSX.Close(Handle);
                Handle = IntPtr.Zero;
            }
        }


        /* 
         * Querying properties.
         */
        #region Properties

        /// <summary>
        /// Get the value of a property.
        /// </summary>
        /// <param name="name">property name</param>
        /// <returns></returns>
        /// <exception cref="OpenSlideException"/>
        [IndexerName("Property")]
        public string this[string name]
        {
            get
            {
                if (NativeMethods.isWindows)
                {
                    return CheckIfThrow(NativeMethods.Windows.GetPropertyValue(Handle, name));
                } else if (NativeMethods.isLinux)
                {
                    return CheckIfThrow(NativeMethods.Linux.GetPropertyValue(Handle, name));
                }
                else
                {
                    return CheckIfThrow(NativeMethods.OSX.GetPropertyValue(Handle, name));
                }
            }
        }
        /// <summary>
        /// Get the array of property names. 
        /// </summary>
        /// <returns>The array of property names</returns>
        /// <exception cref="OpenSlideException"/>
        public string[] GetPropertyNames
        {
            get
            {
                if (NativeMethods.isWindows)
                {
                    return CheckIfThrow(NativeMethods.Windows.GetPropertyNames(Handle));
                } else if (NativeMethods.isLinux)
                {
                    return CheckIfThrow(NativeMethods.Linux.GetPropertyNames(Handle));
                }
                else
                {
                    return CheckIfThrow(NativeMethods.OSX.GetPropertyNames(Handle));
                }
            }
        }
        /// <summary>
        /// Gets the property value.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="value">The value of the property.</param>
        /// <returns>True if the property of the specified name exists. Otherwise, false.</returns>
        public bool TryGetProperty(string name, out string value)
        {
            if (NativeMethods.isWindows)
            {
                value = NativeMethods.Windows.GetPropertyValue(Handle, name);
                return value is string;
            }
            else if (NativeMethods.isLinux)
            {
                value = NativeMethods.Linux.GetPropertyValue(Handle, name);
                return value is string;
            }
            else
            {
                value = NativeMethods.OSX.GetPropertyValue(Handle, name);
                return value is string;
            }
        }

        /// <summary>
        /// Gets the property value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name">The name of the property.</param>
        /// <returns></returns>
        /// <exception cref="OpenSlideException"/>
        public T GetProperty<T>(string name)
        {
            if (NativeMethods.isWindows)
            {
                var value = CheckIfThrow(NativeMethods.Windows.GetPropertyValue(Handle, name));
                return (T)Convert.ChangeType(value, typeof(T));
            }
            else if (NativeMethods.isLinux)
            {
                var value = CheckIfThrow(NativeMethods.Linux.GetPropertyValue(Handle, name));
                return (T)Convert.ChangeType(value, typeof(T));
            }
            else
            {
                var value = CheckIfThrow(NativeMethods.OSX.GetPropertyValue(Handle, name));
                return (T)Convert.ChangeType(value, typeof(T));
            }
        }

        /// <summary>
        /// Get all properites.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<KeyValuePair<string, string>> GetProperties()
        {
            var keys = GetPropertyNames;
            foreach (var key in keys)
            {
                yield return new KeyValuePair<string, string>(key, this[key]);
            }
        }

        #endregion

        /* 
         * Reading associated images.
         */
        #region Associated Images


        /// <summary>
        /// Get the array of names of associated images. 
        /// </summary>
        /// <returns>The array of names of associated images.</returns>
        /// <exception cref="OpenSlideException"/>
        public string[] GetAssociatedImageNames
        {
            get
            {
                if (NativeMethods.isWindows)
                {
                    return CheckIfThrow(NativeMethods.Windows.GetAssociatedImageNames(Handle));
                }
                else if (NativeMethods.isLinux)
                {
                    return CheckIfThrow(NativeMethods.Linux.GetAssociatedImageNames(Handle));
                }
                else
                {
                    return CheckIfThrow(NativeMethods.OSX.GetAssociatedImageNames(Handle));
                }
            }
        }

        /// <summary>
        /// Gets the dimensions of the associated image.
        /// </summary>
        /// <param name="name">The name of the associated image.</param>
        /// <param name="dimensions">The dimensions of the associated image.</param>
        /// <returns>True if the associated image of the specified name exists. Otherwise, false.</returns>

        public bool TryGetAssociatedImageDimensions(string name, out ImageDimension dimensions)
        {

            if (NativeMethods.isWindows)
            {
                dimensions = default;
                NativeMethods.Windows.GetAssociatedImageDimensions(Handle, name, out dimensions.width, out dimensions.height);
                return dimensions.Width >= 0 && dimensions.Height >= 0;
            }
            else if (NativeMethods.isLinux)
            {
                dimensions = default;
                NativeMethods.Linux.GetAssociatedImageDimensions(Handle, name, out dimensions.width, out dimensions.height);
                return dimensions.Width >= 0 && dimensions.Height >= 0;
            }
            else
            {
                dimensions = default;
                NativeMethods.OSX.GetAssociatedImageDimensions(Handle, name, out dimensions.width, out dimensions.height);
                return dimensions.Width >= 0 && dimensions.Height >= 0;
            }
        }

        /// <summary>
        /// Gets the dimensions of the associated image.
        /// </summary>
        /// <param name="name">The name of the associated image.</param>
        /// <returns>The dimensions of the associated image.</returns>
        /// <exception cref="OpenSlideException"/>
        public ImageDimension GetAssociatedImageDimensions(string name)
        {
            return TryGetAssociatedImageDimensions(name, out var dim) ? dim : throw new OpenSlideException(new KeyNotFoundException().Message);
        }

        /// <summary>
        /// Copy pre-multiplied BGRA data from an associated image.
        /// </summary>
        /// <param name="name">The name of the associated image.</param>
        /// <param name="image">The associated image.</param>
        /// <returns>The pixel data of the associated image.</returns>
        public unsafe bool TryGetAssociatedImage(string name, out AssociatedImage image)
        {
            if (TryGetAssociatedImageDimensions(name, out var dimensions))
            {
                var dest = new byte[dimensions.Width * dimensions.Height * 4];
                if (dest.Length > 0)
                {
                    fixed (byte* pdata = dest)
                    {
                        if (NativeMethods.isWindows)
                        {
                            NativeMethods.Windows.ReadAssociatedImage(Handle, name, pdata);
                        }
                        else if (NativeMethods.isLinux)
                        {
                            NativeMethods.Linux.ReadAssociatedImage(Handle, name, pdata);
                        }
                        else
                        {
                            NativeMethods.OSX.ReadAssociatedImage(Handle, name, pdata);
                        }

                    }
                    image = new AssociatedImage(dimensions, dest);
                    return true;
                }
            }
            image = default;
            return false;
        }

        /// <summary>
        /// Copy pre-multiplied BGRA data from an associated image.
        /// </summary>
        /// <param name="name">The name of the associated image.</param>
        /// <returns></returns>
        public AssociatedImage GetAssociatedImage(string name)
        {
            return TryGetAssociatedImage(name, out var image) ? image : throw new OpenSlideException(new KeyNotFoundException().Message);
        }

        /// <summary>
        /// Get all associated images.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<KeyValuePair<string, AssociatedImage>> GetAssociatedImages()
        {
            var keys = GetAssociatedImageNames;
            foreach (var key in keys)
            {
                if (TryGetAssociatedImage(key, out var image))
                    yield return new KeyValuePair<string, AssociatedImage>(key, image);
            }
        }

        #endregion

        /* 
         * Utility functions.
         */
        #region Miscellaneous

        ///<summary>
        ///Get the version of the OpenSlide library.
        ///</summary>
        ///<return>A string describing the OpenSlide version.</return> 
        public static string LibraryVersion
        {
            get
            {
                if (NativeMethods.isWindows)
                {
                    return NativeMethods.Windows.GetVersion();
                }
                else if (NativeMethods.isLinux)
                {
                    return NativeMethods.Linux.GetVersion();
                }
                else
                {
                    return NativeMethods.OSX.GetVersion();
                }
                
            }
        }

        #endregion

        private T CheckIfThrow<T>(T value, bool isDispose = false)
        {
            if (NativeMethods.isWindows)
            {
                if (NativeMethods.Windows.GetError(Handle) is string error)
                {
                    if (isDispose)
                        Dispose(true);
                    throw new OpenSlideException(error);
                }
                return value;
            }
            else if (NativeMethods.isLinux)
            {
                if (NativeMethods.Linux.GetError(Handle) is string error)
                {
                    if (isDispose)
                        Dispose(true);
                    throw new OpenSlideException(error);
                }
                return value;
            }
            else
            {
                if (NativeMethods.OSX.GetError(Handle) is string error)
                {
                    if (isDispose)
                        Dispose(true);
                    throw new OpenSlideException(error);
                }
                return value;
            }
            
        }


        #region IDisposable

        private bool disposedValue;

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                Close();
                disposedValue = true;
            }
        }

        /// <summary>
        /// </summary>
        ~OpenSlideImage()
        {
            Dispose(disposing: false);
        }

        /// <summary>
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
