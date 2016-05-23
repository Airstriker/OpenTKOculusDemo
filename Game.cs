using System;
using System.Drawing;
using System.Drawing.Imaging;
using OculusWrap;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Input;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;

namespace SimpleDemo
{
    public class Game : GameWindow
    {
        Wrap wrap = new Wrap();
        Hmd hmd = null;

        // Shared textures (rendertargets)
        TextureBuffer[] eyeRenderTexture = new TextureBuffer[2];
        DepthBuffer[] eyeDepthBuffer = new DepthBuffer[2];

        long frameIndex = 0;

        int mirrorFbo = 0;

        bool isVisible = true;

        Vector3 playerPos = new Vector3(0, 0, -10);

        Layers layers = new Layers();
        LayerEyeFov layerFov = null;

        OVRTypes.Sizei windowSize;
        MirrorTexture mirrorTexture = null;

        int cubeProgram = 0;

        int vao = 0;
        int vpLoc, worldLoc;
        int posLoc, colLoc;

        int cubeBuf, cubeColBuf, cubeIdxBuf;

        public Game()
        {
            this.KeyDown += Game_KeyDown;
        }

        void Game_KeyDown(object sender, OpenTK.Input.KeyboardKeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Exit();
                    break;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            InitShader();
            InitBuffer();

            // Define initialization parameters with debug flag.
            OVRTypes.InitParams initializationParameters = new OVRTypes.InitParams();
            initializationParameters.Flags = OVRTypes.InitFlags.Debug;

            // Initialize the Oculus runtime.
            bool success = wrap.Initialize(initializationParameters);
            if (!success)
            {
                MessageBox.Show("Failed to initialize the Oculus runtime library.", "Uh oh", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Exit();
                return;
            }

            // Use the head mounted display.
            OVRTypes.GraphicsLuid graphicsLuid;
            hmd = wrap.Hmd_Create(out graphicsLuid);
            if (hmd == null)
            {
                MessageBox.Show("Oculus Rift not detected.", "Uh oh", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Exit();
                return;
            }

            if (hmd.ProductName == string.Empty)
            {
                MessageBox.Show("The HMD is not enabled.", "There's a tear in the Rift", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Exit();
                return;
            }

            Console.WriteLine("SDK Version: " + wrap.GetVersionString());

            try
            {
                for (int i = 0; i < 2; i++)
                {
                    OVRTypes.Sizei idealTextureSize = hmd.GetFovTextureSize((OVRTypes.EyeType)i, hmd.DefaultEyeFov[i], 1);
                    eyeRenderTexture[i] = new TextureBuffer(wrap, hmd, true, true, idealTextureSize, 1, IntPtr.Zero, 1);
                    eyeDepthBuffer[i] = new DepthBuffer(eyeRenderTexture[i].GetSize(), 0);
                }

                // Note: the mirror window can be any size, for this sample we use 1/2 the HMD resolution
                windowSize = new OVRTypes.Sizei(hmd.Resolution.Width / 2, hmd.Resolution.Height / 2);

                //For image displayed at ordinary monitor - copy of Oculus rendered one.
                OVRTypes.MirrorTextureDesc mirrorTextureDescription = new OVRTypes.MirrorTextureDesc();
                mirrorTextureDescription.Format = OVRTypes.TextureFormat.R8G8B8A8_UNORM_SRGB;
                mirrorTextureDescription.Width = windowSize.Width;
                mirrorTextureDescription.Height = windowSize.Height;
                mirrorTextureDescription.MiscFlags = OVRTypes.TextureMiscFlags.None;

                // Create the texture used to display the rendered result on the computer monitor.
                OVRTypes.Result result;
                result = hmd.CreateMirrorTextureGL(mirrorTextureDescription, out mirrorTexture);
                WriteErrorDetails(wrap, result, "Failed to create mirror texture.");

                layerFov = layers.AddLayerEyeFov();
                layerFov.Header.Flags = OVRTypes.LayerFlags.TextureOriginAtBottomLeft; // OpenGL Texture coordinates start from bottom left
                layerFov.Header.Type = OVRTypes.LayerType.EyeFov;

                // Configure the mirror read buffer
                uint texId;
                result = mirrorTexture.GetBufferGL(out texId);
                WriteErrorDetails(wrap, result, "Failed to retrieve the texture from the created mirror texture buffer.");

                //Rendertarget for mirror desktop window
                GL.GenFramebuffers(1, out mirrorFbo);
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, mirrorFbo);
                GL.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texId, 0);
                GL.FramebufferRenderbuffer(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, 0);
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);

                // Turn off vsync to let the compositor do its magic
                this.VSync = VSyncMode.Off; //wglSwapIntervalEXT(0);

                // FloorLevel will give tracking poses where the floor height is 0
                result = hmd.SetTrackingOriginType(OVRTypes.TrackingOrigin.FloorLevel);
                WriteErrorDetails(wrap, result, "Failed to set tracking origin type.");

                GL.Enable(EnableCap.DepthTest); //DO NOT DELETE IT IN FUTURE UPDATES!
            }
            catch
            {
                // Release all resources
                Dispose(layers);
                if (mirrorFbo != 0) GL.DeleteFramebuffers(1, ref mirrorFbo);
                Dispose(mirrorTexture);
                for (int eyeIndex = 0; eyeIndex < 2; ++eyeIndex)
                {
                    Dispose(eyeRenderTexture[eyeIndex]);
                    Dispose(eyeDepthBuffer[eyeIndex]);
                }

                // Disposing the device, before the hmd, will cause the hmd to fail when disposing.
                // Disposing the device, after the hmd, will cause the dispose of the device to fail.
                // It looks as if the hmd steals ownership of the device and destroys it, when it's shutting down.
                // device.Dispose();
                Dispose(hmd);
                Dispose(wrap);
            }
        }

