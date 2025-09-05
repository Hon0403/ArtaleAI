using SharpDX;
using SharpDX.Direct3D11;
using System.Drawing.Imaging;
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
                3, // 增加緩衝區數量
                item.Size);

            _session = _framePool.CreateCaptureSession(item);
            _lastSize = item.Size;
            _session.StartCapture();
        }

        public Bitmap? TryGetNextFrame()
        {
            using var frame = _framePool.TryGetNextFrame();
            if (frame == null) return null;

            // 只在尺寸真的改變時才重建
            if (frame.ContentSize.Width != _lastSize.Width || frame.ContentSize.Height != _lastSize.Height)
            {
                _lastSize = frame.ContentSize;
                _framePool.Recreate(
                    CreateDirect3DDevice(_device.QueryInterface<SharpDX.DXGI.Device>()),
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _lastSize);
            }

            return ConvertToBitmap(frame);
        }

        // 簡化的位圖轉換方法
        private Bitmap ConvertToBitmap(Direct3D11CaptureFrame frame)
        {
            using var sourceTexture = GetSharpDXTexture2D(frame.Surface);
            var desc = sourceTexture.Description;
            desc.CpuAccessFlags = CpuAccessFlags.Read;
            desc.BindFlags = BindFlags.None;
            desc.Usage = ResourceUsage.Staging;
            desc.OptionFlags = ResourceOptionFlags.None;

            using var stagingTexture = new Texture2D(_device, desc);
            _device.ImmediateContext.CopyResource(sourceTexture, stagingTexture);

            var dataBox = _device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            var bitmap = new Bitmap(desc.Width, desc.Height, PixelFormat.Format32bppArgb);
            var boundsRect = new Rectangle(0, 0, desc.Width, desc.Height);
            var mapDest = bitmap.LockBits(boundsRect, ImageLockMode.WriteOnly, bitmap.PixelFormat);

            // 檢查是否可以一次複製全部
            if (dataBox.RowPitch == mapDest.Stride)
            {
                Utilities.CopyMemory(mapDest.Scan0, dataBox.DataPointer, desc.Height * dataBox.RowPitch);
            }
            else
            {
                // 只有在 RowPitch 和 Stride 不同時才逐行復製
                IntPtr sourcePtr = dataBox.DataPointer;
                IntPtr destPtr = mapDest.Scan0;
                for (int y = 0; y < desc.Height; y++)
                {
                    Utilities.CopyMemory(destPtr, sourcePtr, desc.Width * 4);
                    sourcePtr = IntPtr.Add(sourcePtr, dataBox.RowPitch);
                    destPtr = IntPtr.Add(destPtr, mapDest.Stride);
                }
            }

            bitmap.UnlockBits(mapDest);
            _device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
            return bitmap;
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
            _session?.Dispose();
            _framePool?.Dispose();
            _device?.Dispose();
        }
    }
}
