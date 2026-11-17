﻿using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using Uia.DriverServer.Domain;
using Uia.DriverServer.Models;

namespace OcrInspector
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// <remarks>
    /// The window shows an image together with the OCR words recognized on it and lets the user copy the OCR
    /// locator of any word. The image comes from a file, from a local screen capture, or from a running UiaDriver
    /// Server. Local sources run the driver's own recognition code (the files under the Driver folder), and the
    /// driver source shows the driver's own capture, so the displayed locators always match what the driver resolves.
    /// </remarks>
    public partial class MainWindow : Window
    {
        #region *** Constants    ***

        // Gives the hidden window time to leave the screen before a capture, so it never appears in the image.
        private const int HideDelayMilliseconds = 500;

        // Declares the session a driver snapshot runs under: a desktop session that starts no application.
        private const string DesktopSessionRequest =
            "{\"capabilities\":{\"alwaysMatch\":{\"browserName\":\"Uia\",\"uia:options\":{}}}}";

        // Shares one HTTP client for every driver request of the application lifetime, as the runtime recommends.
        private static readonly HttpClient DriverClient = new()
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        // Reads the driver's camel-cased JSON into the mirrored driver models.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        #endregion

        #region *** Constructors ***

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            // Initialize the window components and event handlers
            // when the window is created and loaded in the application
            InitializeComponent();

            // Set the status text in the UI to indicate that the application is ready
            MainImage.Loaded += (s, e) => Dispatcher.Invoke(() => StatusTextBlock.Text = "Ready");
        }

        #endregion

        #region *** Methods      ***

        // Handles the From Driver button: asks a running UiaDriver Server for its screenshot and the OCR words it
        // recognized, then displays both. The window hides during the request so it is absent from the driver's
        // capture, and is always shown again. Any failure is reported in the status bar instead of being thrown.
        // The method keeps the event-handler name that the XAML markup binds to, so it carries no Async suffix.
        private async void BtnFromDriver_Click(object sender, RoutedEventArgs e)
        {
            // Read the address on the UI thread before the window hides and the asynchronous work starts.
            var driverUrl = TxtDriverUrl.Text.Trim().TrimEnd('/');
            StatusTextBlock.Text = $"Requesting the OCR snapshot from {driverUrl}...";

            // Hide the main window so the driver captures the screen behind it.
            Hide();

            try
            {
                // Wait for the window to leave the screen, then let the driver capture and recognize it.
                // Both awaits resume on the UI thread on purpose, because the result is applied to UI elements.
                await Task.Delay(HideDelayMilliseconds);
                var snapshot = await GetDriverSnapshotAsync(driverUrl);

                // Display the driver's own image and words; nothing is recognized locally for this source.
                var imageData = Convert.FromBase64String(snapshot.Screenshot);
                ShowSnapshot(imageData, words: snapshot.Words, source: $"driver {driverUrl}");
            }
            catch (Exception exception)
            {
                // Keep the previous image and explain the failure where the user looks for feedback.
                StatusTextBlock.Text = $"Driver snapshot failed: {exception.Message}";
            }
            finally
            {
                // Restore the window whether the request succeeded or failed.
                Show();
            }
        }

        // Handles the Load Image button: lets the user pick an image file, recognizes its words with the driver's
        // recognition code, and displays the image with the words. A canceled dialog leaves the window unchanged.
        private void BtnLoadImage_Click(object sender, RoutedEventArgs e)
        {
            // Open the file dialog box to allow the user to select an image file to process
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp"
            };

            // Return without changes when the user canceled the dialog box
            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                // Load the file through the same bitmap type the driver captures, then release the file lock.
                using var image = new Bitmap(openFileDialog.FileName);
                ShowBitmap(image, source: Path.GetFileName(openFileDialog.FileName));
            }
            catch (Exception exception)
            {
                // Keep the previous image and explain the failure where the user looks for feedback.
                StatusTextBlock.Text = $"Image recognition failed: {exception.Message}";
            }
        }

        // Handles the Take Screenshot button: captures the primary display exactly as the driver does, recognizes
        // its words with the driver's recognition code, and displays the capture with the words.
        private void BtnTakeScreenshot_Click(object sender, RoutedEventArgs e)
        {
            // Hide the main window to avoid it appearing in the screenshot.
            Hide();

            try
            {
                // Wait for a short period to ensure the window is hidden before taking the screenshot.
                System.Threading.Thread.Sleep(HideDelayMilliseconds);

                // Capture and recognize while the window is hidden; the capture is released after display.
                using var image = NewScreenBitmap();
                ShowBitmap(image, source: "local screenshot");
            }
            catch (Exception exception)
            {
                // Keep the previous image and explain the failure where the user looks for feedback.
                StatusTextBlock.Text = $"Screenshot recognition failed: {exception.Message}";
            }
            finally
            {
                // Show the main window again whether the capture succeeded or failed.
                Show();
            }
        }

        // Adds a clickable rectangle to the MainCanvas at the location of the given word.
        // The rectangle shows the word's locator and confidence in a tooltip and copies the locator on right-click.
        private void AddClickablePoint(OcrWordModel word)
        {
            // Create a tooltip that carries the exact locator the driver resolves to this word.
            var tooltip = new ToolTip
            {
                Content = $"OCR Locator: {word.Locator}, Accuracy: {Math.Round(word.Confidence, 2)}%",
                Focusable = true,
                StaysOpen = true,
                Tag = word.Locator,
                Template = (ControlTemplate)FindResource("ToolTipTemplate")
            };

            // Create a rectangle to represent the clickable point on the image
            System.Windows.Shapes.Rectangle rectangle = new()
            {
                Fill = System.Windows.Media.Brushes.Transparent,
                Height = Math.Max(1, word.Rectangle.Bottom - word.Rectangle.Top),
                Stroke = System.Windows.Media.Brushes.Blue,
                StrokeThickness = 1,
                Tag = word,
                ToolTip = tooltip,
                Width = Math.Max(1, word.Rectangle.Right - word.Rectangle.Left)
            };

            // Copy the complete locator, including its occurrence index, to the clipboard on right-click
            rectangle.MouseRightButtonUp += (s, e) =>
            {
                Clipboard.SetText(word.Locator);
                StatusTextBlock.Text = $"The OCR Locator {word.Locator} has been copied to the clipboard.";
            };

            // Set the position of the rectangle on the canvas to match the position of the word on the image
            Canvas.SetLeft(rectangle, word.Rectangle.Left);
            Canvas.SetTop(rectangle, word.Rectangle.Top);

            // Add the rectangle to the MainCanvas to display the clickable point on the image
            MainCanvas.Children.Add(rectangle);
        }

        // Recognizes the words on a bitmap with the driver's recognition code and displays the bitmap with them.
        // The caller keeps ownership of the bitmap; the display works on an encoded copy.
        private void ShowBitmap(Bitmap image, string source)
        {
            // Run the mirrored driver pipeline so the words and locators equal the driver's for these pixels.
            var words = OcrEngine.ResolveWords(image);

            // Encode the pixels as PNG, the lossless format the driver publishes its captures in.
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);

            ShowSnapshot(imageData: stream.ToArray(), words, source);
        }

        // Replaces the displayed image and word rectangles with a new snapshot and reports it in the status bar.
        // The image is decoded into memory, so the supplied buffer is not referenced after the method returns.
        private void ShowSnapshot(byte[] imageData, List<OcrWordModel> words, string source)
        {
            // Decode the image completely and freeze it so no stream stays open behind the displayed picture.
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(imageData))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();

            // Remove every rectangle of the previous snapshot; the image stays as the first canvas child.
            MainCanvas.Children.RemoveRange(index: 1, count: MainCanvas.Children.Count - 1);

            // Size the image and the canvas in image pixels so word rectangles land on their text.
            MainImage.Source = bitmap;
            MainImage.Width = bitmap.PixelWidth;
            MainImage.Height = bitmap.PixelHeight;
            MainCanvas.Width = bitmap.PixelWidth;
            MainCanvas.Height = bitmap.PixelHeight;

            // Add clickable points to the MainCanvas at the locations of the recognized words
            foreach (var word in words)
            {
                AddClickablePoint(word);
            }

            // Report the result and remind the user how to take a locator from the picture.
            var status = $"{words.Count} words recognized from {source}. Right-click a word to copy its OCR Locator.";
            StatusTextBlock.Text = status;
        }

        // Requests one OCR snapshot from a running UiaDriver Server through a temporary desktop session.
        // The session starts no application and is removed again even when the snapshot request fails.
        // Failures surface as exceptions with a message suited for the status bar.
        private static async Task<OcrSnapshotModel> GetDriverSnapshotAsync(string driverUrl)
        {
            // Open a desktop session; the driver scopes every route, including the OCR route, to a session.
            using var sessionContent = new StringContent(DesktopSessionRequest, Encoding.UTF8, "application/json");
            using var sessionResponse = await DriverClient
                .PostAsync($"{driverUrl}/session", sessionContent)
                .ConfigureAwait(false);
            sessionResponse.EnsureSuccessStatusCode();

            var sessionJson = await sessionResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var sessionDocument = JsonDocument.Parse(sessionJson);
            var session = sessionDocument.RootElement
                .GetProperty("value")
                .GetProperty("sessionId")
                .GetString();

            try
            {
                // Ask the driver to capture its screen and recognize the words in one step.
                using var response = await DriverClient
                    .GetAsync($"{driverUrl}/session/{session}/ocr")
                    .ConfigureAwait(false);

                // Explain a missing route, which means the server is the standard build without the OCR engine.
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var message = "the server has no OCR route. Use the -emgu build of the UiaDriver Server.";
                    throw new InvalidOperationException(message);
                }

                response.EnsureSuccessStatusCode();

                // Read the snapshot from the standard WebDriver envelope into the mirrored driver model.
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var snapshot = document.RootElement.GetProperty("value").Deserialize<OcrSnapshotModel>(JsonOptions);
                snapshot.Words ??= [];

                return snapshot;
            }
            finally
            {
                // Remove the temporary session so inspecting never leaves sessions behind on the server.
                using var deleteResponse = await DriverClient
                    .DeleteAsync($"{driverUrl}/session/{session}")
                    .ConfigureAwait(false);
            }
        }

        // Captures the primary display into a new bitmap, using the same steps as the driver's screen capture
        // so both tools recognize identical pixels. The caller owns and disposes the returned bitmap.
        private static Bitmap NewScreenBitmap()
        {
            // Get the current display settings.
            var devMode = ExternalMethods.GetDisplaySettings();

            // Create a bitmap with the dimensions of the primary display.
            var image = new Bitmap(
                width: devMode.dmPelsWidth,
                height: devMode.dmPelsHeight,
                format: PixelFormat.Format32bppArgb);

            // Capture the screen and copy it to the bitmap; the drawing surface is released after the copy.
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.CopyFromScreen(
                    sourceX: devMode.dmPositionX,
                    sourceY: devMode.dmPositionY,
                    destinationX: 0,
                    destinationY: 0,
                    blockRegionSize: image.Size);
            }

            // Set the resolution of the bitmap to 300 DPI.
            image.SetResolution(300, 300);

            return image;
        }

        #endregion
    }

    /// <summary>
    /// Provides methods and structures for working with display settings.
    /// </summary>
    public static class ExternalMethods
    {
        // Enumerates the display settings for the specified device.
        [DllImport("user32.dll")]
        [SuppressMessage(
            category: "Interoperability",
            checkId: "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
            Justification = "Using DllImport for compatibility with existing P/Invoke patterns.")]
        [SuppressMessage(
            category: "Globalization",
            checkId: "CA2101:Specify marshaling for P/Invoke string arguments",
            Justification = "Marshaling not specified for string arguments as this is consistent with existing legacy code which has been tested and is known to work correctly.")]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

        /// <summary>
        /// Retrieves the current display settings.
        /// </summary>
        /// <returns>A <see cref="DevMode"/> structure containing the current display settings.</returns>
        public static DevMode GetDisplaySettings()
        {
            // Initialize a DevMode structure with default values.
            DevMode devMode = default;

            // Set the size of the DevMode structure.
            devMode.dmSize = (short)Marshal.SizeOf(devMode);

            // Retrieve the current display settings.
            EnumDisplaySettings(null, -1, ref devMode);

            // Return the DevMode structure containing the display settings.
            return devMode;
        }

        /// <summary>
        /// Defines the display settings structure used by the <see cref="EnumDisplaySettings"/> function.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