        private void InitBuffer()
        {
            GL.GenVertexArrays(1, out vao);

            GL.BindVertexArray(vao);

            GL.GenBuffers(1, out cubeColBuf);
            GL.BindBuffer(BufferTarget.ArrayBuffer, cubeColBuf);
            GL.BufferData<Vector4>(BufferTarget.ArrayBuffer, new IntPtr(colors.Length * Vector4.SizeInBytes), colors, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, Vector4.SizeInBytes, 0);

            GL.GenBuffers(1, out cubeBuf);
            GL.BindBuffer(BufferTarget.ArrayBuffer, cubeBuf);
            GL.BufferData<Vector3>(BufferTarget.ArrayBuffer, new IntPtr(cubeVertices.Length * Vector3.SizeInBytes), cubeVertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, 0);

            GL.GenBuffers(1, out cubeIdxBuf);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, cubeIdxBuf);
            GL.BufferData<uint>(BufferTarget.ElementArrayBuffer, new IntPtr(indices.Length * sizeof(uint)), indices, BufferUsageHint.StaticDraw);
            GL.BindVertexArray(0);
        }

        private void InitShader()
        {
            cubeProgram = GL.CreateProgram();

            // Vertex Shader
            int vshader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vshader, vshaderString);
            GL.CompileShader(vshader);

            string info;

            int compileResult;
            GL.GetShader(vshader, ShaderParameter.CompileStatus, out compileResult);

            GL.GetShaderInfoLog(vshader, out info);
            Console.WriteLine(info);
            // Pixel Shader
            int pshader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(pshader, pshaderString);
            GL.CompileShader(pshader);


            GL.GetShader(pshader, ShaderParameter.CompileStatus, out compileResult);

            GL.GetShaderInfoLog(pshader, out info);
            Console.WriteLine(info);

            GL.AttachShader(cubeProgram, vshader);
            GL.AttachShader(cubeProgram, pshader);

            GL.BindAttribLocation(cubeProgram, 0, "vertex_position");
            GL.BindAttribLocation(cubeProgram, 1, "vertex_color");

            GL.LinkProgram(cubeProgram);

            GL.DeleteShader(vshader);
            GL.DeleteShader(pshader);

            vpLoc = GL.GetUniformLocation(cubeProgram, "viewporj_matrix");
            worldLoc = GL.GetUniformLocation(cubeProgram, "world_matrix");

