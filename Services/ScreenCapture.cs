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

namespace ArtaleAI.GameWindow
{
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    public class GraphicsCapturer : IDisposable
    {
        private GraphicsCaptureItem _item;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private SizeInt32 _lastSize;
        private SharpDX.Direct3D11.Device _device;

        public GraphicsCapturer(GraphicsCaptureItem item)
        {
            _item = item;
            _device = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);

            // 啟用多執行緒保護
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

        // 直接返回 Mat，跳過 Bitmap 轉換
        public Mat? TryGetNextMat()
        {
            try
            {

                using var frame = _framePool.TryGetNextFrame();

                if (frame == null)
                {
                    return null;
                }

                // 檢查尺寸變化
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
            catch (Exception ex)
            {
                return null;
            }
        }


        // 直接從 Direct3D11CaptureFrame 轉換到 Mat
        private Mat ConvertToMat(Direct3D11CaptureFrame frame)
        {
            Texture2D? sourceTexture = null;
            Texture2D? stagingTexture = null;

            try
            {
                sourceTexture = GetSharpDXTexture2D(frame.Surface);
                var desc = sourceTexture.Description;

                // 建立 staging texture 供 CPU 讀取
                var stagingDesc = new Texture2DDescription
                {
                    ArraySize = 1,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    Format = desc.Format,
                    Height = desc.Height,
                    Width = desc.Width,
                    MipLevels = 1,
                    OptionFlags = ResourceOptionFlags.None,
                    SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging
                };

                stagingTexture = new Texture2D(_device, stagingDesc);
                _device.ImmediateContext.CopyResource(sourceTexture, stagingTexture);

                var dataBox = _device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

                try
                {
                    int width = desc.Width;
                    int height = desc.Height;

                    // BGRA -> Mat (Direct3D 使用 BGRA 格式)
                    var matBGRA = new Mat(height, width, MatType.CV_8UC4);

                    unsafe
                    {
                        byte* matPtr = (byte*)matBGRA.DataPointer;
                        byte* sourcePtr = (byte*)dataBox.DataPointer;

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

                    // 🔥 關鍵修改：轉換 BGRA -> RGB (而不是 BGR)
                    var matRGB = new Mat();
                    Cv2.CvtColor(matBGRA, matRGB, ColorConversionCodes.BGRA2RGB);

                    matBGRA.Dispose();

                    return matRGB;
                }
                finally
                {
                    _device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
                }
            }
            finally
            {
                sourceTexture?.Dispose();
                stagingTexture?.Dispose();
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
            try
            {
                //_session?.Dispose();
                //_framePool?.Dispose();
                //_device?.Dispose();

                //_session = null;
                //_framePool = null;
                //_device = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GraphicsCapturer dispose error: {ex.Message}");
            }
        }
    }
}
