using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using IrrKlang;
using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using Echo_Sense.models;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrackBar;

namespace Echo_Sense
{
    public partial class Form1 : Form
    {
        // REGION: YOLO
        private KinectSensor sensor;
        private DepthImagePixel[] depthMap;
        private readonly ISoundEngine soundEngine = new ISoundEngine();
        private ISoundSource music;
        private readonly List<Blob> existingBlobs = new List<Blob>();
        private readonly List<Blob> redExistingBlobs = new List<Blob>();
        readonly List<BlobCoordinates> blobsParams = new List<BlobCoordinates>();
        private System.Timers.Timer soundTimer;
        private DateTime nextSoundTime = DateTime.MinValue;
        public static object dataLock = new object(); // Synchronization lock for safety
        private readonly object soundLock = new object();

        public Form1()
        {
            InitializeComponent();
            InitializeKinect();
            InitializeSoundTimer();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Console.WriteLine("STARTED");
            soundEngine.SoundVolume = 0.5f;
            music = soundEngine.GetSoundSource("C:\\Users\\Nytri\\source\\repos\\echo-sense\\sound2-very-loud.wav", true);
            soundEngine.SetListenerPosition(0, 0, 0, 0, 0, 1);
            if (music == null)
            {
                Console.WriteLine("Error playing 3D sound.");
                return;
            }
        }

        private void InitializeSoundTimer()
        {
            soundTimer = new System.Timers.Timer(50); // Check every 50ms
            soundTimer.Elapsed += OnSoundTimerElapsed;
            soundTimer.Start();
        }

        /// <summary>
        /// Called on every timer tick, checks if it's time to play a sound based on the closest blob's distance.
        /// If it's time, plays the sound and updates the next time to play a sound.
        /// </summary>
        private void OnSoundTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (soundLock)
            {
                if (blobsParams.Count > 0 && DateTime.Now >= nextSoundTime)
                {
                    var closestBlob = blobsParams.OrderBy(b => b.Z).First();
                    int soundTiming = CalculateSoundTiming(closestBlob.Z);

                    PlaySoundForBlob(closestBlob);
                    nextSoundTime = DateTime.Now.AddMilliseconds(soundTiming);
                }
            }
        }

        /// <summary>
        /// Plays the sound for the given blob, adjusting the volume and pitch based on the blob's position.
        /// </summary>
        /// <param name="blob">The blob position to play the sound for.</param>
        private void PlaySoundForBlob(BlobCoordinates blob)
        {
            // Adjust sound parameters based on blob position
            float volume = blob.Z / 10f; // Louder when closer
            volume = Math.Max(0.1f, Math.Min(1.0f, volume)); // Clamp between 0.1 and 1.0

            float pitch = 2.0f - (blob.Z / 10f); // Higher pitch when closer

            // Use Play2D with volume and pitch parameters
            soundEngine.Play3D(music, blob.X, blob.Y, blob.Z, false, false, false);
            //soundEngine.SoundVolume = 1.0f; // # FOR TESTING
            soundEngine.SoundVolume = volume;
            //soundEngine.Play2D("C:\\Users\\Nytri\\source\\repos\\echo-sense\\sound2-very-loud.wav");
        }

        /// <summary>
        /// Calculates the sound timing in milliseconds based on the given depth value.
        /// The timing is calculated such that the sound is played more frequently when the object is closer,
        /// and less frequently when the object is farther away.
        /// </summary>
        /// <param name="depth">The depth value to calculate the sound timing for.</param>
        /// <returns>The sound timing in milliseconds.</returns>
        private int CalculateSoundTiming(float depth)
        {
            float minDepth = 0.1f; // Minimum depth (closest)
            float maxDepth = 4.0f; // Maximum depth (farthest)
            int minSoundTiming = 100; // Minimum sound timing (closest) in milliseconds
            int maxSoundTiming = 1000; // Maximum sound timing (farthest) in milliseconds

            // Ensure depth is within the defined range
            depth = Math.Max(minDepth, Math.Min(maxDepth, depth));

            // Calculate soundTiming based on depth (inverted)
            int soundTiming = (int)((maxDepth - depth) / (maxDepth - minDepth) * (maxSoundTiming - minSoundTiming) + minSoundTiming);

            return soundTiming;
        }