            // Just check correct attribute binding, -1 if error
            posLoc = GL.GetAttribLocation(cubeProgram, "vertex_position");
            colLoc = GL.GetAttribLocation(cubeProgram, "vertex_color");
        }


        private void RenderScene(Matrix4 viewProj, Matrix4 worldCube)
        {
            // Switch to cubeshader pipeline
            GL.UseProgram(cubeProgram);

            // Update Viewprojection and Worldmatrix on GPU
            GL.UniformMatrix4(vpLoc, false, ref viewProj);
            GL.UniformMatrix4(worldLoc, false, ref worldCube);


            // VAO keeps the attribute binding of the vertex and index buffer
            GL.BindVertexArray(vao);

            // Draw Cube
            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, IntPtr.Zero);

            //Unbind VAO
            GL.BindVertexArray(0);

            // Unbind shader program
            GL.UseProgram(0);
        }

        float startTime = 0;

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);
            startTime += (float)e.Time;

            // Call ovr_GetRenderDesc each frame to get the ovrEyeRenderDesc, as the returned values (e.g. HmdToEyeOffset) may change at runtime.
            OVRTypes.EyeRenderDesc[] eyeRenderDesc = new OVRTypes.EyeRenderDesc[2];
            eyeRenderDesc[0] = hmd.GetRenderDesc(OVRTypes.EyeType.Left, hmd.DefaultEyeFov[0]);
            eyeRenderDesc[1] = hmd.GetRenderDesc(OVRTypes.EyeType.Right, hmd.DefaultEyeFov[1]);

            // Get eye poses, feeding in correct IPD offset
            OVRTypes.Posef[] EyeRenderPose = new OVRTypes.Posef[2];
            OVRTypes.Vector3f[] HmdToEyeOffset = { eyeRenderDesc[0].HmdToEyeOffset, eyeRenderDesc[1].HmdToEyeOffset };

            // Keeping sensorSampleTime as close to ovr_GetTrackingState as possible - fed into the layer
            double sensorSampleTime;    // sensorSampleTime is fed into the layer later
            hmd.GetEyePoses(frameIndex, true, HmdToEyeOffset, ref EyeRenderPose, out sensorSampleTime);


            //double displayMidpoint = hmd.GetPredictedDisplayTime(0);
            //OVRTypes.TrackingState trackingState = hmd.GetTrackingState(displayMidpoint, true);

            //double ftiming = hmd.GetPredictedDisplayTime(0);
            //OVR.TrackingState hmdState = hmd.GetTrackingState(ftiming);


            Matrix4 worldCube = Matrix4.CreateScale(5) * Matrix4.CreateRotationX(startTime) * Matrix4.CreateRotationY(startTime) * Matrix4.CreateRotationZ(startTime) * Matrix4.CreateTranslation(new Vector3(0, 0, 10));

            try
            {
                if (isVisible)
                {
                    for (int eyeIndex = 0; eyeIndex < 2; eyeIndex++)
                    {
                        // Switch to eye render target
                        eyeRenderTexture[eyeIndex].SetAndClearRenderSurface(eyeDepthBuffer[eyeIndex]);

                        // Setup Viewmatrix
                        Quaternion rotationQuaternion = EyeRenderPose[eyeIndex].Orientation.ToTK();
                        Matrix4 rotationMatrix = Matrix4.CreateFromQuaternion(rotationQuaternion);

                        // I M P O R T A N T !!!! Play with this scaleMatrix to tweek HMD's Pitch, Yaw and Roll behavior. It depends on your coordinate system.
                        //Convert to X=right, Y=up, Z=in
                        //S = [1, 1, -1];
                        //viewMat = viewMat * S * R * S;

                        Matrix4 scaleMatrix = Matrix4.CreateScale(-1f, 1f, -1f);
                        rotationMatrix = scaleMatrix * rotationMatrix * scaleMatrix;

                        Vector3 lookUp = Vector3.Transform(Vector3.UnitY, rotationMatrix);
                        Vector3 lookAt = Vector3.Transform(Vector3.UnitZ, rotationMatrix);

                        Vector3 viewPosition = playerPos;

                        //NOTE! If head tracking is reversed at any axis - change minus to plus.
                        viewPosition.X -= EyeRenderPose[eyeIndex].Position.ToTK().X;
                        viewPosition.Y += EyeRenderPose[eyeIndex].Position.ToTK().Y;
                        viewPosition.Z -= EyeRenderPose[eyeIndex].Position.ToTK().Z;

                        Matrix4 view = Matrix4.LookAt(viewPosition, viewPosition + lookAt, lookUp);
                        //Thread.Sleep(10000);
                        Matrix4 proj = wrap.Matrix4f_Projection(hmd.DefaultEyeFov[eyeIndex], 0.1f, 1000.0f, OVRTypes.ProjectionModifier.None).ToTK();
                        proj.Transpose(); //DO NOT DELETE IT IN FUTURE UPDATES!

                        // OpenTK has Row Major Order and transposes matrices on the way to the shaders, thats why matrix multiplication is reverse order.
                        RenderScene(view * proj, worldCube);

                        // Avoids an error when calling SetAndClearRenderSurface during next iteration.
                        // Without this, during the next while loop iteration SetAndClearRenderSurface
                        // would bind a framebuffer with an invalid COLOR_ATTACHMENT0 because the texture ID
                        // associated with COLOR_ATTACHMENT0 had been unlocked by calling wglDXUnlockObjectsNV.
                        eyeRenderTexture[eyeIndex].UnsetRenderSurface();

                        // Commit changes to the textures so they get picked up frame
                        // Commits any pending changes to the TextureSwapChain, and advances its current index
                        eyeRenderTexture[eyeIndex].Commit();
                    }
                }

                // Do distortion rendering, Present and flush/sync

                for (int eyeIndex = 0; eyeIndex < 2; eyeIndex++)
                {
                    // Update layer
                    layerFov.ColorTexture[eyeIndex] = eyeRenderTexture[eyeIndex].TextureChain.TextureSwapChainPtr;
                    layerFov.Viewport[eyeIndex].Position = new OVRTypes.Vector2i(0, 0);
                    layerFov.Viewport[eyeIndex].Size = eyeRenderTexture[eyeIndex].GetSize();
                    layerFov.Fov[eyeIndex] = hmd.DefaultEyeFov[eyeIndex];
                    layerFov.RenderPose[eyeIndex] = EyeRenderPose[eyeIndex];
                    layerFov.SensorSampleTime = sensorSampleTime;
                }

                OVRTypes.Result result = hmd.SubmitFrame(0, layers);
                WriteErrorDetails(wrap, result, "Failed to submit the frame of the current layers.");

                isVisible = (result == OVRTypes.Result.Success);

                OVRTypes.SessionStatus sessionStatus = new OVRTypes.SessionStatus();
                hmd.GetSessionStatus(ref sessionStatus);
                if (sessionStatus.ShouldQuit == 1)
                    throw new Exception("SessionStatus.ShouldQuit"); //Check if ok to throw exception here
                                                                     //if (sessionStatus.ShouldRecenter == 1)
                                                                     //    hmd.RecenterTrackingOrigin();

                // Copy mirror data from mirror texture provided by OVR to backbuffer of the desktop window.
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, mirrorFbo);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
                int w = windowSize.Width;
                int h = windowSize.Height;

                GL.BlitFramebuffer(
                    0, h, w, 0,
                    0, 0, w, h,
                    ClearBufferMask.ColorBufferBit,
                    BlitFramebufferFilter.Nearest);

                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);

                this.SwapBuffers();

                frameIndex++;
            }
            catch
            {
                // Release all resources
                Dispose(layers);
                if (mirrorFbo != 0) GL.DeleteFramebuffers(1, ref mirrorFbo);
                Dispose(mirrorTexture);
                for (int eyeIndex = 0; eyeIndex < 2; ++eyeIndex)
                {
                    Dispose(eyeRenderTexture[eyeIndex]);
                    Dispose(eyeDepthBuffer[eyeIndex]);
                }

                // Disposing the device, before the hmd, will cause the hmd to fail when disposing.
                // Disposing the device, after the hmd, will cause the dispose of the device to fail.
                // It looks as if the hmd steals ownership of the device and destroys it, when it's shutting down.
                // device.Dispose();
                Dispose(hmd);
                Dispose(wrap);
            }
        }

        private Bitmap GrabScreenshot(int w, int h)
        {
            if (GraphicsContext.CurrentContext == null)
                throw new GraphicsContextMissingException();

            Bitmap bmp = new Bitmap(w, h);
            BitmapData data =
                bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            GL.ReadPixels(0, 0, w, h, OpenTK.Graphics.OpenGL4.PixelFormat.Bgr, PixelType.UnsignedByte, data.Scan0);
            bmp.UnlockBits(data);

            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
            return bmp;
        }

        protected override void OnUnload(EventArgs e)
        {
            // Release all resources
            Dispose(layers);
            if (mirrorFbo != 0) GL.DeleteFramebuffers(1, ref mirrorFbo);
            Dispose(mirrorTexture);
            for (int eyeIndex = 0; eyeIndex < 2; ++eyeIndex)
            {
                Dispose(eyeRenderTexture[eyeIndex]);
                Dispose(eyeDepthBuffer[eyeIndex]);
            }

            GL.DeleteBuffer(cubeBuf);
            GL.DeleteBuffer(cubeColBuf);
            GL.DeleteBuffer(cubeIdxBuf);

            GL.DeleteProgram(cubeProgram);

            // Disposing the device, before the hmd, will cause the hmd to fail when disposing.
            // Disposing the device, after the hmd, will cause the dispose of the device to fail.
            // It looks as if the hmd steals ownership of the device and destroys it, when it's shutting down.
            // device.Dispose();
            Dispose(hmd);
            Dispose(wrap);

            base.OnUnload(e);
        }


        //Called when the NativeWindow is about to close.
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Finish();
            OpenTK.Graphics.GraphicsContext current = (OpenTK.Graphics.GraphicsContext)OpenTK.Graphics.GraphicsContext.CurrentContext;
            current.MakeCurrent(null);
            current.Dispose();
        }


        /// <summary>
		/// Dispose the specified object, unless it's a null object.
		/// </summary>
		/// <param name="disposable">Object to dispose.</param>
		public static void Dispose(IDisposable disposable)
        {
            if (disposable != null)
                disposable.Dispose();
        }

        /// <summary>
        /// Write out any error details received from the Oculus SDK, into the debug output window.
        /// 
        /// Please note that writing text to the debug output window is a slow operation and will affect performance,
        /// if too many messages are written in a short timespan.
        /// </summary>
        /// <param name="oculus">OculusWrap object for which the error occurred.</param>
        /// <param name="result">Error code to write in the debug text.</param>
        /// <param name="message">Error message to include in the debug text.</param>
        public static void WriteErrorDetails(Wrap oculus, OVRTypes.Result result, string message)
        {
            if (result >= OVRTypes.Result.Success)
                return;

            // Retrieve the error message from the last occurring error.
            OVRTypes.ErrorInfo errorInformation = oculus.GetLastError();

            string formattedMessage = string.Format("{0}. \nMessage: {1} (Error code={2})", message, errorInformation.ErrorString, errorInformation.Result);
            Trace.WriteLine(formattedMessage);
            MessageBox.Show(formattedMessage, message);

            throw new Exception(formattedMessage);
        }

        private static readonly Vector3[] cubeVertices = new Vector3[]
        {
            new Vector3(-1.0f, -1.0f,  1.0f),
            new Vector3( 1.0f, -1.0f,  1.0f),
            new Vector3( 1.0f,  1.0f,  1.0f),
            new Vector3(-1.0f,  1.0f,  1.0f),
            new Vector3(-1.0f, -1.0f, -1.0f),
            new Vector3( 1.0f, -1.0f, -1.0f), 
            new Vector3( 1.0f,  1.0f, -1.0f),
            new Vector3(-1.0f,  1.0f, -1.0f) 
        };

        private static readonly Vector4[] colors = new Vector4[] 
        {
            new Vector4(1, 0, 0, 1),
            new Vector4(0, 1, 0, 1),
            new Vector4(0, 0, 1, 1),
            new Vector4(1, 0, 0, 1),
            new Vector4(0, 1, 0, 1), 
            new Vector4(0, 0, 1, 1),
            new Vector4(1, 0, 0, 1),
            new Vector4(0, 1, 0, 1)
        };

        private static readonly uint[] indices = new uint[]
            {
                0, 1, 2, 2, 3, 0,
                // top face
                3, 2, 6, 6, 7, 3,
                // left face
                4, 0, 3, 3, 7, 4,
                // bottom face
                5, 1, 0, 0, 4, 5,
                // right face
                6, 2, 1, 1, 5, 6,
                 // back face
                7, 6, 5, 5, 4, 7
            };

        private const string vshaderString = @"
