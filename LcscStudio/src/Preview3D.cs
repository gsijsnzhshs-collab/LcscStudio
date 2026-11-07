using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace LcscStudio
{
    internal sealed class ObjMesh
    {
        public readonly List<Point3D> Positions = new List<Point3D>();
        public readonly List<int> Indices = new List<int>();
        public double Extent = 10;

        public static ObjMesh Parse(byte[] bytes)
        {
            var mesh = new ObjMesh();
            var vertices = new List<Point3D>();

            using (var reader = new StreamReader(new MemoryStream(bytes)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length < 3) continue;
                    if (line[0] == 'v' && line[1] == ' ')
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 4) continue;
                        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                            continue;
                        vertices.Add(new Point3D(x, z, y));
                    }
                    else if (line[0] == 'f' && line[1] == ' ')
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 4) continue;
                        var indices = new List<int>(parts.Length - 1);
                        for (var i = 1; i < parts.Length; i++)
                        {
                            var token = parts[i];
                            var slash = token.IndexOf('/');
                            if (slash >= 0) token = token.Substring(0, slash);
                            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                                continue;
                            indices.Add(index < 0 ? vertices.Count + index : index - 1);
                        }

                        for (var i = 1; i + 1 < indices.Count; i++)
                        {
                            mesh.Indices.Add(indices[0]);
                            mesh.Indices.Add(indices[i + 1]);
                            mesh.Indices.Add(indices[i]);
                        }
                    }
                }
            }

            if (vertices.Count == 0) return mesh;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var vertex in vertices)
            {
                if (vertex.X < minX) minX = vertex.X;
                if (vertex.X > maxX) maxX = vertex.X;
                if (vertex.Y < minY) minY = vertex.Y;
                if (vertex.Y > maxY) maxY = vertex.Y;
                if (vertex.Z < minZ) minZ = vertex.Z;
                if (vertex.Z > maxZ) maxZ = vertex.Z;
            }

            var center = new Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            var extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            mesh.Extent = extent > 0.01 ? extent : 10;

            foreach (var vertex in vertices)
                mesh.Positions.Add(new Point3D(vertex.X - center.X, vertex.Y - center.Y, vertex.Z - center.Z));

            return mesh;
        }

        public MeshGeometry3D BuildGeometry()
        {
            var geometry = new MeshGeometry3D();
            var normals = new Vector3D[Positions.Count];
            var valid = 0;

            for (var i = 0; i + 2 < Indices.Count; i += 3)
            {
                var i0 = Indices[i];
                var i1 = Indices[i + 1];
                var i2 = Indices[i + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= Positions.Count || i1 >= Positions.Count || i2 >= Positions.Count)
                    continue;

                var normal = Vector3D.CrossProduct(
                    Positions[i1] - Positions[i0], Positions[i2] - Positions[i0]);
                if (normal.LengthSquared < 1e-12) continue;
                normal.Normalize();
                normals[i0] += normal;
                normals[i1] += normal;
                normals[i2] += normal;
                valid++;
            }

            if (valid == 0) return geometry;

            foreach (var position in Positions)
            {
                geometry.Positions.Add(position);
                var normal = normals[geometry.Positions.Count - 1];
                if (normal.LengthSquared > 1e-12) normal.Normalize();
                else normal = new Vector3D(0, 1, 0);
                geometry.Normals.Add(normal);
            }

            foreach (var index in Indices)
                geometry.TriangleIndices.Add(index);

            return geometry;
        }
    }

    internal sealed class Model3DPreview : Grid
    {
        private readonly Viewport3D _viewport = new Viewport3D();
        private readonly TextBlock _placeholder = new TextBlock
        {
            Text = "无 3D 数据",
            Foreground = Brushes.Gray,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        private readonly AxisAngleRotation3D _yaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 35);
        private readonly AxisAngleRotation3D _pitch = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -30);
        private readonly PerspectiveCamera _camera = new PerspectiveCamera
        {
            FieldOfView = 45,
            UpDirection = new Vector3D(0, 1, 0),
        };
        private readonly ModelVisual3D _modelVisual = new ModelVisual3D();

        private Point _lastMouse;
        private bool _dragging;
        private double _distance = 30;

        public Model3DPreview()
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(0x3A, 0x3E, 0x46), Color.FromRgb(0x1E, 0x21, 0x26), 90);
            _placeholder.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA8));
            Children.Add(_viewport);
            Children.Add(_placeholder);
            _viewport.Camera = _camera;

            _viewport.MouseLeftButtonDown += (s, e) =>
            {
                _dragging = true;
                _lastMouse = e.GetPosition(this);
                CaptureMouse();
            };
            _viewport.MouseLeftButtonUp += (s, e) =>
            {
                _dragging = false;
                ReleaseMouseCapture();
            };
            _viewport.MouseMove += (s, e) =>
            {
                if (!_dragging) return;
                var point = e.GetPosition(this);
                _yaw.Angle += (point.X - _lastMouse.X) * 0.5;
                _pitch.Angle = Math.Max(-89, Math.Min(89, _pitch.Angle - (point.Y - _lastMouse.Y) * 0.5));
                _lastMouse = point;
            };
            _viewport.MouseWheel += (s, e) =>
            {
                _distance = Math.Max(2, Math.Min(500, _distance * (e.Delta > 0 ? 0.88 : 1.12)));
                UpdateCamera();
            };

            SetMesh(null);
        }

        public void SetMesh(ObjMesh mesh)
        {
            if (mesh == null || mesh.Indices.Count == 0)
            {
                _modelVisual.Content = null;
                _viewport.Children.Clear();
                _viewport.Children.Add(_modelVisual);
                _placeholder.Visibility = Visibility.Visible;
                return;
            }

            _placeholder.Visibility = Visibility.Collapsed;


            var geometry = mesh.BuildGeometry();
            var material = new DiffuseMaterial(
                new SolidColorBrush(Color.FromRgb(0xC8, 0xD2, 0xDC)))
            {
                AmbientColor = Color.FromRgb(0xE0, 0xE0, 0xE0),
            };

            var model = new GeometryModel3D(geometry, material) { BackMaterial = material };
            var group = new Model3DGroup();
            group.Children.Add(new AmbientLight(Colors.White));
            group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -2, -3)));
            group.Children.Add(new DirectionalLight(Color.FromRgb(0x80, 0x80, 0x80), new Vector3D(2, 1, 2)));
            group.Children.Add(model);

            var transforms = new Transform3DGroup();
            transforms.Children.Add(new RotateTransform3D(_yaw));
            transforms.Children.Add(new RotateTransform3D(_pitch));
            model.Transform = transforms;

            _modelVisual.Content = group;
            _viewport.Children.Clear();
            _viewport.Children.Add(_modelVisual);

            _distance = mesh.Extent * 2.0 + 4;
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            _camera.Position = new Point3D(0, 0, _distance);
            _camera.LookDirection = new Vector3D(0, 0, -_distance);
            _camera.NearPlaneDistance = Math.Max(0.01, _distance * 0.01);
            _camera.FarPlaneDistance = _distance * 10;
        }
    }
}
