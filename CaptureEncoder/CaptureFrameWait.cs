﻿using System;
using System.Threading;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace CaptureEncoder
{
    public sealed class SurfaceWithInfo : IDisposable
    {
        public IDirect3DSurface Surface { get; internal set; }
        public TimeSpan SystemRelativeTime { get; internal set; }

        public void Dispose()
        {
            Surface?.Dispose();
            Surface = null;
        }
    }

    class MultithreadLock : IDisposable
    {
        public MultithreadLock(SharpDX.Direct3D11.Multithread multithread)
        {
            _multithread = multithread;
            _multithread?.Enter();
        }

        public void Dispose()
        {
            _multithread?.Leave();
            _multithread = null;
        }

        private SharpDX.Direct3D11.Multithread _multithread;
    }

    public sealed class CaptureFrameWait : IDisposable
    {
        public CaptureFrameWait(
            IDirect3DDevice device,
            GraphicsCaptureItem item,
            SizeInt32 size,
            bool includeCursor)
        {
            _device = device;
            _d3dDevice = Direct3D11Helpers.CreateSharpDXDevice(device);
            _multithread = _d3dDevice.QueryInterface<SharpDX.Direct3D11.Multithread>();
            _multithread.SetMultithreadProtected(true);
            _item = item;
            _frameEvent = new ManualResetEvent(false);
            _closedEvent = new ManualResetEvent(false);
            _events = new[] { _closedEvent, _frameEvent };

            InitializeComposeTexture(size);
            InitializeCapture(size, includeCursor);
        }

        private void InitializeCapture(SizeInt32 size, bool includeCursor)
        {
            _item.Closed += OnClosed;
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                size);
            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(_item);
            if (!includeCursor)
            {
                _session.IsCursorCaptureEnabled = includeCursor;
            }
            _session.StartCapture();
                    StartAudioCapture();
    }

    private void OnFrameArrived(Direct3D11CaptureFrame frame)
    {
        StopAudioCapture()
        CombineAudioVideo(capturedAudioData, frame);
    }
    private void StartAudioCapture()
    {

    }

    private void StopAudioCapture()
    {

    }

    private void CombineAudioVideo(byte[] audioData, Direct3D11CaptureFrame frame)
    {

    }
}

        }

        private void InitializeComposeTexture(SizeInt32 size)
        {
            var description = new SharpDX.Direct3D11.Texture2DDescription
            {
                Width = size.Width,
                Height = size.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SharpDX.DXGI.SampleDescription()
                {
                    Count = 1,
                    Quality = 0
                },
                Usage = SharpDX.Direct3D11.ResourceUsage.Default,
                BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.RenderTarget,
                CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
                OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None
            };
            _composeTexture = new SharpDX.Direct3D11.Texture2D(_d3dDevice, description);
            _composeRenderTargetView = new SharpDX.Direct3D11.RenderTargetView(_d3dDevice, _composeTexture);
        }

        private void SetResult(Direct3D11CaptureFrame frame)
        {
            _currentFrame = frame;
            _frameEvent.Set();
        }

        private void Stop()
        {
            _closedEvent.Set();
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            SetResult(sender.TryGetNextFrame());
        }

        private void OnClosed(GraphicsCaptureItem sender, object args)
        {
            Stop();
        }

        private void Cleanup()
        {
            _framePool?.Dispose();
            _session?.Dispose();
            if (_item != null)
            {
                _item.Closed -= OnClosed;
            }
            _item = null;
            _device = null;
            _d3dDevice = null;
            _composeTexture?.Dispose();
            _composeTexture = null;
            _composeRenderTargetView?.Dispose();
            _composeRenderTargetView = null;
            _currentFrame?.Dispose();
        }

        public SurfaceWithInfo WaitForNewFrame()
        {
            // Let's get a fresh one.
            _currentFrame?.Dispose();
            _frameEvent.Reset();

            var signaledEvent = _events[WaitHandle.WaitAny(_events)];
            if (signaledEvent == _closedEvent)
            {
                Cleanup();
                return null;
            }

            var result = new SurfaceWithInfo();
            result.SystemRelativeTime = _currentFrame.SystemRelativeTime;
            using (var multithreadLock = new MultithreadLock(_multithread))
            using (var sourceTexture = Direct3D11Helpers.CreateSharpDXTexture2D(_currentFrame.Surface))
            {
                _d3dDevice.ImmediateContext.ClearRenderTargetView(_composeRenderTargetView, new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 1));

                var width = Math.Clamp(_currentFrame.ContentSize.Width, 0, _currentFrame.Surface.Description.Width);
                var height = Math.Clamp(_currentFrame.ContentSize.Height, 0, _currentFrame.Surface.Description.Height);
                var region = new SharpDX.Direct3D11.ResourceRegion(0, 0, 0, width, height, 1);
                _d3dDevice.ImmediateContext.CopySubresourceRegion(sourceTexture, 0, region, _composeTexture, 0);

                var description = sourceTexture.Description;
                description.Usage = SharpDX.Direct3D11.ResourceUsage.Default;
                description.BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.RenderTarget;
                description.CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None;
                description.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;

                using (var copyTexture = new SharpDX.Direct3D11.Texture2D(_d3dDevice, description))
                {
                    _d3dDevice.ImmediateContext.CopyResource(_composeTexture, copyTexture);
                    result.Surface = Direct3D11Helpers.CreateDirect3DSurfaceFromSharpDXTexture(copyTexture);
                }
            }

            return result;
        }

        public void Dispose()
        {
            Stop();
            Cleanup();
        }

        private IDirect3DDevice _device;
        private SharpDX.Direct3D11.Device _d3dDevice;
        private SharpDX.Direct3D11.Multithread _multithread;
        private SharpDX.Direct3D11.Texture2D _composeTexture;
        private SharpDX.Direct3D11.RenderTargetView _composeRenderTargetView;

        private ManualResetEvent[] _events;
        private ManualResetEvent _frameEvent;
        private ManualResetEvent _closedEvent;
        private Direct3D11CaptureFrame _currentFrame;

        private GraphicsCaptureItem _item;
        private GraphicsCaptureSession _session;
        private Direct3D11CaptureFramePool _framePool;
    }
}
