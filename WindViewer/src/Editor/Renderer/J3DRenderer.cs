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

            //Link shaders 
            GL.LinkProgram(_programId);

            //Deincriment the reference count on the shaders so that they 
            //don't exist until the context is destroyed.
            //GL.DeleteShader(vertShaderId);
            //GL.DeleteShader(fragShaderId);

            GL.BindAttribLocation(_programId, (int) ShaderAttributeIds.Position, "vertexPos");
            GL.BindAttribLocation(_programId, (int)ShaderAttributeIds.Color, "inColor");
            Console.WriteLine(GL.GetError());

            //if (GL.GetError() != ErrorCode.NoError)
                Console.WriteLine(GL.GetProgramInfoLog(_programId));

            //
            float[] vertices = new[]
            {
                0.0f,  0.5f, 0f, 1f, 0f, 0f, // Vertex 1: Red
                0.5f, -0.5f,  0f, 0f, 1f, 0f, // Vertex 2: Green
                -0.5f, -0.5f, 0f, 0f, 0f, 1f// Vertex 3: Blue
            };


            GL.GenBuffers(1, out _glVbo);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glVbo);
            
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr) (vertices.Length*4), vertices, BufferUsageHint.StaticDraw);

            Console.WriteLine(GL.GetError());
        }

        

        //Holy temporary batmans
        private int _glVbo;
        private int _glColor;

        public override void Render(Camera camera, float aspectRatio)
        {
            GL.UseProgram(_programId);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0); //Clear any previously bound buffer

            //Enable Attributes for Shader
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glVbo);

            GL.EnableVertexAttribArray((int) ShaderAttributeIds.Position);
            GL.VertexAttribPointer((int)ShaderAttributeIds.Position, 3, VertexAttribPointerType.Float, false, 6*4 , 0);

            GL.EnableVertexAttribArray((int)ShaderAttributeIds.Color);
            GL.VertexAttribPointer((int)ShaderAttributeIds.Color, 3, VertexAttribPointerType.Float, false, 6*4 , 3 * 4);


            //FFS
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.DisableVertexAttribArray((int) ShaderAttributeIds.Position);
            GL.DisableVertexAttribArray((int)ShaderAttributeIds.Color);
            GL.Flush();
        }
    }
}