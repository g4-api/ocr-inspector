using Emgu.CV;
using Emgu.CV.OCR;
using Emgu.CV.Structure;

using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace OcrInspector
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
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

        // Event handler for the Load Image button click event to open the file dialog box to allow the user
        // to select an image file to process using Tesseract OCR and display in the window UI elements
        private void BtnLoadImage_Click(object sender, RoutedEventArgs e)
        {
            // Open the file dialog box to allow the user to select an image file to process
            // using Tesseract OCR and display in the window UI elements
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp"
            };

            // Check if the user selected a file or canceled the dialog box and return
            // if the user canceled the dialog box
            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            // Load the image from the file path
            var uriSource = new Uri(openFileDialog.FileName);

            // Process the image using Tesseract OCR and get the recognized words along with the
            // processed image with bounding boxes around the recognized words drawn on it
            var resolvedImage = ResolveWords(openFileDialog.FileName);

            // Clear the existing clickable points on the MainCanvas to display the new image and recognized words
            MainCanvas.Children.RemoveRange(1, MainCanvas.Children.Count - 2);

            // Add clickable points to the MainCanvas at the locations of the recognized words
            foreach (var word in resolvedImage.Words)
            {
                AddClickablePoint(word);
            }

            // Display the image and the recognized words in the UI elements of the window
            MainImage.Source = resolvedImage.ImageSource;

            // Set the image size to its original size to display the image correctly in
            // the window without distortion or cropping issues
            var bitmap = new BitmapImage(uriSource);
            MainImage.Width = bitmap.PixelWidth;
            MainImage.Height = bitmap.PixelHeight;

            // Set the width of the MainCanvas to match the width of the MainImage
            MainCanvas.Width = bitmap.PixelWidth;

            // Set the height of the MainCanvas to match the height of the MainImage
            MainCanvas.Height = bitmap.PixelHeight;
        }

        // Processes the image at the given path using Tesseract OCR, converts it to grayscale, recognizes the text,
        // draws bounding boxes around the recognized words, and returns the processed image along with the list of recognized words.
        private static (ImageSource ImageSource, List<Tesseract.Word> Words) ResolveWords(string imagePath)
        {
            // Load the image
            var image = new Image<Bgr, byte>(imagePath);

            // Initialize Tesseract OCR
            var ocr = new Tesseract("TrainData/", "eng", OcrEngineMode.TesseractOnly);

            // Convert to grayscale for OCR processing (Tesseract requires a grayscale image)
            var grayImage = image.Convert<Gray, byte>();

            // Perform OCR on the grayscale image and get the recognized words
            ocr.SetImage(grayImage);
            ocr.Recognize();

            // Get the words from the OCR result
            var words = ocr.GetWords().ToList();

            // Save or display the image
            return (image.ToBitmapSource(), words);
        }

        // Adds a clickable point to the MainCanvas at the location of the given word.
        // The point displays a tooltip with the word text and accuracy.
        private void AddClickablePoint(Tesseract.Word word)
        {
            // Get the position of the word on the image
            var position = word.Region.Location;

            // Create a tooltip for the word with the text and accuracy information and set the tooltip
            // template to a custom template defined in XAML resources (ToolTipTemplate) to style the tooltip
            var tooltip = new ToolTip
            {
                Content = $"OCR Locator: {word.Text}, Accuracy: {Math.Round(word.Confident, 2)}%",
                Focusable = true,
                StaysOpen = true,
                Tag = word.Text,
                Template = (ControlTemplate)FindResource("ToolTipTemplate")
            };

            // Create a rectangle to represent the clickable point on the image
            Rectangle rectangle = new()
            {
                Fill = Brushes.Transparent,
                Height = word.Region.Height,
                Stroke = Brushes.Blue,
                StrokeThickness = 1,
                Tag = word.Region.Location,
                ToolTip = tooltip,
                Width = word.Region.Width
            };

            // Add event handlers for mouse events to show the tooltip on mouse hover
            // and copy the word text to the clipboard on right-click
            rectangle.MouseRightButtonUp += (s, e) =>
            {
                // Copy the word text to the clipboard
                Clipboard.SetText(word.Text);

                // Update the status text in the UI to indicate that the word text has been copied to the clipboard
                Dispatcher.Invoke(() => StatusTextBlock.Text = $"The OCR Locator value '{word.Text}' has been successfully copied to the clipboard.");
            };

            // Set the position of the rectangle on the canvas to match the position of the word on the image
            Canvas.SetLeft(rectangle, position.X);
            Canvas.SetTop(rectangle, position.Y);

            // Add the rectangle to the MainCanvas to display the clickable point on the image
            MainCanvas.Children.Add(rectangle);
        }
    }
}