        /// <summary>
        /// Handles the DepthImageFrameReady event from the sensor by processing the depth frame.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame != null)
                {
                    ProcessDepthFrame(depthFrame);
                }
            }
        }

        /// <summary>
        /// Processes a depth frame from the sensor, extracting color data and then detecting red blobs in the image.
        /// The detected blobs are then processed into a list of <see cref="BlobCoordinates"/> which are used to control the 3D sound.
        /// </summary>
        /// <param name="depthFrame">The depth frame from the sensor.</param>
        private void ProcessDepthFrame(DepthImageFrame depthFrame)
        {
            depthFrame.CopyDepthImagePixelDataTo(depthMap);

            Mat bitmapSource = ColorizeAndFlipDepthImage(depthFrame);
            Mat resizedBitmap = new Mat();
            Mat hlsFrame = ConvertToHLS(bitmapSource);

            // Resize
            CvInvoke.Resize(bitmapSource, resizedBitmap, new Size(640, 640));

            // Display the processed frame in the picture box
            frameBox.Image = bitmapSource.ToBitmap();

            List<Blob> redBlobCenters = ProcessColorMasks(hlsFrame, bitmapSource);

            lock (soundLock)
            {
                blobsParams.Clear();
                foreach (var blob in redBlobCenters)
                {
                    int centerX = (int)blob.CenterX;
                    int centerY = (int)blob.CenterY;

                    int scaledCenterX = (int)ScaleBetween(centerX, -67, 67, 0, 640);
                    int scaledCenterY = (int)ScaleBetween(centerY, 50, -50, 0, 480);

                    int length = (int)Math.Sqrt(scaledCenterX * scaledCenterX + scaledCenterY * scaledCenterY + 2 * 2);

                    if (length != 0)
                    {
                        float normalizedX = (float)scaledCenterX / length;
                        float normalizedY = (float)scaledCenterY / length;

                        blobsParams.Add(new BlobCoordinates { X = normalizedX * 10, Y = normalizedY * 5, Z = (10f / length) * 10 });
                    }
                }
            }

            hlsFrame.Dispose();
            bitmapSource.Dispose();
        }

        private Mat ColorizeAndFlipDepthImage(DepthImageFrame depthFrame)
        {
            Mat bitmapSource = ColorizeDepthImage(depthFrame).ToMat();
            CvInvoke.Flip(bitmapSource, bitmapSource, FlipType.Horizontal);
            return bitmapSource;
        }

        private Mat ConvertToHLS(Mat bitmapSource)
        {
            Mat hlsFrame = new Mat();
            CvInvoke.CvtColor(bitmapSource, hlsFrame, ColorConversion.Bgr2Hls);
            return hlsFrame;
        }

        private List<Blob> ProcessColorMasks(Mat hlsFrame, Mat bitmapSource)
        {


            Mat redBinaryMask = CreateColorMask(hlsFrame, new MCvScalar(0, 100, 100), new MCvScalar(10, 255, 255));
            Mat blueBinaryMask = CreateColorMask(hlsFrame, new MCvScalar(100, 100, 100), new MCvScalar(120, 255, 255));

            List<Blob> redBlobCenters = ProcessRedBlobs(redBinaryMask, bitmapSource);
            ProcessBlueBlobs(blueBinaryMask);

            DisplayResults(redBinaryMask, bitmapSource);

            DisposeColorMasks(redBinaryMask, blueBinaryMask);

            return redBlobCenters;
        }

        private Mat CreateColorMask(Mat hlsFrame, MCvScalar lowerBound, MCvScalar upperBound)
        {
            Mat binaryMask = new Mat();
            CvInvoke.InRange(hlsFrame, new ScalarArray(lowerBound), new ScalarArray(upperBound), binaryMask);
            return binaryMask;
        }

        private List<Blob> ProcessRedBlobs(Mat redBinaryMask, Mat bitmapSource)
        {
            List<Blob> redBlobCenters = GetBlobCenters(redBinaryMask.ToImage<Gray, byte>(), 5000, redExistingBlobs);

            foreach (var blobCenter in redBlobCenters)
            {
                redExistingBlobs.Add(new Blob(blobCenter.ID, blobCenter.CenterX, blobCenter.CenterY, blobCenter.Area));
            }

            ProcessBlobCoordinates(redBlobCenters, bitmapSource);
            return redBlobCenters;
        }

        private void ProcessBlueBlobs(Mat blueBinaryMask)
        {
            List<Blob> blueBlobCenters = GetBlobCenters(blueBinaryMask.ToImage<Gray, byte>(), 9000, existingBlobs);
            // Additional processing for blue blobs can be added here if needed
        }

        private void ProcessBlobCoordinates(List<Blob> blobCenters, Mat bitmapSource)
        {
            blobsParams.Clear();

            foreach (var blob in blobCenters)
            {
                int centerX = (int)blob.CenterX;
                int centerY = (int)blob.CenterY;

                int scaledCenterX = (int)ScaleBetween(centerX, -67, 67, 0, 640);
                int scaledCenterY = (int)ScaleBetween(centerY, 50, -50, 0, 480);

                int length = (int)Math.Sqrt(scaledCenterX * scaledCenterX + scaledCenterY * scaledCenterY + 2 * 2);

                if (length != 0)
                {
                    float normalizedX = (float)scaledCenterX / length;
                    float normalizedY = (float)scaledCenterY / length;

                    blobsParams.Add(new BlobCoordinates { X = normalizedX * 10, Y = normalizedY * 5, Z = (10f / length) * 10 });
                }

                CvInvoke.PutText(bitmapSource, $"{scaledCenterX}, {scaledCenterY}, {length}", new Point(centerX, centerY), FontFace.HersheyComplex, 1, new MCvScalar(255, 255, 255, 255), 2);
            }
        }

        private void DisplayResults(Mat redBinaryMask, Mat bitmapSource)
        {
            pictureBox2.Image = redBinaryMask.ToBitmap();
            frameBox.Image = bitmapSource.ToBitmap();
        }

        private void DisposeColorMasks(params Mat[] masks)
        {
            foreach (var mask in masks)
            {
                mask.Dispose();
            }
        }
        // Define blobCenters as a class-level variable
        float ScaleBetween(float unscaledNum, float minAllowed, float maxAllowed, float min, float max)
        {
            return (maxAllowed - minAllowed) * (unscaledNum - min) / (max - min) + minAllowed;
        }
        /// <summary>
        /// Finds the centers of the blobs in the given binary image. If the area of a contour is larger than the given minimum area, it is considered a blob and its center is added to the list of blob centers. If the blob is already in the list of existing blobs, it is updated with the new position and area; otherwise, a new blob is created and added to the list.
        /// </summary>
        /// <param name="binaryImage">The binary image to find the blob centers in</param>
        /// <param name="minContourArea">The minimum area of a contour to be considered a blob</param>
        /// <param name="existingBlobs">The list of existing blobs to update or add to</param>
        /// <returns>The list of blob centers found in the given binary image</returns>


        private List<Blob> GetBlobCenters(Image<Gray, byte> binaryImage, double minContourArea, List<Blob> existingBlobs)
        {
            List<Blob> blobCenters = new List<Blob>();

            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            CvInvoke.FindContours(binaryImage, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

            for (int i = 0; i < contours.Size; i++)
            {
                using (VectorOfPoint contour = contours[i])
                {
                    Moments moments = CvInvoke.Moments(contour);
                    float centerX = (float)(moments.M10 / moments.M00);
                    float centerY = (float)(moments.M01 / moments.M00);

                    double contourArea = CvInvoke.ContourArea(contour);
                    if (contourArea > minContourArea)
                    {
                        (Blob existingBlob, double distance) = FindClosestBlob(centerX, centerY, existingBlobs);
                        //System.Console.WriteLine("Blob found: " + existingBlob);
                        if (existingBlob != null)
                        {

                            existingBlob.Update(centerX, centerY, contourArea);
                            blobCenters.Add(existingBlob);
                        }
                        else
                        {
                            Blob newBlob = new Blob(blobCenters.Count, centerX, centerY, contourArea);
                            //System.Console.WriteLine("New Blob: " + newBlob);
                            blobCenters.Add(newBlob);
                        }
                    }
                }
            }

            contours.Dispose();
            return blobCenters;
        }

        // Helper function to find the closest blob based on proximity
        private (Blob closestBlob, double distance) FindClosestBlob(float x, float y, List<Blob> blobs)
        {
            double minDistance = double.MaxValue;
            Blob closestBlob = null;

            foreach (Blob blob in blobs)
            {
                double distance = Math.Sqrt(Math.Pow(x - blob.CenterX, 2) + Math.Pow(y - blob.CenterY, 2));

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestBlob = blob;
                }
            }

            //System.Console.WriteLine("minDistance: " + minDistance);

            // Adjust the threshold based on your requirements
            if (minDistance < 10)
            {
                return (closestBlob, minDistance);
            }
            else
            {
                return (null, minDistance);
            }
        }

        private Bitmap ColorizeDepthImage(DepthImageFrame depthFrame)
        {
            int width = depthFrame.Width;
            int height = depthFrame.Height;
            Bitmap colorizedBitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            // Define depth ranges for colorization
            // int blackMinDepth = 0;
            // int blackMaxDepth = 400;
            // int redMinDepth = 401;
            int redMinDepth = 200;
            int redMaxDepth = 1500;
            int blueMinDepth = 1501;
            int blueMaxDepth = 2000;
            int orangeMinDepth = 2001;
            int orangeMaxDepth = 3500;
            int greenMinDepth = 3501;
            int greenMaxDepth = 4000;

            // Lock the bitmap's bits
            BitmapData bmpData = colorizedBitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, colorizedBitmap.PixelFormat);

            try
            {
                int depthIndex = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++, depthIndex++)
                    {
                        short depth = depthMap[depthIndex].Depth;

                        byte red = 0;
                        byte green = 0;
                        byte blue = 0;

                        // if (depth >= blackMinDepth && depth <= blackMaxDepth)
                        // {
                        // // Set color to black for pixels in the range 0 to 400
                        // }
                        // else
                        if (depth >= redMinDepth && depth <= redMaxDepth)
                        {
                            red = 255;
                        }
                        else if (depth >= blueMinDepth && depth <= blueMaxDepth)
                        {
                            blue = 255;
                        }
                        else if (depth >= orangeMinDepth && depth <= orangeMaxDepth)
                        {
                            red = 128;
                            green = 128; // Green component for orange color
                        }
                        else if (depth >= greenMinDepth && depth <= greenMaxDepth)
                        {
                            green = 255;
                        }
                        int colorValue = Color.FromArgb(255, red, green, blue).ToArgb();
                        Marshal.WriteInt32(bmpData.Scan0, depthIndex * 4, colorValue);
                    }
                }
            }
            finally
            {
                // Unlock the bits
                colorizedBitmap.UnlockBits(bmpData);
                GC.Collect();
            }

            return colorizedBitmap;
        }

        private void InitializeKinect()
        {
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    sensor = potentialSensor;
                    break;
                }
            }

            if (sensor != null)
            {
                depthMap = new DepthImagePixel[sensor.DepthStream.FramePixelDataLength];

                sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                sensor.DepthFrameReady += SensorDepthFrameReady;
                try
                {
                    sensor.Start();
                }
                catch (Exception)
                {
                    sensor = null;
                    MessageBox.Show("Please Connect Kinect Sensor", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                }
            }
            else
            {
                MessageBox.Show("No Kinect Sensor Found Connected", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void frameBox_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }
    }
}
