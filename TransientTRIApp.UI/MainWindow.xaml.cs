using TransientTRIApp.Core;
using TransientTRIApp.Common;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Models;
using TransientTRIApp.Common.Interfaces;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using TransientTRIApp.Core.Services;
using TransientTRIApp.Core.Hardware;
using TransientTRIApp.Core.Camera;
using System.Windows.Threading;

namespace TransientTRIApp.UI
{
    public partial class MainWindow : Window
    {
        private readonly ApplicationController _controller;

        public MainWindow()
        {
            InitializeComponent();

            var hardware = new HardwareService();
            var camera = new CameraService();

            _controller = new ApplicationController(hardware, camera);

            hardware.MeasurementUpdated += OnMeasurementUpdated;
            camera.FrameReady += OnFrameReady;
        }

        private void UI(Action action)
        {
            Dispatcher.BeginInvoke(action);
        }


        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            _controller.StartAll();
        }

        private void OnMeasurementUpdated(object sender, MeasurementUpdatedEventArgs e)
        {
            UI(() =>
            {
                VoltageText.Text = $"{e.MeasurementData.Voltage:F3} V";
                CurrentText.Text = $"{e.MeasurementData.Current:F3} A";
            });
        }

        private void OnFrameReady(object sender, CameraFrameEventArgs e)
        {
            UI(() =>
            {
                CameraImage.Source = ConvertBitmap(e.Bmp);
            });
        }


        private void OnSetpointChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Send setpoint to hardware service
            // _controller.Hardware.SetSetpoint(e.NewValue);
        }

        private void OnStartClicked(object sender, RoutedEventArgs e)
        {
            _controller.StartAll();
        }

        private void OnStopClicked(object sender, RoutedEventArgs e)
        {
            _controller.StopAll();
        }

        protected override void OnClosed(EventArgs e)
        {
            _controller.StopAll();
            base.OnClosed(e);
        }

        // --- Bitmap -> BitmapSource conversion ---
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private static BitmapSource ConvertBitmap(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
                bitmap.Dispose();
            }
        }
    }
}