﻿using System;
using OpenTK.Graphics.OpenGL;

namespace WindViewer.Editor.Renderer
{
    public sealed class J3DRenderer : IRenderer
    {
        public J3DRenderer()
        {
            InitializeShader("shaders/j3d_vs.glsl", "shaders/j3d_fs.glsl");
        }

        protected override void InitializeShader(string vertShader, string fragShader)
        {
            //Initialize the OpenGL Program
            _programId = GL.CreateProgram();

            int vertShaderId, fragShaderId;
            LoadShader(vertShader, ShaderType.VertexShader, _programId, out vertShaderId);
            LoadShader(fragShader, ShaderType.FragmentShader, _programId, out fragShaderId);

            

            //Deincriment the reference count on the shaders so that they 
            //don't exist until the context is destroyed.
            GL.DeleteShader(vertShaderId);
            GL.DeleteShader(fragShaderId);

            GL.BindAttribLocation(_programId, (int) ShaderAttributeIds.Position, "vertexPos");
            GL.BindAttribLocation(_programId, (int)ShaderAttributeIds.Color, "inColor");
            GL.BindAttribLocation(_programId, (int)ShaderAttributeIds.TexCoord, "vertexUV");

            //Link shaders 
            GL.LinkProgram(_programId);

            if (GL.GetError() != ErrorCode.NoError)
                Console.WriteLine(GL.GetProgramInfoLog(_programId));

            //Temp
            float[] vertices = new[]
            {
                //  Position      Color             Texcoords
                -0.5f,  0.5f, 0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, // Top-left
                 0.5f,  0.5f, 0f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, // Top-right
                 0.5f, -0.5f, 0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, // Bottom-right
                -0.5f, -0.5f, 0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f  // Bottom-left
            };

            uint[] indexes = {0, 1, 2, 2, 3, 0};


            //Generate the VBO, Bind, and Upload Data
            GL.GenBuffers(1, out _glVbo);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glVbo);
            
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr) (vertices.Length*4), vertices, BufferUsageHint.StaticDraw);

            //Generate the EBO, Bind, and Upload Data
            GL.GenBuffers(1, out _glEbo);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _glEbo);

            GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr) (indexes.Length*4), indexes,
                BufferUsageHint.StaticDraw);

            //Generate the texture, Bind, and Upload Data
            GL.GenTextures(1, out _glTex);
            GL.BindTexture(TextureTarget.Texture2D, _glTex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)All.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)All.Repeat);

            // Black/white checkerboard
            float[] pixels =
            {
                0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f,
                1.0f, 1.0f, 1.0f, 0.0f, 0.0f, 0.0f
            };

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, 2, 2, 0, PixelFormat.Rgb, PixelType.Float,
                pixels);

        }

        

        //Holy temporary batmans
        private int _glVbo;
        private int _glEbo;
        private int _glTex;

        public override void Render(Camera camera, float aspectRatio)
        {
            GL.UseProgram(_programId);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0); //Clear any previously bound buffer

            //Enable Attributes for Shader
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glVbo);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _glEbo);
            GL.BindTexture(TextureTarget.Texture2D, _glTex);

            GL.EnableVertexAttribArray((int) ShaderAttributeIds.Position);
            GL.EnableVertexAttribArray((int)ShaderAttributeIds.Color);
            GL.EnableVertexAttribArray((int)ShaderAttributeIds.TexCoord);

            GL.VertexAttribPointer((int)ShaderAttributeIds.Position, 3, VertexAttribPointerType.Float, false, 8*4 , 0);
            GL.VertexAttribPointer((int)ShaderAttributeIds.Color, 3, VertexAttribPointerType.Float, false, 8*4 , 3 *4);
            GL.VertexAttribPointer((int)ShaderAttributeIds.TexCoord, 2, VertexAttribPointerType.Float, false, 8*4, 6* 4);


            //FFS
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);

            GL.DisableVertexAttribArray((int) ShaderAttributeIds.Position);
            GL.DisableVertexAttribArray((int)ShaderAttributeIds.Color);
            GL.DisableVertexAttribArray((int)ShaderAttributeIds.TexCoord);
            GL.Flush();
        }
    }
}