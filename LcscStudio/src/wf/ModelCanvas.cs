using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace LcscStudio
{
    // EasyEDA 的 3D 模型是 OBJ 三角网格：优先按连通块、焊死的网格再按材质把本体与引脚区分开。
    internal sealed class ObjMesh
    {
        public float[] Vertices = new float[0];      // 连续的 x,y,z
        public int[] Triangles = new int[0];         // 每 3 个索引一个三角形
        public int[] TriangleComponent = new int[0]; // 每个三角形所属的连通块
        public int CaseComponent = -1;               // 面积最大的连通块视为器件本体
        public bool[] FlatComponent = new bool[0];   // 没有厚度的贴片（丝印/标记），不算引脚
        public bool[] PinTriangle = new bool[0];     // 每个三角形是否属于引脚
        public float CenterX, CenterY, CenterZ;
        public float Radius = 1f;

        public int TriangleCount => Triangles.Length / 3;

        public static ObjMesh Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream))
                return Parse(reader);
        }

        private static ObjMesh Parse(TextReader reader)
        {
            var vertices = new List<float>(1 << 15);
            var triangles = new List<int>(1 << 16);
            var triangleMaterial = new List<int>(1 << 16);
            var materialOf = new Dictionary<string, int>();
            var face = new List<int>(8);
            var material = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var text = line.Trim();
                if (text.Length < 2) continue;
                if (text[0] == 'v' && text[1] == ' ') ReadVertex(text, vertices);
                else if (text[0] == 'f' && text[1] == ' ') ReadFace(text, vertices.Count / 3, triangles, face, triangleMaterial, material);
                else if (text.StartsWith("usemtl ", StringComparison.Ordinal)) material = MaterialId(materialOf, text.Substring(7).Trim());
            }

            var mesh = new ObjMesh
            {
                Vertices = vertices.ToArray(),
                Triangles = triangles.ToArray(),
            };
            mesh.Measure();
            mesh.GroupComponents(triangleMaterial);
            return mesh;
        }

        private static int MaterialId(Dictionary<string, int> materialOf, string name)
        {
            if (materialOf.TryGetValue(name, out var id)) return id;
            id = materialOf.Count;
            materialOf[name] = id;
            return id;
        }

        private static void ReadVertex(string text, List<float> vertices)
        {
            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return;
            if (!TryFloat(parts[1], out var x)) return;
            if (!TryFloat(parts[2], out var y)) return;
            if (!TryFloat(parts[3], out var z)) return;
            vertices.Add(x);
            vertices.Add(y);
            vertices.Add(z);
        }

        private static void ReadFace(string text, int vertexCount, List<int> triangles, List<int> face, List<int> triangleMaterial, int material)
        {
            face.Clear();
            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 1; i < parts.Length; i++)
            {
                var token = parts[i];
                var slash = token.IndexOf('/');
                if (slash >= 0) token = token.Substring(0, slash);
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) return;
                if (index < 0) index += vertexCount;
                else index -= 1;
                if (index < 0 || index >= vertexCount) return;
                face.Add(index);
            }

            // 多边形面按扇形三角化，OBJ 里常有四边形。
            for (var i = 1; i + 1 < face.Count; i++)
            {
                triangles.Add(face[0]);
                triangles.Add(face[i]);
                triangles.Add(face[i + 1]);
                triangleMaterial.Add(material);
            }
        }

        private static bool TryFloat(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            value = 0f;
            return false;
        }

        private void Measure()
        {
            if (Vertices.Length < 3) return;
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var minZ = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            var maxZ = float.MinValue;
            for (var i = 0; i < Vertices.Length; i += 3)
            {
                minX = Math.Min(minX, Vertices[i]);
                maxX = Math.Max(maxX, Vertices[i]);
                minY = Math.Min(minY, Vertices[i + 1]);
                maxY = Math.Max(maxY, Vertices[i + 1]);
                minZ = Math.Min(minZ, Vertices[i + 2]);
                maxZ = Math.Max(maxZ, Vertices[i + 2]);
            }

            CenterX = (minX + maxX) / 2f;
            CenterY = (minY + maxY) / 2f;
            CenterZ = (minZ + maxZ) / 2f;
            var dx = maxX - minX;
            var dy = maxY - minY;
            var dz = maxZ - minZ;
            Radius = (float)(Math.Sqrt(dx * dx + dy * dy + dz * dz) / 2);
            if (Radius < 0.0001f) Radius = 1f;
        }

        // 模型导出时本体和各根引脚是彼此独立的网格，用并查集合并共享顶点的三角形，
        // 面积最大的连通块就是本体（case），其余连通块当作引脚等附属件。
        private void GroupComponents(List<int> triangleMaterial)
        {
            var vertexCount = Vertices.Length / 3;
            var triangleCount = TriangleCount;
            if (vertexCount == 0 || triangleCount == 0) return;

            var parent = new int[vertexCount];
            for (var i = 0; i < vertexCount; i++) parent[i] = i;
            for (var t = 0; t < triangleCount; t++)
            {
                var a = Triangles[t * 3];
                var b = Triangles[t * 3 + 1];
                var c = Triangles[t * 3 + 2];
                Union(parent, a, b);
                Union(parent, b, c);
            }

            var componentOfRoot = new Dictionary<int, int>(64);
            var areas = new List<double>(64);
            var componentById = new int[triangleCount];
            var minXs = new List<float>(64);
            var minYs = new List<float>(64);
            var minZs = new List<float>(64);
            var maxXs = new List<float>(64);
            var maxYs = new List<float>(64);
            var maxZs = new List<float>(64);
            for (var t = 0; t < triangleCount; t++)
            {
                var root = Find(parent, Triangles[t * 3]);
                if (!componentOfRoot.TryGetValue(root, out var component))
                {
                    component = areas.Count;
                    componentOfRoot[root] = component;
                    areas.Add(0);
                    minXs.Add(float.MaxValue);
                    minYs.Add(float.MaxValue);
                    minZs.Add(float.MaxValue);
                    maxXs.Add(float.MinValue);
                    maxYs.Add(float.MinValue);
                    maxZs.Add(float.MinValue);
                }

                areas[component] += TriangleArea(t);
                componentById[t] = component;
            }

            for (var v = 0; v < vertexCount; v++)
            {
                if (!componentOfRoot.TryGetValue(Find(parent, v), out var component)) continue;
                var x = Vertices[v * 3];
                var y = Vertices[v * 3 + 1];
                var z = Vertices[v * 3 + 2];
                if (x < minXs[component]) minXs[component] = x;
                if (x > maxXs[component]) maxXs[component] = x;
                if (y < minYs[component]) minYs[component] = y;
                if (y > maxYs[component]) maxYs[component] = y;
                if (z < minZs[component]) minZs[component] = z;
                if (z > maxZs[component]) maxZs[component] = z;
            }

            var largest = 0;
            for (var i = 1; i < areas.Count; i++)
                if (areas[i] > areas[largest]) largest = i;

            // 丝印/标记是近似共面的薄贴片，厚度远小于自身长宽，按本体颜色画，避免误判成引脚。
            var flatLimit = Radius * 2f * 0.002f;
            var flat = new bool[areas.Count];
            for (var i = 0; i < areas.Count; i++)
            {
                var extentX = maxXs[i] - minXs[i];
                var extentY = maxYs[i] - minYs[i];
                var extentZ = maxZs[i] - minZs[i];
                var thickness = Math.Min(extentX, Math.Min(extentY, extentZ));
                var longest = Math.Max(extentX, Math.Max(extentY, extentZ));
                // 引脚是细长柱体，最薄方向通常还有长边的 1/5 以上；不到 8% 的基本就是贴片。
                flat[i] = thickness < flatLimit || thickness < longest * 0.08f;
            }

            TriangleComponent = componentById;
            CaseComponent = areas.Count > 0 ? largest : -1;
            FlatComponent = flat;

            // 本体和引脚焊成一个连通块时，连通块没法拆，改按材质分组识别引脚。
            PinTriangle = new bool[triangleCount];
            if (areas.Count > 1)
            {
                for (var t = 0; t < triangleCount; t++)
                {
                    var component = componentById[t];
                    PinTriangle[t] = component != largest && !flat[component];
                }
            }
            else
            {
                MarkPinsByMaterial(triangleMaterial);
            }
        }

        // 焊死的网格里，引脚是贴着 PCB 底部、最薄的那组材质；其余（本体、丝印）都不算。
        private void MarkPinsByMaterial(List<int> triangleMaterial)
        {
            var triangleCount = TriangleCount;
            if (triangleMaterial.Count != triangleCount) return;

            var slots = new Dictionary<int, int>(4);
            var minZs = new List<float>(4);
            var maxZs = new List<float>(4);
            for (var t = 0; t < triangleCount; t++)
            {
                if (!slots.TryGetValue(triangleMaterial[t], out var slot))
                {
                    slot = slots.Count;
                    slots[triangleMaterial[t]] = slot;
                    minZs.Add(float.MaxValue);
                    maxZs.Add(float.MinValue);
                }

                for (var k = 0; k < 3; k++)
                {
                    var z = Vertices[Triangles[t * 3 + k] * 3 + 2];
                    if (z < minZs[slot]) minZs[slot] = z;
                    if (z > maxZs[slot]) maxZs[slot] = z;
                }
            }

            if (slots.Count < 2) return;

            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            for (var i = 0; i < minZs.Count; i++)
            {
                if (minZs[i] < minZ) minZ = minZs[i];
                if (maxZs[i] > maxZ) maxZ = maxZs[i];
            }

            var height = maxZ - minZ;
            if (height <= 0f) return;

            var best = -1;
            var bestThickness = float.MaxValue;
            for (var i = 0; i < minZs.Count; i++)
            {
                if (minZs[i] > minZ + height * 0.15f) continue; // 必须贴在底部
                var thickness = maxZs[i] - minZs[i];
                if (thickness >= bestThickness) continue;
                bestThickness = thickness;
                best = i;
            }

            // 兜底材质若同时压着底部和顶部、几乎占满整个高度，说明它其实是本体外壳。
            // D882 这类立式器件的引脚材质会一直伸进本体内，厚度同样接近全高，不能只看厚度。
            if (best < 0)
            {
                MarkPinsBySilhouette();
                return;
            }
            if (bestThickness > height * 0.7f && maxZs[best] > maxZ - height * 0.02f)
            {
                // 本体和引脚是同一块焊死网格、同一材质（TO-220 常见），材质分不开，改按外形切。
                MarkPinsBySilhouette();
                return;
            }

            for (var t = 0; t < triangleCount; t++)
                PinTriangle[t] = slots[triangleMaterial[t]] == best;
        }

        // 沿竖直方向把模型切成若干层，本体的截面明显比引脚粗；
        // 找到第一层“够宽”的高度当作本体底面，底面以下的三角形就是伸出来的引脚。
        private void MarkPinsBySilhouette()
        {
            var triangleCount = TriangleCount;
            if (triangleCount == 0) return;

            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            for (var i = 2; i < Vertices.Length; i += 3)
            {
                if (Vertices[i] < minZ) minZ = Vertices[i];
                if (Vertices[i] > maxZ) maxZ = Vertices[i];
            }

            var height = maxZ - minZ;
            if (height <= 0.0001f) return;

            const int bins = 40;
            var sliceMinX = new float[bins];
            var sliceMaxX = new float[bins];
            var sliceMinY = new float[bins];
            var sliceMaxY = new float[bins];
            for (var i = 0; i < bins; i++)
            {
                sliceMinX[i] = float.MaxValue;
                sliceMaxX[i] = float.MinValue;
                sliceMinY[i] = float.MaxValue;
                sliceMaxY[i] = float.MinValue;
            }

            var step = height / bins;
            for (var t = 0; t < triangleCount; t++)
            {
                var low = float.MaxValue;
                var high = float.MinValue;
                for (var k = 0; k < 3; k++)
                {
                    var z = Vertices[Triangles[t * 3 + k] * 3 + 2];
                    if (z < low) low = z;
                    if (z > high) high = z;
                }

                var first = Math.Min(bins - 1, Math.Max(0, (int)((low - minZ) / step)));
                var last = Math.Min(bins - 1, Math.Max(0, (int)((high - minZ) / step)));
                for (var b = first; b <= last; b++)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        var v = Triangles[t * 3 + k] * 3;
                        var x = Vertices[v];
                        var y = Vertices[v + 1];
                        if (x < sliceMinX[b]) sliceMinX[b] = x;
                        if (x > sliceMaxX[b]) sliceMaxX[b] = x;
                        if (y < sliceMinY[b]) sliceMinY[b] = y;
                        if (y > sliceMaxY[b]) sliceMaxY[b] = y;
                    }
                }
            }

            var bodyWidth = 0f;
            for (var i = 0; i < bins; i++)
            {
                if (sliceMinX[i] > sliceMaxX[i]) continue;
                var width = Math.Min(sliceMaxX[i] - sliceMinX[i], sliceMaxY[i] - sliceMinY[i]);
                if (width > bodyWidth) bodyWidth = width;
            }

            if (bodyWidth <= 0f) return;

            var limit = bodyWidth * 0.55f;
            var bottom = float.MinValue;
            for (var i = 0; i < bins; i++)
            {
                if (sliceMinX[i] > sliceMaxX[i]) continue;
                var width = Math.Min(sliceMaxX[i] - sliceMinX[i], sliceMaxY[i] - sliceMinY[i]);
                if (width < limit) continue;
                bottom = minZ + i * step;
                break;
            }

            // 切面贴在最底部（整个模型都一样粗）或太靠上（切到本体）都不动，保持原样。
            if (bottom <= minZ + height * 0.02f || bottom >= minZ + height * 0.6f) return;

            var cut = bottom + height * 0.02f;
            for (var t = 0; t < triangleCount; t++)
            {
                var high = float.MinValue;
                for (var k = 0; k < 3; k++)
                {
                    var z = Vertices[Triangles[t * 3 + k] * 3 + 2];
                    if (z > high) high = z;
                }

                if (high <= cut) PinTriangle[t] = true;
            }
        }

        public bool IsPin(int triangle)
        {
            if (PinTriangle.Length != TriangleCount) return false;
            return PinTriangle[triangle];
        }

        private double TriangleArea(int triangle)
        {
            var i0 = Triangles[triangle * 3] * 3;
            var i1 = Triangles[triangle * 3 + 1] * 3;
            var i2 = Triangles[triangle * 3 + 2] * 3;
            var ax = Vertices[i1] - Vertices[i0];
            var ay = Vertices[i1 + 1] - Vertices[i0 + 1];
            var az = Vertices[i1 + 2] - Vertices[i0 + 2];
            var bx = Vertices[i2] - Vertices[i0];
            var by = Vertices[i2 + 1] - Vertices[i0 + 1];
            var bz = Vertices[i2 + 2] - Vertices[i0 + 2];
            var cx = ay * bz - az * by;
            var cy = az * bx - ax * bz;
            var cz = ax * by - ay * bx;
            return Math.Sqrt(cx * cx + cy * cy + cz * cz) / 2;
        }

        private static int Find(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        private static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a);
            b = Find(parent, b);
            if (a != b) parent[b] = a;
        }
    }

    // 3D 预览：软件渲染 + 画家算法；本体深灰、引脚亮银，左键拖动旋转，滚轮缩放，双击复位。
    internal sealed class Model3DCanvas : ZoomCanvas
    {
        private enum ModelState
        {
            Empty,
            NoModel,
            Loading,
            Failed,
            Ready,
        }

        private const float DefaultYaw = -0.55f;
        private const float DefaultPitch = 0.38f;
        private const int ShadeSteps = 24;

        // 本体压暗、引脚提亮，两组色阶完全不重叠：任何朝向的引脚都比本体亮。
        private static readonly Color[] CaseShades = BuildRamp(0x2A, 0x2F, 0x35, 0x6E, 0x78, 0x84);
        private static readonly Color[] PinShades = BuildRamp(0xA8, 0xB2, 0xBC, 0xF4, 0xF9, 0xFD);

        private readonly SolidBrush _brush = new SolidBrush(Color.Black);
        private readonly PointF[] _triangle = new PointF[3];

        private ObjMesh _mesh;
        private ModelState _state = ModelState.Empty;
        private float _yaw = DefaultYaw;
        private float _pitch = DefaultPitch;
        private float[] _screenX = new float[0];
        private float[] _screenY = new float[0];
        private float[] _depth = new float[0];
        private int[] _order = new int[0];
        private float[] _triangleDepth = new float[0];
        private bool _rotating;
        private Point _rotateFrom;

        public bool HasMesh => _state == ModelState.Ready && _mesh != null;

        public void ShowEmpty() => SetState(ModelState.Empty);

        public void ShowNoModel() => SetState(ModelState.NoModel);

        public void ShowLoading() => SetState(ModelState.Loading);

        public void ShowFailed() => SetState(ModelState.Failed);

        private void SetState(ModelState state)
        {
            _state = state;
            _mesh = null;
            Invalidate();
        }

        public void SetMesh(ObjMesh mesh)
        {
            _mesh = mesh;
            _state = mesh != null && mesh.TriangleCount > 0 ? ModelState.Ready : ModelState.Failed;
            ResetView();
        }

        public override void ResetView()
        {
            base.ResetView();
            _yaw = DefaultYaw;
            _pitch = DefaultPitch;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Canvas);
            DrawGrid(g);
            if (_state != ModelState.Ready || _mesh == null || _mesh.TriangleCount == 0)
            {
                DrawEmpty(g);
                return;
            }

            DrawMesh(g);
            using (var brush = new SolidBrush(Theme.TextFaint))
                g.DrawString("拖动旋转 · 滚轮缩放 · 双击复位", Theme.LabelFont, brush, 6,
                    Height - Theme.LabelFont.Height - 6);
        }

        private void DrawEmpty(Graphics g)
        {
            string title;
            string hint;
            switch (_state)
            {
                case ModelState.NoModel:
                    title = "该器件没有 3D 模型";
                    hint = "可在封装页查看 2D 图形";
                    break;
                case ModelState.Loading:
                    title = "正在加载 3D 模型";
                    hint = "首次加载需要下载模型文件";
                    break;
                case ModelState.Failed:
                    title = "3D 模型加载失败";
                    hint = "稍后重试，或先导入到 AD 中查看";
                    break;
                default:
                    title = "还没有 3D 模型";
                    hint = "搜索型号后在此查看 3D 预览";
                    break;
            }

            PreviewEmpty.Draw(g, ClientSize, title, hint, false);
        }

        private void DrawMesh(Graphics g)
        {
            var mesh = _mesh;
            var vertexCount = mesh.Vertices.Length / 3;
            var triangleCount = mesh.TriangleCount;
            EnsureBuffers(vertexCount, triangleCount);

            var cosYaw = (float)Math.Cos(_yaw);
            var sinYaw = (float)Math.Sin(_yaw);
            var cosPitch = (float)Math.Cos(_pitch);
            var sinPitch = (float)Math.Sin(_pitch);
            var vertices = mesh.Vertices;
            for (var i = 0; i < vertexCount; i++)
            {
                var x = vertices[i * 3] - mesh.CenterX;
                var y = vertices[i * 3 + 1] - mesh.CenterY;
                var z = vertices[i * 3 + 2] - mesh.CenterZ;

                // 模型是 Z 向上的，绕 Z 偏航后绕 X 俯仰；屏幕 y 向下，故取反。
                var rx = x * cosYaw - y * sinYaw;
                var ry = x * sinYaw + y * cosYaw;
                var ry2 = ry * cosPitch - z * sinPitch;
                var rz2 = ry * sinPitch + z * cosPitch;
                _screenX[i] = rx;
                _screenY[i] = -rz2;
                _depth[i] = ry2;
            }

            const float pad = 18f;
            var fit = Math.Min(Width - pad * 2, Height - pad * 2) / (2f * mesh.Radius);
            if (fit <= 0) fit = 1f;
            var scale = fit * ViewZoom;
            var originX = Width / 2f;
            var originY = Height / 2f;

            var triangles = mesh.Triangles;
            for (var t = 0; t < triangleCount; t++)
            {
                var i0 = triangles[t * 3];
                var i1 = triangles[t * 3 + 1];
                var i2 = triangles[t * 3 + 2];
                _triangleDepth[t] = (_depth[i0] + _depth[i1] + _depth[i2]) / 3f;
                _order[t] = t;
            }

            // 升序排序后从末尾开始画，即由远及近。
            Array.Sort(_triangleDepth, _order, 0, triangleCount);

            // 左上前方的平行光，方向已归一化。
            const float lightX = -0.38f;
            const float lightY = -0.55f;
            const float lightZ = -0.74f;

            var hasParts = mesh.PinTriangle.Length == triangleCount;
            g.SmoothingMode = SmoothingMode.None;
            for (var n = triangleCount - 1; n >= 0; n--)
            {
                var t = _order[n];
                var i0 = triangles[t * 3];
                var i1 = triangles[t * 3 + 1];
                var i2 = triangles[t * 3 + 2];

                var ax = _screenX[i1] - _screenX[i0];
                var ay = _screenY[i1] - _screenY[i0];
                var az = _depth[i1] - _depth[i0];
                var bx = _screenX[i2] - _screenX[i0];
                var by = _screenY[i2] - _screenY[i0];
                var bz = _depth[i2] - _depth[i0];

                // 背面剔除：法线朝里的三角形看不见，丢掉可显著减少密集网格的绘制量。
                if (ax * by - ay * bx >= 0) continue;

                var nx = ay * bz - az * by;
                var ny = az * bx - ax * bz;
                var nz = ax * by - ay * bx;
                var length = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length < 0.000001f) continue;

                // 双面着色：网格朝向不统一时也不会出现全黑面。
                var dot = Math.Abs((nx * lightX + ny * lightY + nz * lightZ) / length);
                var shade = (int)((0.28f + 0.72f * dot) * (ShadeSteps - 1));
                if (shade < 0) shade = 0;
                else if (shade >= ShadeSteps) shade = ShadeSteps - 1;

                _triangle[0] = new PointF(originX + _screenX[i0] * scale, originY + _screenY[i0] * scale);
                _triangle[1] = new PointF(originX + _screenX[i1] * scale, originY + _screenY[i1] * scale);
                _triangle[2] = new PointF(originX + _screenX[i2] * scale, originY + _screenY[i2] * scale);

                _brush.Color = (hasParts && mesh.IsPin(t) ? PinShades : CaseShades)[shade];
                g.FillPolygon(_brush, _triangle);
            }
        }

        private void EnsureBuffers(int vertexCount, int triangleCount)
        {
            if (_screenX.Length < vertexCount)
            {
                _screenX = new float[vertexCount];
                _screenY = new float[vertexCount];
                _depth = new float[vertexCount];
            }

            if (_order.Length < triangleCount)
            {
                _order = new int[triangleCount];
                _triangleDepth = new float[triangleCount];
            }
        }

        private void DrawGrid(Graphics g)
        {
            const int step = 24;
            using (var pen = new Pen(HasMesh ? Theme.CanvasGrid : Theme.CanvasGridIdle))
            {
                for (var x = 0; x < Width; x += step) g.DrawLine(pen, x, 0, x, Height);
                for (var y = 0; y < Height; y += step) g.DrawLine(pen, 0, y, Width, y);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseDown(e);
                return;
            }

            Focus();
            _rotating = true;
            _rotateFrom = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_rotating)
            {
                base.OnMouseMove(e);
                return;
            }

            _yaw += (e.X - _rotateFrom.X) * 0.012f;
            _pitch += (e.Y - _rotateFrom.Y) * 0.012f;
            const float limit = 1.45f;
            _pitch = Math.Max(-limit, Math.Min(limit, _pitch));
            _rotateFrom = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_rotating) return;
            _rotating = false;
            Capture = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var zoom = ViewZoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f);
            zoom = Math.Max(0.2f, Math.Min(8f, zoom));
            if (Math.Abs(zoom - ViewZoom) < 0.0001f) return;
            ViewZoom = zoom;
            Invalidate();
        }

        private static Color[] BuildRamp(int r0, int g0, int b0, int r1, int g1, int b1)
        {
            var ramp = new Color[ShadeSteps];
            for (var i = 0; i < ramp.Length; i++)
            {
                var k = i / (float)(ramp.Length - 1);
                ramp[i] = Color.FromArgb(
                    (int)Math.Round(r0 + (r1 - r0) * k),
                    (int)Math.Round(g0 + (g1 - g0) * k),
                    (int)Math.Round(b0 + (b1 - b0) * k));
            }

            return ramp;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _brush.Dispose();
            base.Dispose(disposing);
        }
    }
}
