using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ArtaleAI.Services
{
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    /// <summary><see cref="GraphicsCaptureItem"/> → Direct3D 紋理 → BGR <see cref="Mat"/>；快取 staging 紋理減少每幀配置。</summary>
    public class GraphicsCapturer : IDisposable
    {
        private GraphicsCaptureItem _item;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private SizeInt32 _lastSize;
        private SharpDX.Direct3D11.Device? _device;
        private volatile bool _isDisposed = false;

        private Texture2D? _cachedStagingTexture;
        private int _cachedWidth = 0;
        private int _cachedHeight = 0;

        public GraphicsCapturer(GraphicsCaptureItem item)
        {
            _item = item;
            _device = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);

            var multithread = _device.QueryInterface<Multithread>();
            multithread.SetMultithreadProtected(true);

            var dxgiDevice = _device.QueryInterface<SharpDX.DXGI.Device>();
            var d3dDevice = CreateDirect3DDevice(dxgiDevice);

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                d3dDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                3,
                item.Size);

            _session = _framePool.CreateCaptureSession(item);
            _lastSize = item.Size;
            _session.StartCapture();
        }

        public Mat? TryGetNextMat()
        {
            if (_isDisposed || _device == null || _framePool == null)
            {
                return null;
            }

            try
            {
                using var frame = _framePool.TryGetNextFrame();

                if (frame == null)
                {
                    return null;
                }

                if (frame.ContentSize.Width != _lastSize.Width || frame.ContentSize.Height != _lastSize.Height)
                {
                    _lastSize = frame.ContentSize;
                    _framePool.Recreate(
                        CreateDirect3DDevice(_device.QueryInterface<SharpDX.DXGI.Device>()),
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        _lastSize);
                }

                var result = ConvertToMat(frame);

                return result;
            }
            catch
            {
                return null;
            }
        }

        private Mat ConvertToMat(Direct3D11CaptureFrame frame)
        {
            if (_isDisposed || _device == null)
            {
                throw new ObjectDisposedException(nameof(GraphicsCapturer), "GraphicsCapturer has been disposed");
            }

            Texture2D? sourceTexture = null;

            try
            {
                sourceTexture = GetSharpDXTexture2D(frame.Surface);
                var desc = sourceTexture.Description;
                int width = desc.Width;
                int height = desc.Height;

                if (_cachedStagingTexture == null ||
                    _cachedWidth != width ||
                    _cachedHeight != height)
                {
                    _cachedStagingTexture?.Dispose();

                    var stagingDesc = new Texture2DDescription
                    {
                        ArraySize = 1,
                        BindFlags = BindFlags.None,
                        CpuAccessFlags = CpuAccessFlags.Read,
                        Format = desc.Format,
                        Height = height,
                        Width = width,
                        MipLevels = 1,
                        OptionFlags = ResourceOptionFlags.None,
                        SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging
                    };

                    _cachedStagingTexture = new Texture2D(_device, stagingDesc);
                    _cachedWidth = width;
                    _cachedHeight = height;
                    
                    Debug.WriteLine($"Staging texture recreated: {width}x{height}");
                }

                _device.ImmediateContext.CopyResource(sourceTexture, _cachedStagingTexture);

                var dataBox = _device.ImmediateContext.MapSubresource(_cachedStagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

                try
                {
                    var matBGRA = new Mat(height, width, MatType.CV_8UC4);

                    unsafe
                    {
                        byte* matPtr = (byte*)matBGRA.DataPointer;
                        byte* sourcePtr = (byte*)dataBox.DataPointer;

                        if (matPtr == null || sourcePtr == null)
                        {
                            throw new InvalidOperationException("無效的記憶體指標");
                        }

                        if (dataBox.RowPitch == matBGRA.Step())
                        {
                            System.Buffer.MemoryCopy(sourcePtr, matPtr, matBGRA.Total() * matBGRA.ElemSize(), height * dataBox.RowPitch);
                        }
                        else
                        {
                            for (int y = 0; y < height; y++)
                            {
                                System.Buffer.MemoryCopy(
                                    sourcePtr + y * dataBox.RowPitch,
                                    matPtr + y * matBGRA.Step(),
                                    width * 4,
                                    width * 4);
                            }
                        }
                    }

                    var matBGR = new Mat();
                    Cv2.CvtColor(matBGRA, matBGR, ColorConversionCodes.BGRA2BGR);

                    matBGRA.Dispose();

                    return matBGR;
                }
                finally
                {
                    if (!_isDisposed && _device != null && _cachedStagingTexture != null)
                    {
                        try
                        {
                            _device.ImmediateContext.UnmapSubresource(_cachedStagingTexture, 0);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"UnmapSubresource: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                sourceTexture?.Dispose();
            }
        }


        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
        private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        private static IDirect3DDevice CreateDirect3DDevice(SharpDX.DXGI.Device dxgiDevice)
        {
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var d3dDevicePtr);
            var d3dDevice = MarshalInspectable<IDirect3DDevice>.FromAbi(d3dDevicePtr);
            Marshal.Release(d3dDevicePtr);
            return d3dDevice;
        }

        private static Texture2D GetSharpDXTexture2D(IDirect3DSurface surface)
        {
            var access = surface.As<IDirect3DDxgiInterfaceAccess>();
            var IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            var texturePtr = access.GetInterface(ref IID_ID3D11Texture2D);
            return new Texture2D(texturePtr);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            try
            {
                _session?.Dispose();
                _framePool?.Dispose();

                _cachedStagingTexture?.Dispose();
                _cachedStagingTexture = null;
                _cachedWidth = 0;
                _cachedHeight = 0;

                _device?.Dispose();

                _session = null;
                _framePool = null;
                _device = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GraphicsCapturer dispose error: {ex.Message}");
            }
        }
    }
}
