
//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.SkeletonBasics
{
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using Microsoft.Kinect;
    using System;
    using System.Runtime.InteropServices;
    using System.Diagnostics;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        double volume;
        double thresvol;
        /// <summary>
        /// Width of output drawing
        /// </summary>
        private const float RenderWidth = 640.0f;

        /// <summary>
        /// Height of our output drawing
        /// </summary>
        private const float RenderHeight = 480.0f;

        /// <summary>
        /// Thickness of drawn joint lines
        /// </summary>
        private const double JointThickness = 3;

        /// <summary>
        /// Thickness of body center ellipse
        /// </summary>
        private const double BodyCenterThickness = 10;

        /// <summary>
        /// Thickness of clip edge rectangles
        /// </summary>
        private const double ClipBoundsThickness = 10;

        /// <summary>
        /// Brush used to draw skeleton center point
        /// </summary>
        private readonly Brush centerPointBrush = Brushes.Blue;

        /// <summary>
        /// Brush used for drawing joints that are currently tracked
        /// </summary>
        private readonly Brush trackedJointBrush = new SolidColorBrush(Color.FromArgb(255, 68, 192, 68));

        /// <summary>
        /// Brush used for drawing joints that are currently inferred
        /// </summary>        
        private readonly Brush inferredJointBrush = Brushes.Yellow;

        /// <summary>
        /// Pen used for drawing bones that are currently tracked
        /// </summary>
        private readonly Pen trackedBonePen = new Pen(Brushes.Green, 6);

        /// <summary>
        /// Pen used for drawing bones that are currently inferred
        /// </summary>        
        private readonly Pen inferredBonePen = new Pen(Brushes.Gray, 1);

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor sensor;

        /// <summary>
        /// Drawing group for skeleton rendering output
        /// </summary>
        private DrawingGroup drawingGroup;

        /// <summary>
        /// Drawing image that we will display
        /// </summary>
        private DrawingImage imageSource;


        private int frameCount = 0; // Use to get the frame
        private Vector4[] startingFrame, endingFrame; // Use to store the coordinate of a specific Joint at the starting frame and ending frame
        private float[] posDisplacement; // Use to store the displacement of a specific Joint between starting frame and ending frame
        private int noOfJoints = 0; // Use to get the number of Joints change during User selection, useful to know when to intialize a new starting frame, ending frame and posdisplacement

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Draws indicators to show which edges are clipping skeleton data
        /// </summary>
        /// <param name="skeleton">skeleton to draw clipping information for</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private static void RenderClippedEdges(Skeleton skeleton, DrawingContext drawingContext)
        {
            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Bottom))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, RenderHeight - ClipBoundsThickness, RenderWidth, ClipBoundsThickness));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Top))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, RenderWidth, ClipBoundsThickness));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Left))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, ClipBoundsThickness, RenderHeight));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Right))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(RenderWidth - ClipBoundsThickness, 0, ClipBoundsThickness, RenderHeight));
            }
        }

        /// <summary>
        /// Execute startup tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            // Create the drawing group we'll use for drawing
            this.drawingGroup = new DrawingGroup();

            // Create an image source that we can use in our image control
            this.imageSource = new DrawingImage(this.drawingGroup);

            // Display the drawing using our image control
            Image.Source = this.imageSource;

            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit (See components in Toolkit Browser).
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {
                // Turn on the skeleton stream to receive skeleton frames
                this.sensor.SkeletonStream.Enable();

                // Add an event handler to be called whenever there is new color frame data
                this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;

                // Start the sensor!
                try
                {
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
            }

            if (null == this.sensor)
            {
                this.statusBarText.Text = Properties.Resources.NoKinectReady;
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (null != this.sensor)
            {
                this.sensor.Stop();
            }

            SendMessageW(Process.GetCurrentProcess().MainWindowHandle, WM_APPCOMMAND, Process.GetCurrentProcess().MainWindowHandle, (IntPtr)APPCOMMAND_VOLUME_MUTE);

        }

        /// <summary>
        /// Event handler for Kinect sensor's SkeletonFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            Skeleton[] skeletons = new Skeleton[0];
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    skeletonFrame.CopySkeletonDataTo(skeletons);

                    TrackClosestSkeleton(skeletons); // Call method to track closest skeleton, not yet tested
                }
            }

            using (DrawingContext dc = this.drawingGroup.Open())
            {
                // Draw a transparent background to set the render size
                dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));

                if (skeletons.Length != 0)
                {
                    foreach (Skeleton skel in skeletons)
                    {

                        RenderClippedEdges(skel, dc);

                        if (skel.TrackingState == SkeletonTrackingState.Tracked)
                        {
                            this.DrawBonesAndJoints(skel, dc);

                            VolumeControl(skel);
                        
                        }
                        else if (skel.TrackingState == SkeletonTrackingState.PositionOnly)
                        {
                            dc.DrawEllipse(
                            this.centerPointBrush,
                            null,
                            this.SkeletonPointToScreen(skel.Position),
                            BodyCenterThickness,
                            BodyCenterThickness);
                        }
                    }
                }

                // prevent drawing outside of our render area
                this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
            }
        }

        /// <summary>
        /// Draws a skeleton's bones and joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void DrawBonesAndJoints(Skeleton skeleton, DrawingContext drawingContext)
        {
            //Torso
            //if (JointCombobox.SelectedItem == Torso)
            //{            
            if (Head.IsSelected == true)
            {
                this.DrawBone(skeleton, drawingContext, JointType.Head, JointType.ShoulderCenter);
            }

            if (Torso.IsSelected == true)
            {
                this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderLeft);
                this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderRight);
                this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.Spine);
                this.DrawBone(skeleton, drawingContext, JointType.Spine, JointType.HipCenter);
                this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipLeft);
                this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipRight);
            }

            // Left Arm
            //else if (JointCombobox.SelectedItem == LeftArm)
            //{     
            if (LeftArm.IsSelected == true)
            {
                this.DrawBone(skeleton, drawingContext, JointType.ShoulderLeft, JointType.ElbowLeft);
                this.DrawBone(skeleton, drawingContext, JointType.ElbowLeft, JointType.WristLeft);
                this.DrawBone(skeleton, drawingContext, JointType.WristLeft, JointType.HandLeft);
            }

            // Right Arm
            // else if (JointCombobox.SelectedItem == RightArm)
            // {     
            if (RightArm.IsSelected == true)
            {
                this.DrawBone(skeleton, drawingContext, JointType.ShoulderRight, JointType.ElbowRight);
                this.DrawBone(skeleton, drawingContext, JointType.ElbowRight, JointType.WristRight);
                this.DrawBone(skeleton, drawingContext, JointType.WristRight, JointType.HandRight);
            }

            //Left Leg
            //else if (JointCombobox.SelectedItem == LeftLeg)
            //{ 
            if (LeftLeg.IsSelected == true)
            {
                this.DrawBone(skeleton, drawingContext, JointType.HipLeft, JointType.KneeLeft);
                this.DrawBone(skeleton, drawingContext, JointType.KneeLeft, JointType.AnkleLeft);
                this.DrawBone(skeleton, drawingContext, JointType.AnkleLeft, JointType.FootLeft);
            }

            // Right Leg
            // else if (JointCombobox.SelectedItem == RightLeg)
            // {
            if (RightLeg.IsSelected == true)
            {
                this.DrawBone(skeleton, drawingContext, JointType.HipRight, JointType.KneeRight);
                this.DrawBone(skeleton, drawingContext, JointType.KneeRight, JointType.AnkleRight);
                this.DrawBone(skeleton, drawingContext, JointType.AnkleRight, JointType.FootRight);
            }
            // Render Joints
            foreach (Joint joint in skeleton.Joints)
            {
                Brush drawBrush = null;

                if (joint.TrackingState == JointTrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;
                }
                else if (joint.TrackingState == JointTrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;
                }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, this.SkeletonPointToScreen(joint.Position), JointThickness, JointThickness);
                }
            }
        }

        /// <summary>
        /// Maps a SkeletonPoint to lie within our render space and converts to Point
        /// </summary>
        /// <param name="skelpoint">point to map</param>
        /// <returns>mapped point</returns>
        private Point SkeletonPointToScreen(SkeletonPoint skelpoint)
        {
            // Convert point to depth space.  
            // We are not using depth directly, but we do want the points in our 640x480 output resolution.
            DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }

        /// <summary>
        /// Draws a bone line between two joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw bones from</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        /// <param name="jointType0">joint to start drawing from</param>
        /// <param name="jointType1">joint to end drawing at</param>
        private void DrawBone(Skeleton skeleton, DrawingContext drawingContext, JointType jointType0, JointType jointType1)
        {
            Joint joint0 = skeleton.Joints[jointType0];
            Joint joint1 = skeleton.Joints[jointType1];

            // If we can't find either of these joints, exit
            if (joint0.TrackingState == JointTrackingState.NotTracked ||
                joint1.TrackingState == JointTrackingState.NotTracked)
            {
                return;
            }

            // Don't draw if both points are inferred
            if (joint0.TrackingState == JointTrackingState.Inferred &&
                joint1.TrackingState == JointTrackingState.Inferred)
            {
                return;
            }

            // We assume all drawn bones are inferred unless BOTH joints are tracked
            Pen drawPen = this.inferredBonePen;
            if (joint0.TrackingState == JointTrackingState.Tracked && joint1.TrackingState == JointTrackingState.Tracked)
            {
                drawPen = this.trackedBonePen;
            }

            drawingContext.DrawLine(drawPen, this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position));
        }

        /// <summary>
        /// Handles the checking or unchecking of the seated mode combo box
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void CheckBoxSeatedModeChanged(object sender, RoutedEventArgs e)
        {
            if (null != this.sensor)
            {
                if (this.checkBoxSeatedMode.IsChecked.GetValueOrDefault())
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                }
                else
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
                }
            }
        }

        #region Track Closest Skeleton only
        /// <summary>
        /// Track Closest Skeleton only
        /// </summary>
        /// <param name="j">Skeleton array object</param>
        private void TrackClosestSkeleton(Skeleton[] skels)
        {
            if (this.sensor != null && this.sensor.SkeletonStream != null)
            {
                if (!this.sensor.SkeletonStream.AppChoosesSkeletons)
                {
                    this.sensor.SkeletonStream.AppChoosesSkeletons = true; // Ensure AppChoosesSkeletons is set
                }

                float closestDistance = 10000f; // Start with a far enough distance
                int closestID = 0;

                foreach (Skeleton skeleton in skels)
                {
                    if (skeleton.TrackingState != SkeletonTrackingState.NotTracked)
                    {
                        if (skeleton.Position.Z < closestDistance)
                        {
                            closestID = skeleton.TrackingId;
                            closestDistance = skeleton.Position.Z;
                        }
                    }
                }

                if (closestID > 0)
                {
                    this.sensor.SkeletonStream.ChooseSkeletons(closestID); // Track this skeleton
                }
            }
        }
        #endregion

        #region Write Position to File
        /// <summary>
        /// Write Position to on the screen and also to file
        /// </summary>
        /// <param name="j">Joint array object</param>
        private void WriteJointPosition(Joint[] j)
        {
            if (j.Length != 0)
            {
                TextWriter tsw = new StreamWriter(@"C:\Users\vigne_000\Documents\GitHub\CSE5911-capstone\KinectTest\SkeletonData.txt", true);
                double x, y, z;
                foreach (Joint joi in j)
                {
                    x = Math.Round(joi.Position.X, 3);
                    y = Math.Round(joi.Position.Y, 3);
                    z = Math.Round(joi.Position.Z, 3);

                    // Write to file which JointType and its x, y, z coordinate
                    tsw.WriteLine(joi.JointType);
                    tsw.WriteLine("x = " + joi.Position.X.ToString() + "\t\ty = " + joi.Position.Y.ToString() + "\t\t  z = " + joi.Position.Z.ToString());
                }
                tsw.Close();
            }
        }
        #endregion

        #region VolumeControl
        /// <summary>
        /// Control Master Volume
        /// </summary>
        /// <param name="skel">Skeleton Object</param>
        private void VolumeControl(Skeleton skel)
        {
            WriteJointPosition(GetJointsCombination(skel)); //Trigger data Collection
            Joint[] j = GetJointsCombination(skel);
            Vector4[] jointCoordinate = new Vector4[j.Length];
            //bool mute = true;

            if (noOfJoints != j.Length) // Check to see if the Joint selection has changed then initialized variables
            {
                startingFrame = new Vector4[j.Length];
                endingFrame = new Vector4[j.Length];
                posDisplacement = new float[j.Length];
                noOfJoints = j.Length;
            }

            for (int i = 0; i < j.Length; i++)  // Get the Coordinate of each joint and put it in an array
            {
                //Debug.WriteLine("j.Length = " + j.Length);
                //Debug.WriteLine("JointCoordinate.Length = " + jointCoordinate.Length);
                jointCoordinate[i].X = (float)Math.Round(j[i].Position.X, 3);
                jointCoordinate[i].Y = (float)Math.Round(j[i].Position.Y, 3);
                jointCoordinate[i].Z = (float)Math.Round(j[i].Position.Z, 3);


                // Check the position of each joint, if they are moving, increase the volume.
                if (frameCount == 1)
                {
                    //Debug.WriteLine("At Frame 1");
                    startingFrame[i].X = (float)Math.Round(jointCoordinate[i].X, 3);
                    startingFrame[i].Y = (float)Math.Round(jointCoordinate[i].Y, 3);
                    startingFrame[i].Z = (float)Math.Round(jointCoordinate[i].Z, 3);
                }
                if (frameCount == 10)
                {
                    //Debug.WriteLine("At Frame 10");
                    Debug.WriteLine("At Frame 10. i = " + i);
                    endingFrame[i].X = (float)Math.Round(jointCoordinate[i].X, 3);
                    endingFrame[i].Y = (float)Math.Round(jointCoordinate[i].Y, 3);
                    endingFrame[i].Z = (float)Math.Round(jointCoordinate[i].Z, 3);

                    // Calculate the displacement of the Joint between the starting frame and ending frame
                    posDisplacement[i] = (float)Math.Sqrt(Math.Pow(endingFrame[i].X - startingFrame[i].X, 2) + Math.Pow(endingFrame[i].Y - startingFrame[i].Y, 2) + Math.Pow(endingFrame[i].Z - startingFrame[i].Z, 2));
                    //y_coordinate.Text = posDisplacement[i].ToString(); // Testing how the value changes

                    //mute = mute && posDisplacement[i] <= 0.05f;
                    if (posDisplacement[i] > 0.05f && volume < thresvol)
                    {
                        for (int l = 0; l < j.Length; l++)
                        {
                            SendMessageW(Process.GetCurrentProcess().MainWindowHandle, WM_APPCOMMAND, Process.GetCurrentProcess().MainWindowHandle, (IntPtr)APPCOMMAND_VOLUME_UP);
                        }
                    }
                    else if (posDisplacement[i] <= 0.05f)
                    {
                        SendMessageW(Process.GetCurrentProcess().MainWindowHandle, WM_APPCOMMAND, Process.GetCurrentProcess().MainWindowHandle, (IntPtr)APPCOMMAND_VOLUME_DOWN);
                    }
                    //if (mute == true && i>0)
                    //{
                    //    SendMessageW(Process.GetCurrentProcess().MainWindowHandle, WM_APPCOMMAND, Process.GetCurrentProcess().MainWindowHandle, (IntPtr)APPCOMMAND_VOLUME_MUTE);
                    //}
                }

            }

            frameCount++;
            if (frameCount > 10)
            {
                frameCount %= 10;
            }
        }

        #endregion

        #region Joint Selection
        /// <summary>
        /// Select Joints Combination 
        /// </summary>
        /// <param name="s">Skeleton Object</param>
        private Joint[] GetJointsCombination(Skeleton s)
        {
            int c = 0;

            Joint[] jt = new Joint[ListBoxJointSelect.SelectedItems.Count]; // With ListboxItem, I can get how many of them are selected and and set the length of the array.

            if (ListBoxJointSelect.SelectedItems.Count != 0)
            {
                if (Head.IsSelected == true)
                {
                    jt[c] = s.Joints[JointType.Head];
                    c++;
                }
                if (Torso.IsSelected == true)
                {
                    jt[c] = s.Joints[JointType.Spine];
                    c++;
                }
                if (LeftArm.IsSelected == true)
                {
                    jt[c] = s.Joints[JointType.HandLeft];
                    c++;
                }
                if (RightArm.IsSelected == true)
                {
                    jt[c] = s.Joints[JointType.HandRight];
                    c++;
                }
                if (LeftLeg.IsSelected == true)
                {
                    jt[c] = s.Joints[JointType.FootLeft];
                    c++;
                }
                if (RightLeg.IsSelected == true)
                {
                    jt[c] = s.Joints[JointType.FootRight];
                    c++;
                }
            }
            return jt;
        }
        #endregion

        
        private const int APPCOMMAND_VOLUME_MUTE = 0x80000;
        private const int APPCOMMAND_VOLUME_UP = 0xA0000;
        private const int APPCOMMAND_VOLUME_DOWN = 0x90000;
        private const int WM_APPCOMMAND = 0x319;
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        #region Track All Joints Button
        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < ListBoxJointSelect.Items.Count; i++)
            {
                //ListBoxJointSelect.Set
                Head.IsSelected = true;
                Torso.IsSelected = true;
                RightArm.IsSelected = true;
                LeftArm.IsSelected = true;
                RightLeg.IsSelected = true;
                LeftLeg.IsSelected = true;
            }

        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Head.IsSelected = false;
            Torso.IsSelected = false;
            RightArm.IsSelected = false;
            LeftArm.IsSelected = false;
            RightLeg.IsSelected = false;
            LeftLeg.IsSelected = false;
        }

        private void OnUnselected(object sender, RoutedEventArgs e)
        {
            if (SelectAllCheckedBox.IsChecked == true)
            {
                SelectAllCheckedBox.IsChecked = false;
            }
        }
        #endregion

        private void Slider_ValueChanged_1(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var slider = sender as Slider;
            this.thresvol = slider.Value;
            this.volume++;
        }

    }
}