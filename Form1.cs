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
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Echo_Sense
{
    public partial class Form1 : Form
    {
        private KinectSensor sensor;
        private DepthImagePixel[] depthMap;
        public ISoundEngine soundEngine = new ISoundEngine();
        ISoundSource music;
        List<Blob> existingBlobs = new List<Blob>();
        List<Blob> redExistingBlobs = new List<Blob>();
        public static object dataLock = new object(); // Synchronization lock for safety
        List<BlobParameters> blobsParams = new List<BlobParameters>();

        public class BlobParameters
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }

        }


        public Form1()
        {
            InitializeComponent();

            this.InitializeKinect();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.music = soundEngine.GetSoundSource("C:\\Users\\User\\source\\repos\\Echo Sense\\sound2.wav", true);
            soundEngine.SetListenerPosition(0, 0, 0, 0, 0, 1);

            if (this.music == null)
            {
                Console.WriteLine("Error playing 3D sound.");
                return;
            }

            void FunctionToExecute()
            {
                lock (dataLock) // Ensure thread-safe access
                {
                    for (int i = 0; i < blobsParams.Count; i++)
                    {
                        var parameters = blobsParams[i];
                        Console.WriteLine($"Thread executing with parameters:{i} {parameters.X}, {parameters.Y} {parameters.Z}");
                        this.soundEngine.Play3D(this.music, parameters.X, parameters.Y, parameters.Z,false,false,false);
                        Thread.Sleep(100);
                    }
                    //blobsParams.Clear();

                }
            }

            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    FunctionToExecute();
                    Thread.Sleep(1000);
                }
            });

            thread.Start();

        }


        private void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {

            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame != null)
                {
                    depthFrame.CopyDepthImagePixelDataTo(this.depthMap);

                    //Image<Bgra, byte> bitmapSource = ColorizeDepthImage(depthFrame);
                    Mat bitmapSource = ColorizeDepthImage(depthFrame).ToMat();
                    CvInvoke.Flip(bitmapSource, bitmapSource, FlipType.Horizontal);
                    Mat hlsFrame = new Mat();
                    CvInvoke.CvtColor(bitmapSource, hlsFrame, ColorConversion.Bgr2Hls);

                    Mat yellowBinaryMask = new Mat();
                    CvInvoke.InRange(hlsFrame, new ScalarArray(new MCvScalar(20, 100, 100)), new ScalarArray(new MCvScalar(30, 255, 255)), yellowBinaryMask);
                    Mat redBinaryMask = new Mat();
                    CvInvoke.InRange(hlsFrame, new ScalarArray(new MCvScalar(0, 100, 100)), new ScalarArray(new MCvScalar(10, 255, 255)), redBinaryMask);

                    Mat blueBinaryMask = new Mat();
                    CvInvoke.InRange(hlsFrame, new ScalarArray(new MCvScalar(100, 100, 100)), new ScalarArray(new MCvScalar(120, 255, 255)), blueBinaryMask);

                    Mat orangeBinaryMask = new Mat();
                    CvInvoke.InRange(hlsFrame, new ScalarArray(new MCvScalar(10, 100, 100)), new ScalarArray(new MCvScalar(20, 255, 255)), orangeBinaryMask);

                    Mat greenBinaryMask = new Mat();
                    CvInvoke.InRange(hlsFrame, new ScalarArray(new MCvScalar(40, 100, 100)), new ScalarArray(new MCvScalar(80, 255, 255)), greenBinaryMask);

                    VectorOfVectorOfPoint redContours = new VectorOfVectorOfPoint();
                    CvInvoke.FindContours(redBinaryMask, redContours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);
                    List<Blob> redBlobCenters = GetBlobCenters(redBinaryMask.ToImage<Gray, byte>(), 5000, redExistingBlobs);

                    VectorOfVectorOfPoint blueContours = new VectorOfVectorOfPoint();
                    CvInvoke.FindContours(redBinaryMask, blueContours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);
                    List<Blob> blueBlobCenters = GetBlobCenters(blueBinaryMask.ToImage<Gray, byte>(), 9000, existingBlobs);

                    foreach (var blobCenter in redBlobCenters)
                    {
                        redExistingBlobs.Add(new Blob(blobCenter.ID, blobCenter.CenterX, blobCenter.CenterY, blobCenter.Area));
                    }

                    //foreach (var blobCenter in blueBlobCenters)
                    //{

                    //    existingBlobs.Add(new Blob(blobCenter.ID, blobCenter.CenterX, blobCenter.CenterY, blobCenter.Area, this.music,this.soundEngine));

                    //    // The parameters are: image, center, radius, color, thickness (-1 for filled circle)
                    //}
                    //CvInvoke.FindContours(yellowBinaryMask, yellowContours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);
                    // TODO: add a new list that will count the blobs that is existing 
                    blobsParams.Clear();

                    foreach (var blob in redBlobCenters)
                    {
                        // Convert the center coordinates to integers
                        int centerX = (int)blob.CenterX;
                        int centerY = (int)blob.CenterY;

                        // Define the desired ranges
                        int rangeX = 67;
                        int rangeY = 50;

                        // Scale and adjust centerX and centerY values within the desired relative range
                        int scaledCenterX = (int)ScaleBetween(centerX, -rangeX, rangeX, 0, 640);
                        int scaledCenterY = (int)ScaleBetween(centerY, rangeY, -rangeY, 0, 480);

                        int length = (int)Math.Sqrt(scaledCenterX * scaledCenterX + scaledCenterY * scaledCenterY + 2 * 2);

                     
                        if (length != 0)
                        {
                            float normalizedX = (float)scaledCenterX / length;
                            float normalizedY = (float)scaledCenterY / length;

                            blobsParams.Add(new BlobParameters { X = normalizedX * 10, Y = normalizedY *5, Z = (10f / length) * 10 });
                        }
                        //blobsParams.Add(new BlobParameters { X = -20 /length, Y = 0/ length, Z = 2 / length });

                        // Draw a circle on the colorized image
                        //CvInvoke.Circle(bitmapSource, new Point(centerX, centerY), 20, new MCvScalar(255, 255, 255, 255), -1);
                        CvInvoke.PutText(bitmapSource, $"{scaledCenterX}, {scaledCenterY}, {length}", new Point(centerX, centerY), FontFace.HersheyComplex, 1, new MCvScalar(255, 255, 255, 255), 2);
                    }



                    CvInvoke.DrawContours(bitmapSource, redContours, -1, new MCvScalar(0, 255, 0), 2);
                    this.pictureBox2.Image = redBinaryMask.ToBitmap();
                    this.frameBox.Image = bitmapSource.ToBitmap();
                    hlsFrame.Dispose();
                    yellowBinaryMask.Dispose();
                    redBinaryMask.Dispose();
                    greenBinaryMask.Dispose();
                    blueBinaryMask.Dispose();
                    orangeBinaryMask.Dispose();
                    redContours.Dispose();
                    bitmapSource.Dispose();
                    //contours.Dispose();

                }
            }
        }
        // Define blobCenters as a class-level variable
        float ScaleBetween(float unscaledNum, float minAllowed, float maxAllowed, float min, float max)
        {
            return (maxAllowed - minAllowed) * (unscaledNum - min) / (max - min) + minAllowed;
        }
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


        // Blob class to store information about a blob
        public class Blob
        {
            public float CenterX { get; set; }
            public float CenterY { get; set; }
            public double Area { get; set; }
            public int ID { get; set; }

            public Blob(int id, float centerX, float centerY, double area)
            {
                CenterX = centerX;
                CenterY = centerY;
                Area = area;
                ID = id;
            }

            public void Update(float centerX, float centerY, double area)
            {
                CenterX = centerX;
                CenterY = centerY;
                Area = area;

            }
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
            int blackMinDepth = 0;
            int blackMaxDepth = 400;
            int redMinDepth = 401;
            int redMaxDepth = 700;
            int blueMinDepth = 701;
            int blueMaxDepth = 1200;
            int orangeMinDepth = 1201;
            int orangeMaxDepth = 1500;
            int greenMinDepth = 1501;
            int greenMaxDepth = 2000;

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

                        if (depth >= blackMinDepth && depth <= blackMaxDepth)
                        {
                            // Set color to black for pixels in the range 0 to 400
                        }
                        else if (depth >= redMinDepth && depth <= redMaxDepth)
                        {
                            red = 255;
                        }
                        else if (depth >= blueMinDepth && depth <= blueMaxDepth)
                        {
                            blue = 255;
                        }
                        else if (depth >= orangeMinDepth && depth <= orangeMaxDepth)
                        {
                            red = 255;
                            green = 165; // Green component for orange color
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
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (this.sensor != null)
            {
                this.depthMap = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];

                this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                this.sensor.DepthFrameReady += this.SensorDepthFrameReady;
                try
                {
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
            }
        }



    }
}