#version 420

uniform mat4 viewporj_matrix;
uniform mat4 world_matrix;

in vec3 vertex_position;

in vec4 vertex_color;

out vec4 oColor;

void main() 
{	
    oColor = vertex_color;
	gl_Position = viewporj_matrix * (world_matrix * vec4(vertex_position, 1.0));
}";
        private const string pshaderString = @"
#version 420

in vec4 oColor;
out vec4 out_frag_color;

void main(void)
{
	out_frag_color.rgba = oColor.rgba;
}";
    }

    public static class Extensions
    {
        public static Quaternion ToTK(this OVRTypes.Quaternionf quat)
        {
            return new Quaternion(quat.X, quat.Y, quat.Z, quat.W);
        }

        public static Vector3 ToTK(this OVRTypes.Vector3f vec)
        {
            return new Vector3(vec.X, vec.Y, vec.Z);
        }

        public static Matrix4 ToTK(this OVRTypes.Matrix4f mat)
        {
            Matrix4 tkMAt = new Matrix4(
                new Vector4(mat.M11, mat.M12, mat.M13, mat.M14),
                new Vector4(mat.M21, mat.M22, mat.M23, mat.M24),
                new Vector4(mat.M31, mat.M32, mat.M33, mat.M34),
                new Vector4(mat.M41, mat.M42, mat.M43, mat.M44)
                );

            return tkMAt;
        }
    }
}
