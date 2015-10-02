using System;
using System.Drawing;
using System.Drawing.Imaging;
using OculusWrap;
using OculusWrap.GL;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Input;
using System.Windows.Forms;

namespace SimpleDemo
{
    public class Game : GameWindow
    {
        Wrap wrap = new Wrap();
        Hmd hmd;

        // Shared textures (rendertargets)
        OvrSharedRendertarget[] eyeRenderTexture = new OvrSharedRendertarget[2];
        DepthBuffer[] eyeDepthBuffer = new DepthBuffer[2];

        OculusWrap.OVR.EyeRenderDesc[] EyeRenderDesc = new OVR.EyeRenderDesc[2];

        int mirrorFbo = 0;

        bool isVisible = true;

        Vector3 playerPos = new Vector3(0, 0, -10);

        Layers layers = new Layers();
        LayerEyeFov layerFov;

        OculusWrap.GL.MirrorTexture mirrorTex;

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

            // Initialize the Oculus runtime.
            bool success = wrap.Initialize();
            if (!success)
            {
                MessageBox.Show("Failed to initialize the Oculus runtime library.", "Uh oh", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Use the head mounted display.
            OVR.GraphicsLuid graphicsLuid;
            hmd = wrap.Hmd_Create(out graphicsLuid);
            if (hmd == null)
            {
                MessageBox.Show("Oculus Rift not detected.", "Uh oh", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (hmd.ProductName == string.Empty)
            {
                MessageBox.Show("The HMD is not enabled.", "There's a tear in the Rift", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Console.WriteLine("SDK Version: " + wrap.GetVersionString());

            for (int i = 0; i < 2; i++)
            {
                OVR.Sizei idealTextureSize = hmd.GetFovTextureSize((OVR.EyeType)i, hmd.DefaultEyeFov[i], 1);
                eyeRenderTexture[i] = new OvrSharedRendertarget(idealTextureSize.Width, idealTextureSize.Height, hmd);
                eyeDepthBuffer[i] = new DepthBuffer(eyeRenderTexture[i].Width, eyeRenderTexture[i].Height);
            }

            hmd.CreateMirrorTextureGL((uint)All.Rgba, this.Width, this.Height, out mirrorTex);

            layerFov = layers.AddLayerEyeFov();
            layerFov.Header.Flags = OVR.LayerFlags.None; // OpenGL Texture coordinates start from bottom left
            layerFov.Header.Type = OVR.LayerType.EyeFov;

            //Rendertarget for mirror desktop window
            GL.GenFramebuffers(1, out mirrorFbo);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, mirrorFbo);
            GL.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, mirrorTex.Texture.TexId, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, 0);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);

            EyeRenderDesc[0] = hmd.GetRenderDesc(OVR.EyeType.Left, hmd.DefaultEyeFov[0]);
            EyeRenderDesc[1] = hmd.GetRenderDesc(OVR.EyeType.Right, hmd.DefaultEyeFov[1]);

            // Specify which head tracking capabilities to enable.
            hmd.SetEnabledCaps(OVR.HmdCaps.DebugDevice);

            // Start the sensor
            hmd.ConfigureTracking(OVR.TrackingCaps.ovrTrackingCap_Orientation | OVR.TrackingCaps.ovrTrackingCap_MagYawCorrection | OVR.TrackingCaps.ovrTrackingCap_Position, OVR.TrackingCaps.None);

            this.VSync = VSyncMode.Off;

            hmd.RecenterPose();

            // Init GL
            GL.Enable(EnableCap.DepthTest);
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

            // Get eye poses, feeding in correct IPD offset
            OVR.Vector3f[] ViewOffset = new OVR.Vector3f[] 
            { 
                EyeRenderDesc[0].HmdToEyeViewOffset,  
                EyeRenderDesc[1].HmdToEyeViewOffset 
            };

            OVR.FrameTiming ftiming = hmd.GetFrameTiming(0);
            OVR.TrackingState hmdState = hmd.GetTrackingState(ftiming.DisplayMidpointSeconds);
            OVR.Posef[] eyePoses = new OVR.Posef[2];

            wrap.CalcEyePoses(hmdState.HeadPose.ThePose, ViewOffset, ref eyePoses);

            Matrix4 worldCube = Matrix4.CreateScale(5) * Matrix4.CreateRotationX(startTime) * Matrix4.CreateRotationY(startTime) * Matrix4.CreateRotationZ(startTime) * Matrix4.CreateTranslation(new Vector3(0, 0, 10));

            if (isVisible)
            {
                for (int eyeIndex = 0; eyeIndex < 2; eyeIndex++)
                {
                    layerFov.RenderPose[eyeIndex] = eyePoses[eyeIndex];

                    // Increment SwapTextureIndex
                    eyeRenderTexture[eyeIndex].TextureSet.CurrentIndex++;

                    GL.Viewport(0, 0, eyeRenderTexture[eyeIndex].Width, eyeRenderTexture[eyeIndex].Height);

                    // Set and Clear Rendertarget
                    eyeRenderTexture[eyeIndex].Bind(eyeDepthBuffer[eyeIndex].TexId);
                    GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);

                    // Setup Viewmatrix
                    Quaternion rotationQuaternion = layerFov.RenderPose[eyeIndex].Orientation.ToTK();
                    Matrix4 rotationMatrix = Matrix4.CreateFromQuaternion(rotationQuaternion);
                    Vector3 lookUp = Vector3.Transform(Vector3.UnitY, rotationMatrix);
                    Vector3 lookAt = Vector3.Transform(Vector3.UnitZ, rotationMatrix);

                    Vector3 viewPosition = playerPos + layerFov.RenderPose[eyeIndex].Position.ToTK();
                    Matrix4 view = Matrix4.LookAt(viewPosition, viewPosition + lookAt, lookUp);
                    Matrix4 proj = OVR.ovrMatrix4f_Projection(hmd.DefaultEyeFov[eyeIndex], 0.1f, 1000.0f, OVR.ProjectionModifier.RightHanded).ToTK();
                    proj.Transpose();

                    // OpenTK has Row Major Order and transposes matrices on the way to the shaders, thats why matrix multiplication is reverse order.
                    RenderScene(view * proj, worldCube);

                    // Unbind bound shared textures
                    eyeRenderTexture[eyeIndex].UnBind();
                }
            }

            // Do distortion rendering, Present and flush/sync
            OVR.ViewScaleDesc viewScale = new OVR.ViewScaleDesc()
            {
                HmdToEyeViewOffset = new OVR.Vector3f[] 
                      {
                          ViewOffset[0],
                          ViewOffset[1]
                      },
                HmdSpaceToWorldScaleInMeters = 1.0f
            };

            for (int eyeIndex = 0; eyeIndex < 2; eyeIndex++)
            {
                // Update layer
                layerFov.ColorTexture[eyeIndex] = eyeRenderTexture[eyeIndex].TextureSet.SwapTextureSetPtr;
                layerFov.Viewport[eyeIndex].Position = new OVR.Vector2i(0, 0);
                layerFov.Viewport[eyeIndex].Size = new OVR.Sizei(eyeRenderTexture[eyeIndex].Width, eyeRenderTexture[eyeIndex].Height);
                layerFov.Fov[eyeIndex] = EyeRenderDesc[eyeIndex].Fov;
            }
           
            OVR.ovrResult result = hmd.SubmitFrame(0, layers);

            isVisible = (result == OVR.ovrResult.Success);

            // Copy mirror data from mirror texture provided by OVR to backbuffer of the desktop window.
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, mirrorFbo);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            int w = mirrorTex.Texture.Header.TextureSize.Width;
            int h = mirrorTex.Texture.Header.TextureSize.Height;

            GL.BlitFramebuffer(
                0, h, w, 0,
                0, 0, w, h,
                ClearBufferMask.ColorBufferBit,
                BlitFramebufferFilter.Nearest);

            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);


            this.SwapBuffers();
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
            // Deleting buffers, rendertargets, dispose...
            eyeRenderTexture[0].CleanUp();
            eyeRenderTexture[1].CleanUp();

            GL.DeleteFramebuffers(1, ref mirrorFbo);

            GL.DeleteBuffer(cubeBuf);
            GL.DeleteBuffer(cubeColBuf);
            GL.DeleteBuffer(cubeIdxBuf);

            GL.DeleteProgram(cubeProgram);


            hmd.Dispose();
            wrap.Dispose();

            base.OnUnload(e);
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
        public static Quaternion ToTK(this OVR.Quaternionf quat)
        {
            return new Quaternion(quat.X, quat.Y, quat.Z, quat.W);
        }

        public static Vector3 ToTK(this OVR.Vector3f vec)
        {
            return new Vector3(vec.X, vec.Y, vec.Z);
        }

        public static Matrix4 ToTK(this OVR.Matrix4f mat)
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
