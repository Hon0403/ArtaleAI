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
    /// <summary>
    /// Direct3D DXGI 介面存取（用於取得底層 Texture2D）
    /// </summary>
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    /// <summary>
    /// 圖形擷取器 - 使用 Windows.Graphics.Capture API 擷取視窗畫面
    /// 支援直接轉換為 OpenCV Mat 格式，避免中間轉換開銷
    /// </summary>
    public class GraphicsCapturer : IDisposable
    {
        private GraphicsCaptureItem _item;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private SizeInt32 _lastSize;
        private SharpDX.Direct3D11.Device _device;
        private volatile bool _isDisposed = false; // 添加線程安全標誌

        /// <summary>
        /// 初始化圖形擷取器
        /// 建立 Direct3D 裝置和擷取工作階段
        /// </summary>
        /// <param name="item">要擷取的視窗項目</param>
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

        /// <summary>
        /// 嘗試獲取下一幀畫面並轉換為 OpenCV Mat 格式
        /// 跳過 Bitmap 轉換步驟，直接從 Direct3D Texture 轉換為 Mat
        /// </summary>
        /// <returns>成功時返回 Mat（BGR 格式），失敗時返回 null</returns>
        public Mat? TryGetNextMat()
        {
            // 修復：檢查是否已釋放
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

        /// <summary>
        /// 將 Direct3D11CaptureFrame 直接轉換為 OpenCV Mat
        /// 使用記憶體拷貝方式提高效能，避免中間格式轉換
        /// </summary>
        /// <param name="frame">擷取的畫面幀</param>
        /// <returns>BGR 格式的 Mat 物件</returns>
        private Mat ConvertToMat(Direct3D11CaptureFrame frame)
        {
            // 修復：檢查是否已釋放
            if (_isDisposed || _device == null)
            {
                throw new ObjectDisposedException(nameof(GraphicsCapturer), "GraphicsCapturer has been disposed");
            }

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

                        // 修復：檢查指標是否有效
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

                    // 🔥 關鍵修改：轉換 BGRA -> RGB (而不是 BGR)
                    var matRGB = new Mat();
                    Cv2.CvtColor(matBGRA, matRGB, ColorConversionCodes.BGRA2RGB);

                    matBGRA.Dispose();

                    return matRGB;
                }
                finally
                {
                    // 修復：檢查 _device 是否仍然有效
                    if (!_isDisposed && _device != null && stagingTexture != null)
                    {
                        try
                        {
                            _device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
                        }
                        catch (Exception ex)
                        {
                            // 如果已經釋放，忽略錯誤
                            Debug.WriteLine($"UnmapSubresource 錯誤（可能已釋放）: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                sourceTexture?.Dispose();
                stagingTexture?.Dispose();
            }
        }


        /// <summary>
        /// Win32 API：從 DXGI Device 建立 Direct3D11 Device
        /// </summary>
        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
        private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        /// <summary>
        /// 從 SharpDX DXGI Device 建立 Windows Runtime Direct3D Device
        /// 用於建立 Direct3D11CaptureFramePool
        /// </summary>
        /// <param name="dxgiDevice">SharpDX DXGI 裝置</param>
        /// <returns>Windows Runtime Direct3D 裝置</returns>
        private static IDirect3DDevice CreateDirect3DDevice(SharpDX.DXGI.Device dxgiDevice)
        {
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var d3dDevicePtr);
            var d3dDevice = MarshalInspectable<IDirect3DDevice>.FromAbi(d3dDevicePtr);
            Marshal.Release(d3dDevicePtr);
            return d3dDevice;
        }

        /// <summary>
        /// 從 Windows Runtime Direct3D Surface 取得 SharpDX Texture2D
        /// 使用 COM 介面轉換方式
        /// </summary>
        /// <param name="surface">Windows Runtime Direct3D Surface</param>
        /// <returns>SharpDX Texture2D 物件</returns>
        private static Texture2D GetSharpDXTexture2D(IDirect3DSurface surface)
        {
            var access = surface.As<IDirect3DDxgiInterfaceAccess>();
            var IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            var texturePtr = access.GetInterface(ref IID_ID3D11Texture2D);
            return new Texture2D(texturePtr);
        }

        /// <summary>
        /// 釋放圖形擷取器使用的所有資源
        /// 包括 Direct3D 裝置、擷取工作階段和幀池
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true; // 設置標誌，防止後續使用

            try
            {
                // 修復：正確釋放所有 Direct3D 資源（按順序）
                // 先停止擷取，再釋放資源
                _session?.Dispose();
                _framePool?.Dispose();
                
                // 最後釋放設備（確保所有操作完成）
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
