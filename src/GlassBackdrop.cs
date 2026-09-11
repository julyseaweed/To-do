using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Effects;
using Windows.UI.Composition;

namespace Shike {
    // The caller owns a WS_EX_NOREDIRECTIONBITMAP HWND. All operations stay on
    // its UI thread; this class owns only the compositor and its visual tree.
    public sealed class GlassBackdrop : IDisposable {
        [ThreadStatic] static IntPtr dispatcherQueue;
        Compositor compositor;
        IBackdropCompositionTarget target;
        SpriteVisual visual;
        CompositionBackdropBrush backdrop;
        CompositionEffectFactory factory;
        CompositionEffectBrush brush;
        double currentScale;
        int currentWidth, currentHeight;
        bool disposed;

        public GlassBackdrop(IntPtr nativeWindow) {
            if (nativeWindow == IntPtr.Zero) throw new ArgumentException("A native window is required.", "nativeWindow");
            try {
                if (dispatcherQueue == IntPtr.Zero) {
                    var options = new DispatcherQueueOptions { Size = 12, ThreadType = 2, Apartment = 2 };
                    Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out dispatcherQueue));
                    // One dispatcher queue serves all backdrops on this thread.
                    // Retain it until the UI thread exits, including hide/restore.
                }
                compositor = new Compositor();
                IntPtr nativeTarget;
                ((IBackdropDesktopInterop)(object)compositor).CreateDesktopWindowTarget(nativeWindow, true, out nativeTarget);
                try { target = (IBackdropCompositionTarget)Marshal.GetObjectForIUnknown(nativeTarget); }
                finally { Marshal.Release(nativeTarget); }
                backdrop = compositor.CreateBackdropBrush();
                visual = compositor.CreateSpriteVisual();
                target.Root = visual;
                Resize(1, 1, 1);
            } catch {
                Dispose();
                throw;
            }
        }

        public void Resize(int width, int height, double dpiScale) {
            if (disposed) throw new ObjectDisposedException("GlassBackdrop");
            if (double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0) dpiScale = 1;
            if (brush == null || Math.Abs(currentScale - dpiScale) > 0.001) {
                // Composition coordinates are physical pixels. Keep the amount
                // of blur consistent as the window crosses display scales.
                var effect = new BackdropGaussianBlur {
                    Name = "GlassBlur", Sigma = (float)(11 * dpiScale),
                    Source = new CompositionEffectSourceParameter("Backdrop")
                };
                CompositionEffectFactory nextFactory = null;
                CompositionEffectBrush nextBrush = null;
                try {
                    nextFactory = compositor.CreateEffectFactory(effect);
                    nextBrush = nextFactory.CreateBrush();
                    nextBrush.SetSourceParameter("Backdrop", backdrop);
                    visual.Brush = nextBrush;
                } catch {
                    Close(nextBrush); Close(nextFactory);
                    throw;
                }
                var previousBrush = brush; var previousFactory = factory;
                brush = nextBrush; factory = nextFactory; currentScale = dpiScale;
                Close(previousBrush); Close(previousFactory);
            }
            width = Math.Max(1, width); height = Math.Max(1, height);
            if (currentWidth != width || currentHeight != height) {
                visual.Size = new Vector2(width, height);
                currentWidth = width; currentHeight = height;
            }
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            // Detach first so no live visual references a brush being closed.
            if (target != null) { try { target.Root = null; } catch (COMException) { } }
            if (visual != null) { try { visual.Brush = null; } catch (COMException) { } }
            Close(visual); visual = null;
            Close(brush); brush = null;
            Close(factory); factory = null;
            Close(backdrop); backdrop = null;
            if (target != null) {
                try { ((IBackdropClosable)target).Close(); } catch (COMException) { }
                target = null;
            }
            Close(compositor); compositor = null;
        }

        static void Close(IDisposable value) {
            if (value == null) return;
            try { value.Dispose(); } catch (COMException) { }
        }

        [StructLayout(LayoutKind.Sequential)] struct DispatcherQueueOptions { public int Size, ThreadType, Apartment; }
        [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IBackdropDesktopInterop { void CreateDesktopWindowTarget(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target); }
        [ComImport, Guid("A1BEA8BA-D726-4663-8129-6B5E7927FFA6"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IBackdropCompositionTarget { Visual Root { get; set; } }
        [ComImport, Guid("30D5A829-7FA4-4026-83BB-D75BAE4EA99E"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IBackdropClosable { void Close(); }
    }

    // Direct2D effect metadata for Windows Composition. This small descriptor
    // avoids a Win2D runtime dependency; pixels remain inside the compositor.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class BackdropGaussianBlur : IGraphicsEffect, IGraphicsEffectSource, IBackdropEffectInterop {
        const int InvalidArgument = unchecked((int)0x80070057);
        public string Name { get; set; }
        public float Sigma { get; set; }
        public IGraphicsEffectSource Source { get; set; }
        public int GetEffectId(out Guid id) { id = new Guid("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5"); return 0; }
        public int GetNamedPropertyMapping(string name, out uint index, out uint mapping) { index = mapping = 0; return InvalidArgument; }
        public int GetPropertyCount(out uint count) { count = 3; return 0; }
        public int GetProperty(uint index, out IntPtr value) {
            value = IntPtr.Zero;
            if (index > 2) return InvalidArgument;
            var properties = (IBackdropPropertyFactory)WindowsRuntimeMarshal.GetActivationFactory(typeof(Windows.Foundation.PropertyValue));
            IntPtr raw;
            if (index == 0) properties.CreateSingle(Sigma, out raw);
            else properties.CreateUInt32(1, out raw); // Balanced optimization, hard border.
            try {
                var propertyValue = new Guid("4BD682DD-7554-40E9-9A9B-82654EDE7E62");
                return Marshal.QueryInterface(raw, ref propertyValue, out value);
            } finally { Marshal.Release(raw); }
        }
        public int GetSource(uint index, out IGraphicsEffectSource source) { source = index == 0 ? Source : null; return index == 0 ? 0 : InvalidArgument; }
        public int GetSourceCount(out uint count) { count = 1; return 0; }
    }

    [ComVisible(true), Guid("2FC57384-A068-44D7-A331-30982FCF7177"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBackdropEffectInterop {
        [PreserveSig] int GetEffectId(out Guid id);
        [PreserveSig] int GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)] string name, out uint index, out uint mapping);
        [PreserveSig] int GetPropertyCount(out uint count);
        [PreserveSig] int GetProperty(uint index, out IntPtr value);
        [PreserveSig] int GetSource(uint index, out IGraphicsEffectSource source);
        [PreserveSig] int GetSourceCount(out uint count);
    }

    [ComImport, Guid("629BDBC8-D932-4FF4-96B9-8D96C5C1E858"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    interface IBackdropPropertyFactory {
        void CreateEmpty(out IntPtr value);
        void CreateUInt8(byte value, out IntPtr result);
        void CreateInt16(short value, out IntPtr result);
        void CreateUInt16(ushort value, out IntPtr result);
        void CreateInt32(int value, out IntPtr result);
        void CreateUInt32(uint value, out IntPtr result);
        void CreateInt64(long value, out IntPtr result);
        void CreateUInt64(ulong value, out IntPtr result);
        void CreateSingle(float value, out IntPtr result);
    }
}
